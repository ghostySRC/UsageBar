using UsageBar.Core.Models;
using UsageBar.Core.Providers;
using UsageBar.Core.Services;

var running = new ProcessDiscovery().GetSnapshot();
if (running.CodexRunning)
{
    var process = running.CodexProcesses.FirstOrDefault(item => File.Exists(item.ExecutablePath));
    if (process is not null)
    {
        await using var provider = new CodexUsageProvider();
        await PrintAsync(await provider.RefreshAsync(process, CancellationToken.None));
    }
    else
    {
        Console.WriteLine("Codex: executable unavailable");
    }
}
else
{
    Console.WriteLine("Codex: not running");
}

if (running.AntigravityRunning)
{
    var provider = new AntigravityUsageProvider();
    try { await PrintAsync(await provider.RefreshAsync(running.AntigravityServers, CancellationToken.None)); }
    catch (UsageProviderException exception) { Console.WriteLine($"Antigravity: {exception.Status}"); }
}
else
{
    Console.WriteLine("Antigravity: not running");
}

static Task PrintAsync(ProviderUsage usage)
{
    foreach (var group in usage.Groups)
    {
        Console.WriteLine($"{usage.DisplayName} · {group.Name} · 5h {Format(group.FiveHour)} · Week {Format(group.Weekly)}");
        Console.WriteLine($"  Reset: 5h {Reset(group.FiveHour)} · Week {Reset(group.Weekly)} · Updated {usage.LastUpdated:O}");
    }
    return Task.CompletedTask;
}

static string Format(UsageWindow? window) => window?.UsedPercent is null ? "unavailable" : $"{window.UsedPercent:0.#}% used / {window.RemainingPercent:0.#}% remaining";
static string Reset(UsageWindow? window) => window?.ResetTime is null ? "unavailable" : window.ResetTime.Value.ToString("O");
