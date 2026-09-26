using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using UsageBar.Core.Models;

namespace UsageBar.Core.Services;

public sealed class ProcessDiscovery
{
    private const string ProcessQuery = "SELECT Name, ProcessId, ParentProcessId, ExecutablePath FROM Win32_Process " +
        "WHERE Name='codex.exe' OR Name='Antigravity.exe' OR Name='Antigravity IDE.exe' OR Name='language_server.exe'";
    private static readonly Regex CsrfArgument = new(
        "(?:^|\\s)--(?:extension_server_)?csrf_token(?:=|\\s+)(?:\"([^\"]+)\"|'([^']+)'|([^\\s]+))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex AppDataArgument = new(
        "(?:^|\\s)--app_data_dir(?:=|\\s+)(?:\"([^\"]+)\"|'([^']+)'|([^\\s]+))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public RunningApplications GetSnapshot(int? excludedCodexProcessId = null)
    {
        var codex = new List<CodexProcess>();
        var appProcesses = new List<(string Path, string Kind)>();
        var candidates = new List<(int Pid, int ParentPid, string Path)>();

        try
        {
            using var searcher = new ManagementObjectSearcher("root\\CIMV2", ProcessQuery);
            using var processes = searcher.Get();
            foreach (ManagementObject process in processes)
            {
                using (process)
                {
                    var name = process["Name"] as string;
                    var path = process["ExecutablePath"] as string;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                    var pid = Convert.ToInt32(process["ProcessId"]);

                    if (name.Equals("codex.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pid != excludedCodexProcessId) codex.Add(new CodexProcess(path, pid));
                    }
                    else if (name.Equals("Antigravity.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        appProcesses.Add((path, "standalone"));
                    }
                    else if (name.Equals("Antigravity IDE.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        appProcesses.Add((path, "ide"));
                    }
                    else if (name.Equals("language_server.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        var parent = Convert.ToInt32(process["ParentProcessId"]);
                        candidates.Add((pid, parent, path));
                    }
                }
            }
        }
        catch
        {
            // A transient WMI failure is handled as an unavailable scan by the caller.
            throw new ProcessDiscoveryException();
        }

        var runningKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, kind) in appProcesses)
        {
            if (path.EndsWith("Antigravity.exe", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith("Antigravity IDE.exe", StringComparison.OrdinalIgnoreCase))
            {
                runningKinds.Add(kind);
            }
        }

        var servers = new List<AntigravityLanguageServer>();
        foreach (var candidate in candidates)
        {
            var kind = GetClientKind(candidate.Path);
            if (kind is null || !runningKinds.Contains(kind)) continue;

            // Read a command line only after the process path matches an installed Antigravity language server.
            var commandLine = GetCommandLine(candidate.Pid);
            if (commandLine is null) continue;
            var appDataMatch = AppDataArgument.Match(commandLine);
            var appData = CaptureArgument(appDataMatch);
            if (appData is not null)
            {
                var expected = kind == "standalone" ? "antigravity" : "antigravity-ide";
                if (!appData.Equals(expected, StringComparison.OrdinalIgnoreCase)) continue;
            }

            var csrf = CaptureArgument(CsrfArgument.Match(commandLine));
            if (csrf is null) continue;

            servers.Add(new AntigravityLanguageServer(
                candidate.Path,
                candidate.Pid,
                kind,
                TcpListenerTable.GetPortsOwnedBy(candidate.Pid),
                csrf));
        }

        var appRunning = runningKinds.Contains("standalone") || runningKinds.Contains("ide");
        return new RunningApplications(codex, appRunning, servers);
    }

    private static string? GetCommandLine(int processId)
    {
        try
        {
            using var query = new ManagementObjectSearcher(
                "root\\CIMV2",
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId={processId}");
            using var results = query.Get();
            foreach (ManagementObject item in results)
            {
                using (item) return item["CommandLine"] as string;
            }
        }
        catch { }
        return null;
    }

    private static string? GetClientKind(string path)
    {
        if (path.Contains("\\Antigravity\\resources\\bin\\language_server.exe", StringComparison.OrdinalIgnoreCase))
            return "standalone";
        if (path.Contains("\\Antigravity IDE\\", StringComparison.OrdinalIgnoreCase))
            return "ide";
        return null;
    }

    private static string? CaptureArgument(Match match)
    {
        if (!match.Success) return null;
        for (var i = 1; i < match.Groups.Count; i++)
        {
            if (match.Groups[i].Success) return match.Groups[i].Value;
        }
        return null;
    }

    private static class TcpListenerTable
    {
        private const int AfInet = 2;
        private const int TcpTableOwnerPidAll = 5;
        private const int TcpStateListen = 2;
        private const uint ErrorInsufficientBuffer = 122;

        public static IReadOnlyList<int> GetPortsOwnedBy(int processId)
        {
            var size = 0;
            var result = GetExtendedTcpTable(IntPtr.Zero, ref size, true, AfInet, TcpTableOwnerPidAll, 0);
            if (result != ErrorInsufficientBuffer || size <= sizeof(uint)) return [];

            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                result = GetExtendedTcpTable(buffer, ref size, true, AfInet, TcpTableOwnerPidAll, 0);
                if (result != 0) return [];

                var count = Marshal.ReadInt32(buffer);
                var rows = new HashSet<int>();
                const int rowSize = 24;
                for (var index = 0; index < count; index++)
                {
                    var row = Marshal.PtrToStructure<TcpRow>(IntPtr.Add(buffer, sizeof(uint) + index * rowSize));
                    if (row.State != TcpStateListen || row.ProcessId != (uint)processId) continue;
                    var port = (ushort)System.Net.IPAddress.NetworkToHostOrder((short)(row.LocalPort & 0xffff));
                    if (port > 0) rows.Add(port);
                }
                return rows.Order().ToArray();
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TcpRow
        {
            public uint State;
            public uint LocalAddress;
            public uint LocalPort;
            public uint RemoteAddress;
            public uint RemotePort;
            public uint ProcessId;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr tcpTable,
            ref int size,
            [MarshalAs(UnmanagedType.Bool)] bool sort,
            int ipVersion,
            int tableClass,
            uint reserved);
    }
}

public sealed class ProcessDiscoveryException : Exception
{
    public ProcessDiscoveryException() : base("The Windows process list could not be read.") { }
}

public sealed class ProcessLifecycleWatcher : IDisposable
{
    private static readonly string[] WatchedNames = ["codex", "Antigravity", "Antigravity IDE", "language_server"];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Func<int?> _ignoredCodexProcessId;
    private Task? _pollTask;
    private bool _disposed;

    public event EventHandler? ProcessChanged;

    public ProcessLifecycleWatcher(Func<int?>? ignoredCodexProcessId = null) =>
        _ignoredCodexProcessId = ignoredCodexProcessId ?? (() => null);

    public void Start()
    {
        if (_pollTask is not null) return;
        _pollTask = Task.Run(() => PollAsync(_lifetime.Token));
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        HashSet<int>? previous = null;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (!cancellationToken.IsCancellationRequested)
        {
            var current = new HashSet<int>();
            foreach (var name in WatchedNames)
            {
                try
                {
                    foreach (var process in System.Diagnostics.Process.GetProcessesByName(name))
                    {
                        using (process)
                        {
                            if (name.Equals("codex", StringComparison.OrdinalIgnoreCase) && process.Id == _ignoredCodexProcessId()) continue;
                            current.Add(process.Id);
                        }
                    }
                }
                catch { }
            }

            if (previous is not null && !current.SetEquals(previous))
            {
                try { ProcessChanged?.Invoke(this, EventArgs.Empty); }
                catch { }
            }
            previous = current;

            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) break;
            }
            catch (OperationCanceledException) { break; }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        try { _pollTask?.GetAwaiter().GetResult(); }
        catch { }
        _lifetime.Dispose();
    }
}
