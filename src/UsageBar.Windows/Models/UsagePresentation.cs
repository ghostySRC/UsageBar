using UsageBar.Core.Models;

namespace UsageBar.Windows.Models;

public sealed record UsageWidgetItem(
    string ProviderLabel,
    string GroupLabel,
    string FiveHourLabel,
    string WeeklyLabel,
    double FiveHourRemaining,
    double WeeklyRemaining,
    bool HasFiveHourData,
    bool HasWeeklyData,
    bool ShowProgressBars,
    string ToolTip);

public sealed record UsagePopupGroup(
    string Name,
    string FiveHourUsed,
    string FiveHourRemaining,
    string FiveHourReset,
    double FiveHourProgress,
    bool HasFiveHourData,
    string WeeklyUsed,
    string WeeklyRemaining,
    string WeeklyReset,
    double WeeklyProgress,
    bool HasWeeklyData,
    bool ShowProgressBars,
    string ToolTip);

public sealed record UsagePopupProvider(
    string Name,
    string Status,
    string StatusColor,
    string LastUpdated,
    IReadOnlyList<UsagePopupGroup> Groups);

public sealed class UsagePopupViewModel
{
    public IReadOnlyList<UsagePopupProvider> Providers { get; init; } = [];
    public bool HasProviders => Providers.Count > 0;
    public string EmptyMessage { get; init; } = "Open Codex or Antigravity to see current usage.";
    public bool ShowLaunchCodex { get; init; }
    public bool ShowLaunchAntigravity { get; init; }

    public static UsagePopupViewModel Create(
        IReadOnlyList<ProviderUsage> usages,
        UsageBarSettings settings,
        bool codexRunning,
        bool antigravityRunning)
    {
        var providers = usages.Select(usage => new UsagePopupProvider(
            usage.DisplayName,
            StatusText(usage),
            StatusColor(usage.Status),
            usage.LastUpdated is null ? "No successful refresh yet" : $"Updated {usage.LastUpdated.Value.ToLocalTime():HH:mm:ss}",
            BuildGroups(usage, settings))).ToArray();

        var empty = providers.Length > 0
            ? string.Empty
            : codexRunning || antigravityRunning
                ? "Usage data is not available yet. Check the connection status in Settings."
                : "Open Codex or Antigravity to see current usage.";

        return new UsagePopupViewModel
        {
            Providers = providers,
            EmptyMessage = empty,
            ShowLaunchCodex = !codexRunning,
            ShowLaunchAntigravity = !antigravityRunning
        };
    }

    public static IReadOnlyList<UsageWidgetItem> CreateWidgetItems(
        IReadOnlyList<ProviderUsage> usages,
        UsageBarSettings settings)
    {
        var items = new List<UsageWidgetItem>();
        foreach (var usage in usages)
        {
            IReadOnlyList<UsageGroup> groups = usage.Groups.Count == 0
                ? [new UsageGroup("Usage", null, null)]
                : usage.Groups;
            var first = true;
            foreach (var group in groups)
            {
                items.Add(new UsageWidgetItem(
                    ProviderLabel(usage.ProviderId, first),
                    DisplayGroupName(group.Name, usage.ProviderId, settings.CompactMode),
                    MetricLabel("5h", group.FiveHour, settings),
                    MetricLabel("Week", group.Weekly, settings),
                    group.FiveHour?.RemainingPercent ?? 0,
                    group.Weekly?.RemainingPercent ?? 0,
                    group.FiveHour?.RemainingPercent is not null,
                    group.Weekly?.RemainingPercent is not null,
                    settings.ShowProgressBars,
                    GroupToolTip(usage, group)));
                first = false;
            }
        }
        return items;
    }

    private static IReadOnlyList<UsagePopupGroup> BuildGroups(ProviderUsage usage, UsageBarSettings settings)
    {
        IReadOnlyList<UsageGroup> groups = usage.Groups.Count == 0 ? [new UsageGroup("Usage", null, null)] : usage.Groups;
        return groups.Select(group => new UsagePopupGroup(
            DisplayGroupName(group.Name, usage.ProviderId, compact: false),
            Percent(group.FiveHour?.UsedPercent),
            Percent(group.FiveHour?.RemainingPercent),
            ResetTime(group.FiveHour?.ResetTime),
            group.FiveHour?.RemainingPercent ?? 0,
            group.FiveHour?.UsedPercent is not null,
            Percent(group.Weekly?.UsedPercent),
            Percent(group.Weekly?.RemainingPercent),
            ResetTime(group.Weekly?.ResetTime),
            group.Weekly?.RemainingPercent ?? 0,
            group.Weekly?.UsedPercent is not null,
            settings.ShowProgressBars,
            GroupToolTip(usage, group))).ToArray();
    }

    private static string ProviderLabel(string providerId, bool first)
    {
        if (!first) return string.Empty;
        return providerId == "codex" ? "Codex" : "Antigravity";
    }

    private static string DisplayGroupName(string name, string providerId, bool compact) =>
        providerId != "antigravity" ? string.Empty : (name, compact) switch
        {
            ("Gemini Models", true) => "Gemini",
            ("Claude and GPT models", true) => "Claude + GPT",
            _ => name
        };

    private static string MetricLabel(string name, UsageWindow? window, UsageBarSettings settings)
    {
        if (window?.UsedPercent is null || window.RemainingPercent is null)
            return settings.ShowPercentages ? $"{name} —" : name;
        if (!settings.ShowPercentages) return name;
        var value = settings.ShowRemaining ? window.RemainingPercent : window.UsedPercent;
        return $"{name} {value:0.#}%";
    }

    private static string Percent(double? value) => value is null ? "—" : $"{value:0.#}%";

    private static string ResetTime(DateTimeOffset? value) => value is null
        ? "Unavailable"
        : value.Value.ToLocalTime().ToString("ddd HH:mm:ss");

    private static string GroupToolTip(ProviderUsage usage, UsageGroup group)
    {
        static string Line(string label, UsageWindow? window)
        {
            if (window?.UsedPercent is null || window.RemainingPercent is null) return $"{label}: usage unavailable";
            var reset = window.ResetTime is null ? "reset unavailable" : $"resets {window.ResetTime.Value.ToLocalTime():ddd HH:mm:ss}";
            return $"{label}: {window.UsedPercent:0.#}% used, {window.RemainingPercent:0.#}% remaining; {reset}";
        }

        var status = StatusText(usage);
        var refreshed = usage.LastUpdated is null ? "No successful refresh" : $"Last successful refresh {usage.LastUpdated.Value.ToLocalTime():HH:mm:ss}";
        var name = DisplayGroupName(group.Name, usage.ProviderId, compact: false);
        var title = string.IsNullOrEmpty(name) ? usage.DisplayName : $"{usage.DisplayName} · {name}";
        return $"{title}\n{Line("5-hour", group.FiveHour)}\n{Line("Weekly", group.Weekly)}\n{refreshed}\n{status}";
    }

    private static string StatusText(ProviderUsage usage) => usage.Status switch
    {
        ProviderStatus.Connected => "Connected",
        ProviderStatus.Refreshing => "Refreshing",
        ProviderStatus.Stale => "Refresh delayed · showing last successful values",
        ProviderStatus.AuthenticationRequired => "Sign-in required",
        ProviderStatus.RateLimited => "Refresh rate limited",
        ProviderStatus.NotRunning => usage.StatusDetail ?? "Application is not running",
        ProviderStatus.NotInstalled => "Application is not installed",
        _ => usage.StatusDetail ?? "Usage is unavailable"
    };

    private static string StatusColor(ProviderStatus status) => status switch
    {
        ProviderStatus.Connected => "#218739",
        ProviderStatus.Refreshing => "#2563EB",
        ProviderStatus.Stale or ProviderStatus.RateLimited => "#B7791F",
        ProviderStatus.AuthenticationRequired or ProviderStatus.Unavailable or ProviderStatus.NotInstalled => "#B42318",
        _ => "#686B70"
    };
}
