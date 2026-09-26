namespace UsageBar.Core.Models;

public enum ProviderStatus
{
    Waiting,
    Connected,
    Refreshing,
    Stale,
    Unavailable,
    NotInstalled,
    NotRunning,
    AuthenticationRequired,
    RateLimited
}

public sealed record UsageWindow(
    double? UsedPercent,
    double? RemainingPercent,
    DateTimeOffset? ResetTime,
    int? DurationMinutes)
{
    public static UsageWindow Unknown(int? durationMinutes = null) => new(null, null, null, durationMinutes);
}

public sealed record UsageGroup(
    string Name,
    UsageWindow? FiveHour,
    UsageWindow? Weekly);

public sealed record ProviderUsage(
    string ProviderId,
    string DisplayName,
    IReadOnlyList<UsageGroup> Groups,
    ProviderStatus Status,
    DateTimeOffset? LastUpdated,
    DateTimeOffset LastAttemptedAt,
    string? StatusDetail = null)
{
    public bool HasKnownUsage => Groups.Any(group =>
        group.FiveHour?.UsedPercent is not null || group.Weekly?.UsedPercent is not null);

    public ProviderUsage WithStatus(ProviderStatus status, string? detail = null) =>
        this with { Status = status, StatusDetail = detail };
}

public sealed record CodexProcess(string ExecutablePath, int ProcessId);

public sealed record AntigravityLanguageServer(
    string ExecutablePath,
    int ProcessId,
    string ClientKind,
    IReadOnlyList<int> ListeningPorts,
    string CsrfToken);

public sealed record RunningApplications(
    IReadOnlyList<CodexProcess> CodexProcesses,
    bool AntigravityRunning,
    IReadOnlyList<AntigravityLanguageServer> AntigravityServers)
{
    public bool CodexRunning => CodexProcesses.Count > 0;
}
