using System.Globalization;
using System.Text.Json;
using UsageBar.Core.Models;

namespace UsageBar.Core.Providers;

public static class AntigravityUsageParser
{
    public static ProviderUsage Parse(JsonElement response, DateTimeOffset fetchedAt)
    {
        var root = response;
        if (CodexUsageParser.TryGetProperty(response, "response", out var nested)) root = nested;
        if (!CodexUsageParser.TryGetProperty(root, "groups", out var groupsElement) || groupsElement.ValueKind != JsonValueKind.Array)
        {
            throw new UsageProviderException("Antigravity did not return quota groups.", ProviderStatus.Unavailable);
        }

        var groups = new List<UsageGroup>();
        foreach (var groupElement in groupsElement.EnumerateArray())
        {
            if (!CodexUsageParser.TryGetProperty(groupElement, "displayName", out var displayNameElement) ||
                displayNameElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var groupName = displayNameElement.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(groupName)) continue;

            UsageWindow? fiveHour = null;
            UsageWindow? weekly = null;
            if (CodexUsageParser.TryGetProperty(groupElement, "buckets", out var buckets) && buckets.ValueKind == JsonValueKind.Array)
            {
                foreach (var bucket in buckets.EnumerateArray())
                {
                    var kind = GetWindowKind(bucket);
                    if (kind is null) continue;
                    var parsed = ParseBucket(bucket, kind == WindowKind.FiveHour ? 300 : 10080);
                    if (kind == WindowKind.FiveHour)
                    {
                        fiveHour = MoreConstrained(fiveHour, parsed);
                    }
                    else
                    {
                        weekly = MoreConstrained(weekly, parsed);
                    }
                }
            }

            if (fiveHour is not null || weekly is not null)
            {
                groups.Add(new UsageGroup(groupName, fiveHour, weekly));
            }
        }

        if (groups.Count == 0 || groups.All(g => g.FiveHour?.UsedPercent is null && g.Weekly?.UsedPercent is null))
        {
            throw new UsageProviderException("Antigravity returned no known quota percentages.", ProviderStatus.Unavailable);
        }

        return new ProviderUsage("antigravity", "Antigravity", groups, ProviderStatus.Connected, fetchedAt, fetchedAt);
    }

    private static UsageWindow MoreConstrained(UsageWindow? existing, UsageWindow candidate)
    {
        if (existing?.RemainingPercent is null) return candidate;
        if (candidate.RemainingPercent is null) return existing;
        return candidate.RemainingPercent < existing.RemainingPercent ? candidate : existing;
    }

    private static UsageWindow ParseBucket(JsonElement bucket, int durationMinutes)
    {
        double? remaining = null;
        if (CodexUsageParser.TryGetProperty(bucket, "remaining", out var remainingElement))
        {
            remaining = remainingElement.ValueKind == JsonValueKind.Object
                ? CodexUsageParser.TryGetDouble(remainingElement, "remainingFraction")
                : ReadFiniteNumber(remainingElement);
        }
        remaining ??= CodexUsageParser.TryGetDouble(bucket, "remainingFraction");
        if (remaining is < 0 or > 1) remaining = null;

        DateTimeOffset? resetAt = null;
        if (CodexUsageParser.TryGetProperty(bucket, "resetTime", out var resetElement))
        {
            if (resetElement.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(resetElement.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                resetAt = parsed;
            }
            else if (resetElement.ValueKind == JsonValueKind.Number && resetElement.TryGetInt64(out var epoch))
            {
                try { resetAt = DateTimeOffset.FromUnixTimeSeconds(epoch); }
                catch (ArgumentOutOfRangeException) { }
            }
        }

        if (remaining is null) return UsageWindow.Unknown(durationMinutes) with { ResetTime = resetAt };
        var remainingPercent = Math.Round(remaining.Value * 100, 1, MidpointRounding.AwayFromZero);
        return new UsageWindow(Math.Round(100 - remainingPercent, 1), remainingPercent, resetAt, durationMinutes);
    }

    private static double? ReadFiniteNumber(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number) && double.IsFinite(number)) return number;
        return null;
    }

    private static WindowKind? GetWindowKind(JsonElement bucket)
    {
        foreach (var propertyName in new[] { "window", "displayName", "bucketId" })
        {
            if (!CodexUsageParser.TryGetProperty(bucket, propertyName, out var value) || value.ValueKind != JsonValueKind.String) continue;
            var text = value.GetString() ?? string.Empty;
            if (text.Contains("week", StringComparison.OrdinalIgnoreCase)) return WindowKind.Weekly;
            if (text.Contains("5h", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("five hour", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("session", StringComparison.OrdinalIgnoreCase)) return WindowKind.FiveHour;
        }
        return null;
    }

    private enum WindowKind { FiveHour, Weekly }
}
