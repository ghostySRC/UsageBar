# Changelog

All notable UsageBar changes are recorded here.

## [1.0.1] - 2026-09-26

### Changed

- Package the self-contained .NET runtime as files instead of one bundled executable, reducing measured idle private memory on the development system by about 30%.
- Add a portable `UsageBar-win-x64.zip` alongside the installer.

## [1.0.0] - 2026-09-26

### Added

- Windows 11 taskbar-edge widget for live Codex and Antigravity quotas.
- Five-hour and weekly used/remaining percentages with reset times.
- Automatic app detection, periodic and lifecycle refresh, local cache, and tray controls.
- Per-user startup, settings, multi-monitor positioning, and light/dark theme support.
- Self-contained Windows x64 installer and GitHub Actions build/release workflows.
