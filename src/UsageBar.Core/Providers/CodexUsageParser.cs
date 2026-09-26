using System.Globalization;
using System.Text.Json;
using UsageBar.Core.Models;

namespace UsageBar.Core.Providers;

public static class CodexUsageParser
{
    public static ProviderUsage Parse(JsonElement response, DateTimeOffset fetchedAt)
    {
        var snapshot = response;
        if (TryGetProperty(response, "result", out var result))
        {
            snapshot = result;
        }

        if (!TryGetProperty(snapshot, "rateLimits", out var limits) || limits.ValueKind != JsonValueKind.Object)
        {
            if (TryGetProperty(snapshot, "rateLimitsByLimitId", out var allLimits) &&
                allLimits.ValueKind == JsonValueKind.Object && TryGetProperty(allLimits, "codex", out var codexLimits))
            {
                limits = codexLimits;
            }
            else
            {
                throw new UsageProviderException("Codex did not return its rate-limit snapshot.", ProviderStatus.Unavailable);
            }
        }

        var fiveHour = UsageWindow.Unknown(300);
        var weekly = UsageWindow.Unknown(10080);
        if (TryGetProperty(limits, "primary", out var primary))
        {
            AssignWindow(primary, ref fiveHour, ref weekly);
        }
        if (TryGetProperty(limits, "secondary", out var secondary))
        {
            AssignWindow(secondary, ref fiveHour, ref weekly);
        }

        if (fiveHour.UsedPercent is null && weekly.UsedPercent is null)
        {
            throw new UsageProviderException("Codex returned no 5-hour or weekly usage windows.", ProviderStatus.Unavailable);
        }

        return new ProviderUsage(
            "codex",
            "Codex",
            [new UsageGroup("Codex", fiveHour, weekly)],
            ProviderStatus.Connected,
            fetchedAt,
            fetchedAt);
    }

    private static void AssignWindow(JsonElement element, ref UsageWindow fiveHour, ref UsageWindow weekly)
    {
        var duration = TryGetInt64(element, "windowDurationMins");
        if (duration is null)
        {
            return;
        }

        var mapped = ParseWindow(element, (int)Math.Clamp(duration.Value, int.MinValue, int.MaxValue));
        if (duration is >= 240 and <= 360)
        {
            fiveHour = mapped;
        }
        else if (duration is >= 6000 and <= 12000)
        {
            weekly = mapped;
        }
    }

    private static UsageWindow ParseWindow(JsonElement element, int durationMinutes)
    {
        var used = TryGetDouble(element, "usedPercent");
        var reset = TryGetInt64(element, "resetsAt");
        if (used is null || used is < 0 or > 100)
        {
            return UsageWindow.Unknown(durationMinutes);
        }

        DateTimeOffset? resetAt = null;
        if (reset is > 0)
        {
            try { resetAt = DateTimeOffset.FromUnixTimeSeconds(reset.Value); }
            catch (ArgumentOutOfRangeException) { }
        }

        var normalizedUsed = Math.Round(used.Value, 1, MidpointRounding.AwayFromZero);
        return new UsageWindow(normalizedUsed, Math.Round(100 - normalizedUsed, 1), resetAt, durationMinutes);
    }

    internal static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    internal static long? TryGetInt64(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), CultureInfo.InvariantCulture, out number)) return number;
        return null;
    }

    internal static double? TryGetDouble(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)) return number;
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number)) return number;
        return null;
    }
}
