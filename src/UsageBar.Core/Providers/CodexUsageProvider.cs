using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using UsageBar.Core.Models;

namespace UsageBar.Core.Providers;

public sealed class CodexUsageProvider : IUsageProvider, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CodexAppServerSession? _session;
    private string? _executablePath;

    public string ProviderId => "codex";
    public string DisplayName => "Codex";
    public int? ServerProcessId => _session?.ProcessId;

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await DisposeSessionAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task<ProviderUsage> RefreshAsync(CodexProcess process, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session is null || !_session.IsRunning ||
                !string.Equals(_executablePath, process.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                await DisposeSessionAsync().ConfigureAwait(false);
                if (!File.Exists(process.ExecutablePath))
                {
                    throw new UsageProviderException("Codex executable is unavailable.", ProviderStatus.NotInstalled);
                }

                _executablePath = process.ExecutablePath;
                _session = new CodexAppServerSession(process.ExecutablePath);
                await _session.StartAsync(cancellationToken).ConfigureAwait(false);
            }

            var result = await _session.ReadRateLimitsAsync(cancellationToken).ConfigureAwait(false);
            return CodexUsageParser.Parse(result, DateTimeOffset.UtcNow);
        }
        catch (UsageProviderException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await DisposeSessionAsync().ConfigureAwait(false);
            throw new UsageProviderException("Codex usage could not be refreshed.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await DisposeSessionAsync().ConfigureAwait(false); }
        finally { _gate.Release(); _gate.Dispose(); }
    }

    private async Task DisposeSessionAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync().ConfigureAwait(false);
            _session = null;
            _executablePath = null;
        }
    }

    private sealed class CodexAppServerSession : IAsyncDisposable
    {
        private readonly string _executablePath;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private readonly CancellationTokenSource _lifetime = new();
        private Process? _process;
        private Task? _readTask;
        private int _requestId;

        public CodexAppServerSession(string executablePath) => _executablePath = executablePath;

        public bool IsRunning => _process is { HasExited: false };
        public int? ProcessId => _process is { HasExited: false } process ? process.Id : null;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new System.Text.UTF8Encoding(false),
                StandardErrorEncoding = new System.Text.UTF8Encoding(false)
            };
            startInfo.ArgumentList.Add("app-server");
            startInfo.ArgumentList.Add("--stdio");

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            try
            {
                if (!process.Start()) throw new UsageProviderException("Codex app-server did not start.");
            }
            catch (UsageProviderException) { throw; }
            catch
            {
                process.Dispose();
                throw new UsageProviderException("Codex app-server is unavailable.", ProviderStatus.NotInstalled);
            }

            _process = process;
            _ = process.StandardError.ReadToEndAsync(); // Drain diagnostics; never persist or print them.
            _readTask = ReadResponsesAsync(process, _lifetime.Token);

            await SendRequestAsync("initialize", new
            {
                clientInfo = new { name = "usagebar", title = "UsageBar", version = "1.0.0" },
                capabilities = new { experimentalApi = true }
            }, TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            await SendNotificationAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);
        }

        public Task<JsonElement> ReadRateLimitsAsync(CancellationToken cancellationToken) =>
            SendRequestAsync("account/rateLimits/read", new { }, TimeSpan.FromSeconds(20), cancellationToken);

        private async Task<JsonElement> SendRequestAsync(string method, object parameters, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var process = _process;
            if (process is null || process.HasExited)
            {
                throw new UsageProviderException("Codex app-server stopped.");
            }

            var id = Interlocked.Increment(ref _requestId);
            var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pending.TryAdd(id, completion)) throw new InvalidOperationException("Duplicate app-server request id.");

            try
            {
                await WriteMessageAsync(new { id, method, @params = parameters }, cancellationToken).ConfigureAwait(false);
                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(timeout);
                try
                {
                    return await completion.Task.WaitAsync(timeoutSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new UsageProviderException("Codex app-server timed out.");
                }
            }
            finally
            {
                _pending.TryRemove(id, out _);
            }
        }

        private Task SendNotificationAsync(string method, object parameters, CancellationToken cancellationToken) =>
            WriteMessageAsync(new { method, @params = parameters }, cancellationToken);

        private async Task WriteMessageAsync(object message, CancellationToken cancellationToken)
        {
            var process = _process;
            if (process is null || process.HasExited) throw new UsageProviderException("Codex app-server stopped.");
            var line = JsonSerializer.Serialize(message);
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                throw new UsageProviderException("Codex app-server stopped.");
            }
            finally
            {
                _writeGate.Release();
            }
        }

        private async Task ReadResponsesAsync(Process process, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null) break;
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out var id) ||
                        !_pending.TryGetValue(id, out var completion)) continue;

                    if (root.TryGetProperty("error", out var error))
                    {
                        completion.TrySetException(MapServerError(error));
                    }
                    else if (root.TryGetProperty("result", out var result))
                    {
                        completion.TrySetResult(result.Clone());
                    }
                    else
                    {
                        completion.TrySetException(new UsageProviderException("Codex returned an invalid response."));
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch
            {
                // The caller retains its last valid snapshot and exposes a stale status.
            }
            finally
            {
                foreach (var pending in _pending.Values)
                {
                    pending.TrySetException(new UsageProviderException("Codex app-server stopped."));
                }
            }
        }

        private static UsageProviderException MapServerError(JsonElement error)
        {
            var code = error.TryGetProperty("code", out var codeElement) ? codeElement.ToString() : string.Empty;
            var status = code.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
                         code.Contains("login", StringComparison.OrdinalIgnoreCase)
                ? ProviderStatus.AuthenticationRequired
                : code.Contains("rate", StringComparison.OrdinalIgnoreCase) || code.Contains("429", StringComparison.OrdinalIgnoreCase)
                    ? ProviderStatus.RateLimited
                    : ProviderStatus.Unavailable;
            return new UsageProviderException(status switch
            {
                ProviderStatus.AuthenticationRequired => "Sign in to Codex to show usage.",
                ProviderStatus.RateLimited => "Codex temporarily limited usage refreshes.",
                _ => "Codex usage service returned an error."
            }, status);
        }

        public async ValueTask DisposeAsync()
        {
            var process = _process;
            _process = null;
            if (process is null) return;

            try { process.StandardInput.Close(); }
            catch { }
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { }
            }
            if (_readTask is not null)
            {
                try { await _readTask.ConfigureAwait(false); }
                catch { }
            }
            _lifetime.Cancel();
            _lifetime.Dispose();
            process.Dispose();
            _writeGate.Dispose();
        }
    }
}
