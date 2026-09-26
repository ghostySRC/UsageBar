# Security policy

## Reporting a vulnerability

Please do not open a public issue for a suspected security vulnerability. Use
GitHub's **Report a vulnerability** action on this repository to send a private
report to the maintainer. Include the affected version and steps to reproduce;
remove account identifiers, usage values, access tokens, and other personal
data from screenshots and logs.

## Data handling

UsageBar reads Codex usage through the locally installed Codex App Server and
Antigravity usage through the local Antigravity language server. Antigravity's
local quota route is undocumented and may change. UsageBar keeps the temporary
Antigravity request header in memory only. It does not write credentials,
request headers, process command lines, response bodies, or usage values to its
log. Local cache and settings are stored in the current Windows user's
`%LOCALAPPDATA%\UsageBar` directory.

The application does not provide a remote telemetry or usage-tracking service.
The provider applications may make their own authenticated network requests.
