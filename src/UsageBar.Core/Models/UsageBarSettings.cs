namespace UsageBar.Core.Models;

public sealed record UsageBarSettings
{
    public int Version { get; init; } = 1;
    public bool StartWithWindows { get; init; } = true;
    public int RefreshIntervalSeconds { get; init; } = 60;
    public bool ShowCodex { get; init; } = true;
    public bool ShowAntigravity { get; init; } = true;
    public bool ShowRemaining { get; init; }
    public bool ShowPercentages { get; init; } = true;
    public bool ShowProgressBars { get; init; } = true;
    public bool CompactMode { get; init; } = true;
    public bool TaskbarEdgeWidget { get; init; } = true;
    public bool AlwaysOnTop { get; init; } = true;
    public double? WidgetLeft { get; init; }
    public double? WidgetTop { get; init; }
    public string? WidgetMonitor { get; init; }

    public static UsageBarSettings Default => new();

    public UsageBarSettings Normalize() => this with
    {
        Version = 1,
        RefreshIntervalSeconds = RefreshIntervalSeconds is 30 or 60 or 120 or 300 ? RefreshIntervalSeconds : 60,
        WidgetLeft = double.IsFinite(WidgetLeft ?? 0) ? WidgetLeft : null,
        WidgetTop = double.IsFinite(WidgetTop ?? 0) ? WidgetTop : null,
        WidgetMonitor = string.IsNullOrWhiteSpace(WidgetMonitor) ? null : WidgetMonitor[..Math.Min(WidgetMonitor.Length, 128)]
    };
}
