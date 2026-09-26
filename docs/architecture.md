# Architecture

UsageBar is a Windows-only WPF tray utility. It has no hosted service or
database.

## Data flow

1. `ProcessDiscovery` reads the Windows process list and the listener ports
   owned by Antigravity's matching language-server process. It reads that
   process command line only after its executable path matches an installed
   Antigravity language server.
2. `CodexUsageProvider` starts the installed `codex app-server --stdio`
   executable, performs the documented JSONL initialization handshake, and
   requests `account/rateLimits/read`. It does not open or copy Codex credential
   files.
3. `AntigravityUsageProvider` posts an empty quota-summary request to the
   loopback language-server route discovered from the installed app process.
   The matching process's CSRF argument is held in memory for the request and
   is not persisted or logged.
4. The provider parsers normalize the service responses into usage groups,
   five-hour and weekly windows, remaining percentages, reset timestamps, and
   status.
5. `UsageCoordinator` serializes refreshes, caches the last valid snapshot,
   keeps stale values after failures, and applies exponential backoff. The
   WPF UI reads snapshots on the dispatcher.

## Local files

- `%LOCALAPPDATA%\UsageBar\settings.json` stores UI and startup preferences.
- `%LOCALAPPDATA%\UsageBar\usage-cache.json` stores the last successful
  normalized usage snapshot for stale display while offline.
- `%LOCALAPPDATA%\UsageBar\logs\usagebar.log` stores bounded, sanitized
  lifecycle and failure categories. It excludes values, tokens, request
  headers, process command lines, and response bodies.

## Windows integration

The app uses an HKCU `Run` value for per-user sign-in startup and a named local
mutex for single-instance behavior. The frameless widget is a topmost,
no-activate tool window positioned against the current monitor's working area.
It does not inject into or modify Explorer. It is placed next to the taskbar,
not embedded inside the taskbar's private window tree.

Process lifecycle changes are checked with a two-second, name-filtered process
poll. Usage itself refreshes on launch, lifecycle changes, popup open, network
return, resume from sleep, and the configured interval (60 seconds by default).
