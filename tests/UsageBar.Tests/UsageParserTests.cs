using System.Text.Json;
using UsageBar.Core.Models;
using UsageBar.Core.Providers;
using UsageBar.Windows.Models;
using Xunit;

namespace UsageBar.Tests;

public sealed class UsageParserTests
{
    private static readonly DateTimeOffset FetchedAt = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CodexParserMapsWindowsByServerReportedDuration()
    {
        using var document = JsonDocument.Parse("""
            {
              "rateLimits": {
                "primary": { "usedPercent": 17, "windowDurationMins": 10080, "resetsAt": 1791000000 },
                "secondary": { "usedPercent": 42.5, "windowDurationMins": 300, "resetsAt": 1790500000 }
              }
            }
            """);

        var result = CodexUsageParser.Parse(document.RootElement, FetchedAt);
        var group = Assert.Single(result.Groups);

        Assert.Equal(42.5, group.FiveHour!.UsedPercent);
        Assert.Equal(57.5, group.FiveHour.RemainingPercent);
        Assert.Equal(300, group.FiveHour.DurationMinutes);
        Assert.Equal(17, group.Weekly!.UsedPercent);
        Assert.Equal(83, group.Weekly.RemainingPercent);
        Assert.Equal(10080, group.Weekly.DurationMinutes);
        Assert.Equal(ProviderStatus.Connected, result.Status);
        Assert.Equal(FetchedAt, result.LastUpdated);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790500000), group.FiveHour.ResetTime);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1791000000), group.Weekly.ResetTime);
    }

    [Fact]
    public void CodexParserDoesNotInventUnknownWindows()
    {
        using var document = JsonDocument.Parse("""
            { "rateLimits": { "primary": { "usedPercent": 24, "windowDurationMins": 90, "resetsAt": 1790500000 } } }
            """);

        Assert.Throws<UsageProviderException>(() => CodexUsageParser.Parse(document.RootElement, FetchedAt));
    }

    [Fact]
    public void AntigravityParserPreservesSeparateModelFamiliesAndResetTimes()
    {
        using var document = JsonDocument.Parse("""
            {
              "response": {
                "groups": [
                  {
                    "displayName": "Gemini Models",
                    "buckets": [
                      { "window": "weekly", "remaining": { "remainingFraction": 0.8 }, "resetTime": "2026-10-01T01:00:00Z" },
                      { "window": "5h", "remaining": { "remainingFraction": 0.65 }, "resetTime": "2026-09-26T15:00:00Z" }
                    ]
                  },
                  {
                    "displayName": "Claude and GPT models",
                    "buckets": [
                      { "window": "weekly", "remaining": { "remainingFraction": 0.5 }, "resetTime": "2026-10-02T01:00:00Z" },
                      { "window": "5h", "remaining": { "remainingFraction": 1.0 }, "resetTime": "2026-09-26T18:00:00Z" }
                    ]
                  }
                ]
              }
            }
            """);

        var result = AntigravityUsageParser.Parse(document.RootElement, FetchedAt);
        Assert.Equal(2, result.Groups.Count);
        Assert.Equal("Gemini Models", result.Groups[0].Name);
        Assert.Equal(35, result.Groups[0].FiveHour!.UsedPercent);
        Assert.Equal(20, result.Groups[0].Weekly!.UsedPercent);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 1, 0, 0, TimeSpan.Zero), result.Groups[0].Weekly!.ResetTime);
        Assert.Equal("Claude and GPT models", result.Groups[1].Name);
        Assert.Equal(0, result.Groups[1].FiveHour!.UsedPercent);
        Assert.Equal(50, result.Groups[1].Weekly!.UsedPercent);
    }

    [Fact]
    public void AntigravityParserChoosesMostConstrainedDuplicateBucket()
    {
        using var document = JsonDocument.Parse("""
            {
              "groups": [{
                "displayName": "Gemini Models",
                "buckets": [
                  { "window": "5h", "remaining": { "remainingFraction": 0.9 } },
                  { "window": "5h", "remaining": { "remainingFraction": 0.4 } },
                  { "window": "weekly", "remaining": { "remainingFraction": 0.7 } }
                ]
              }]
            }
            """);

        var result = AntigravityUsageParser.Parse(document.RootElement, FetchedAt);
        Assert.Equal(60, Assert.Single(result.Groups).FiveHour!.UsedPercent);
    }

    [Fact]
    public void AntigravityParserRejectsResponsesWithoutRealPercentages()
    {
        using var document = JsonDocument.Parse("""
            { "groups": [{ "displayName": "Gemini Models", "buckets": [{ "window": "5h", "resetTime": "2026-09-26T15:00:00Z" }] }] }
            """);

        Assert.Throws<UsageProviderException>(() => AntigravityUsageParser.Parse(document.RootElement, FetchedAt));
    }

    [Fact]
    public void CodexParserRejectsOutOfRangeUsageInsteadOfClampingIt()
    {
        using var document = JsonDocument.Parse("""
            { "rateLimits": { "primary": { "usedPercent": 101, "windowDurationMins": 300 } } }
            """);

        Assert.Throws<UsageProviderException>(() => CodexUsageParser.Parse(document.RootElement, FetchedAt));
    }

    [Fact]
    public void AntigravityParserRejectsOutOfRangeFractionsInsteadOfInventingUsage()
    {
        using var document = JsonDocument.Parse("""
            { "groups": [{ "displayName": "Gemini Models", "buckets": [{ "window": "5h", "remainingFraction": 1.2 }] }] }
            """);

        Assert.Throws<UsageProviderException>(() => AntigravityUsageParser.Parse(document.RootElement, FetchedAt));
    }

    [Fact]
    public void WidgetUsesFullProviderNamesAndShortensOnlyCompactModelGroups()
    {
        var codex = new ProviderUsage("codex", "Codex",
            [new UsageGroup("Codex", new UsageWindow(25, 75, null, 300), new UsageWindow(40, 60, null, 10080))],
            ProviderStatus.Connected, FetchedAt, FetchedAt);
        var antigravity = new ProviderUsage("antigravity", "Antigravity",
            [
                new UsageGroup("Gemini Models", new UsageWindow(30, 70, null, 300), new UsageWindow(20, 80, null, 10080)),
                new UsageGroup("Claude and GPT models", new UsageWindow(10, 90, null, 300), new UsageWindow(15, 85, null, 10080))
            ], ProviderStatus.Connected, FetchedAt, FetchedAt);

        var compact = UsagePopupViewModel.CreateWidgetItems([codex, antigravity], UsageBarSettings.Default with { CompactMode = true });
        Assert.Equal("Codex", compact[0].ProviderLabel);
        Assert.Equal("Antigravity", compact[1].ProviderLabel);
        Assert.Equal("Gemini", compact[1].GroupLabel);
        Assert.Equal("Claude + GPT", compact[2].GroupLabel);
        Assert.Equal("5h 25%", compact[0].FiveHourLabel);

        var expanded = UsagePopupViewModel.CreateWidgetItems([antigravity], UsageBarSettings.Default with { CompactMode = false });
        Assert.Equal("Gemini Models", expanded[0].GroupLabel);
        Assert.Equal("Claude and GPT models", expanded[1].GroupLabel);
    }
}
