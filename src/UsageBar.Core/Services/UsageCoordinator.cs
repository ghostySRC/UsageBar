using UsageBar.Core.Models;
using UsageBar.Core.Providers;

namespace UsageBar.Core.Services;

public sealed class UsageCoordinator : IAsyncDisposable
{
    private readonly ProcessDiscovery _discovery;
    private readonly ProcessLifecycleWatcher _watcher;
    private readonly CodexUsageProvider _codexProvider;
    private readonly AntigravityUsageProvider _antigravityProvider;
    private readonly SettingsStore _settingsStore;
    private readonly UsageCacheStore _cacheStore;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly object _backoffGate = new();
    private readonly Dictionary<string, ProviderUsage> _snapshots;
    private readonly Dictionary<string, int> _failureCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _nextAttempts = new(StringComparer.OrdinalIgnoreCase);
    private Task? _periodicTask;
    private int _lifecycleRefreshQueued;
    private UsageBarSettings _settings;
    private RunningApplications _running = new([], false, []);

    public event EventHandler? Changed;

    public UsageCoordinator(
        ProcessDiscovery? discovery = null,
        ProcessLifecycleWatcher? watcher = null,
        SettingsStore? settingsStore = null,
        UsageCacheStore? cacheStore = null,
        CodexUsageProvider? codexProvider = null,
        AntigravityUsageProvider? antigravityProvider = null)
    {
        _discovery = discovery ?? new ProcessDiscovery();
        _settingsStore = settingsStore ?? new SettingsStore();
        _cacheStore = cacheStore ?? new UsageCacheStore();
        _codexProvider = codexProvider ?? new CodexUsageProvider();
        _antigravityProvider = antigravityProvider ?? new AntigravityUsageProvider();
        _watcher = watcher ?? new ProcessLifecycleWatcher(() => _codexProvider.ServerProcessId);
        _settings = _settingsStore.Load();
        _snapshots = _cacheStore.Load().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var key in _snapshots.Keys.ToArray())
        {
            _snapshots[key] = _snapshots[key] with { Status = ProviderStatus.Stale, StatusDetail = null };
        }
    }

    public UsageBarSettings Settings => _settings;
    public RunningApplications Running => _running;

    public IReadOnlyList<ProviderUsage> Snapshots
    {
        get { lock (_stateGate) return _snapshots.Values.OrderBy(snapshot => snapshot.ProviderId).ToArray(); }
    }

    public IReadOnlyList<ProviderUsage> VisibleUsages
    {
        get
        {
            var running = _running;
            var settings = _settings;
            lock (_stateGate)
            {
                return _snapshots.Values
                    .Where(snapshot =>
                        (snapshot.ProviderId == "codex" && running.CodexRunning && settings.ShowCodex) ||
                        (snapshot.ProviderId == "antigravity" && running.AntigravityRunning && settings.ShowAntigravity))
                    .OrderBy(snapshot => snapshot.ProviderId)
                    .ToArray();
            }
        }
    }

    public bool ShouldShowWidget => _settings.TaskbarEdgeWidget && VisibleUsages.Count > 0;

    public void Start()
    {
        _watcher.ProcessChanged += OnProcessChanged;
        _ = Task.Run(() =>
        {
            try { _watcher.Start(); }
            catch (Exception exception) { AppLog.Failure("process-watcher", exception); }
        });
        _periodicTask = PeriodicRefreshLoopAsync(_lifetime.Token);
        _ = RefreshAsync(CancellationToken.None);
    }

    public void SaveSettings(UsageBarSettings settings)
    {
        _settings = settings.Normalize();
        _settingsStore.Save(_settings);
        RaiseChanged();
        NotifyLifecycleChanged();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default, bool bypassBackoff = false)
    {
        if (cancellationToken == default) cancellationToken = _lifetime.Token;
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RunningApplications current;
            try
            {
                current = await Task.Run(() => _discovery.GetSnapshot(_codexProvider.ServerProcessId), cancellationToken).ConfigureAwait(false);
            }
            catch (ProcessDiscoveryException exception)
            {
                AppLog.Failure("process-scan", exception);
                return;
            }

            var previouslyRunning = _running;
            _running = current;
            if (previouslyRunning.CodexRunning && !current.CodexRunning)
            {
                await _codexProvider.StopAsync().ConfigureAwait(false);
            }

            await UpdatePresenceAsync("codex", current.CodexRunning, _settings.ShowCodex).ConfigureAwait(false);
            await UpdatePresenceAsync("antigravity", current.AntigravityRunning, _settings.ShowAntigravity).ConfigureAwait(false);
            RaiseChanged();

            if (current.CodexRunning && _settings.ShowCodex)
            {
                await RefreshCodexAsync(current.CodexProcesses, cancellationToken, bypassBackoff).ConfigureAwait(false);
            }
            if (current.AntigravityRunning && _settings.ShowAntigravity)
            {
                await RefreshAntigravityAsync(current.AntigravityServers, cancellationToken, bypassBackoff).ConfigureAwait(false);
            }

            lock (_stateGate) _cacheStore.Save(_snapshots);
            RaiseChanged();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void NotifyLifecycleChanged()
    {
        if (Interlocked.Exchange(ref _lifecycleRefreshQueued, 1) != 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), _lifetime.Token).ConfigureAwait(false);
                await RefreshAsync(_lifetime.Token, bypassBackoff: true).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { AppLog.Failure("lifecycle-refresh", exception); }
            finally { Interlocked.Exchange(ref _lifecycleRefreshQueued, 0); }
        });
    }

    private void OnProcessChanged(object? sender, EventArgs args) => NotifyLifecycleChanged();

    private async Task UpdatePresenceAsync(string providerId, bool running, bool enabled)
    {
        lock (_stateGate)
        {
            if (!_snapshots.TryGetValue(providerId, out var snapshot))
            {
                var displayName = providerId == "codex" ? "Codex" : "Antigravity";
                var initialStatus = running && enabled ? ProviderStatus.Refreshing : ProviderStatus.NotRunning;
                var detail = initialStatus == ProviderStatus.Refreshing ? "Refreshing usage." : "Application is not running.";
                _snapshots[providerId] = new ProviderUsage(providerId, displayName, [], initialStatus, null, DateTimeOffset.UtcNow, detail);
                return;
            }
            if (!running)
            {
                _snapshots[providerId] = snapshot.WithStatus(ProviderStatus.NotRunning, "Application is not running.");
            }
            else if (!enabled)
            {
                _snapshots[providerId] = snapshot.WithStatus(ProviderStatus.NotRunning, "Usage monitoring is turned off in Settings.");
            }
            else
            {
                _snapshots[providerId] = snapshot.WithStatus(ProviderStatus.Refreshing, "Refreshing usage.");
            }
        }
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task RefreshCodexAsync(IReadOnlyList<CodexProcess> processes, CancellationToken cancellationToken, bool bypassBackoff)
    {
        if (!bypassBackoff && IsBackedOff("codex")) return;
        var process = processes.FirstOrDefault(candidate => File.Exists(candidate.ExecutablePath));
        if (process is null)
        {
            await RecordFailureAsync("codex", ProviderStatus.NotInstalled, "Codex executable is unavailable.").ConfigureAwait(false);
            return;
        }

        try
        {
            var snapshot = await _codexProvider.RefreshAsync(process, cancellationToken).ConfigureAwait(false);
            RecordSuccess(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (UsageProviderException exception)
        {
            await RecordFailureAsync("codex", exception.Status, exception.Message).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLog.Failure("codex-provider", exception);
            await RecordFailureAsync("codex", ProviderStatus.Unavailable, "Codex usage could not be refreshed.").ConfigureAwait(false);
        }
    }

    private async Task RefreshAntigravityAsync(IReadOnlyList<AntigravityLanguageServer> servers, CancellationToken cancellationToken, bool bypassBackoff)
    {
        if (!bypassBackoff && IsBackedOff("antigravity")) return;
        try
        {
            var snapshot = await _antigravityProvider.RefreshAsync(servers, cancellationToken).ConfigureAwait(false);
            RecordSuccess(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (UsageProviderException exception)
        {
            await RecordFailureAsync("antigravity", exception.Status, exception.Message).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLog.Failure("antigravity-provider", exception);
            await RecordFailureAsync("antigravity", ProviderStatus.Unavailable, "Antigravity usage could not be refreshed.").ConfigureAwait(false);
        }
    }

    private bool IsBackedOff(string providerId)
    {
        lock (_backoffGate)
        {
            return _nextAttempts.TryGetValue(providerId, out var next) && next > DateTimeOffset.UtcNow;
        }
    }

    private void RecordSuccess(ProviderUsage snapshot)
    {
        lock (_stateGate) _snapshots[snapshot.ProviderId] = snapshot;
        lock (_backoffGate)
        {
            _failureCounts.Remove(snapshot.ProviderId);
            _nextAttempts.Remove(snapshot.ProviderId);
        }
    }

    private Task RecordFailureAsync(string providerId, ProviderStatus failureStatus, string detail)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_backoffGate)
        {
            var count = _failureCounts.TryGetValue(providerId, out var previous) ? Math.Min(previous + 1, 8) : 1;
            _failureCounts[providerId] = count;
            var seconds = Math.Min(300, 15 * (1 << Math.Min(count - 1, 4)));
            _nextAttempts[providerId] = now.AddSeconds(seconds);
        }

        lock (_stateGate)
        {
            if (_snapshots.TryGetValue(providerId, out var lastKnown))
            {
                _snapshots[providerId] = lastKnown with
                {
                    Status = failureStatus,
                    LastAttemptedAt = now,
                    StatusDetail = detail
                };
            }
            else
            {
                var title = providerId == "codex" ? "Codex" : "Antigravity";
                _snapshots[providerId] = new ProviderUsage(providerId, title, [], failureStatus, null, now, detail);
            }
        }

        AppLog.Warning(providerId, "refresh-failed-" + failureStatus);
        return Task.CompletedTask;
    }

    private async Task PeriodicRefreshLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var delay = GetNextDelay();
            try { await Task.Delay(delay, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            try { await RefreshAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception exception) { AppLog.Failure("refresh-loop", exception); }
        }
    }

    private TimeSpan GetNextDelay()
    {
        var now = DateTimeOffset.UtcNow;
        var interval = TimeSpan.FromSeconds(_settings.RefreshIntervalSeconds);
        lock (_backoffGate)
        {
            foreach (var provider in new[] { "codex", "antigravity" })
            {
                if (_nextAttempts.TryGetValue(provider, out var next) && next > now && next - now < interval)
                {
                    interval = next - now;
                }
            }
        }
        return interval < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : interval;
    }

    private void RaiseChanged()
    {
        try { Changed?.Invoke(this, EventArgs.Empty); }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _watcher.ProcessChanged -= OnProcessChanged;
        _watcher.Dispose();
        if (_periodicTask is not null)
        {
            try { await _periodicTask.ConfigureAwait(false); }
            catch { }
        }
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        try { await _codexProvider.DisposeAsync().ConfigureAwait(false); }
        finally { _refreshGate.Release(); _refreshGate.Dispose(); _lifetime.Dispose(); }
    }
}
