# Contributing

Issues and pull requests are welcome. Please check existing issues before
opening a duplicate, and include the UsageBar version, Windows version, and a
short reproduction when reporting a bug. Redact account usage, user names,
tokens, and other private information from diagnostics.

## Build and test

Requirements: Windows 11 x64 and the .NET 10 SDK.

```powershell
dotnet restore UsageBar.sln
dotnet build UsageBar.sln --configuration Release --no-restore
dotnet test UsageBar.sln --configuration Release --no-build
```

The live provider probe reads your current signed-in accounts and prints their
usage to the local console. Keep its output private:

```powershell
dotnet run --project tools/UsageBar.ProviderProbe
```

Build the self-contained app and installer with Inno Setup 6 installed:

```powershell
./tools/Build-Release.ps1 -Version 1.0.0
```

Please keep changes focused, preserve secret-safe logging, and add parser or
state tests for changed behavior. Do not commit local settings, provider
responses, logs, or real account screenshots.
