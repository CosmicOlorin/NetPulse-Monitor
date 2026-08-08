# Privacy notice

Last updated: 8 August 2026

NetPulse Monitor is a local-first Windows desktop application. It has no NetPulse
account, advertising SDK, analytics SDK, crash-reporting service or central
telemetry collector.

## Data stored on the computer

- General settings, explicitly saved SMS contact names/numbers, LTE history and
  guarded Cell Lock recovery state are stored under
  `%LOCALAPPDATA%\NetPulseMonitor`.
- Connection events, speed-test results and privacy-filtered router telemetry are
  stored under `%USERPROFILE%\Documents\NetPulse-Monitor`.
- The TP-Link password is not written to those files. When the user chooses to
  remember it, Windows Credential Manager protects it for the Windows account.
- SMS content and the router's Inbox/Sent/Drafts timeline remain in memory and
  are never written to settings, diagnostics, events or CSV logs.
- Full cell identifiers may exist in the local LTE history when required for an
  explicitly requested Cell Lock. CSV telemetry masks cell identifiers.
- Public-IP values used to detect a connection change remain in memory and are
  not written to logs or settings.

## Network requests

Normal internet traffic reveals the computer's public IP address to the remote
service, as with any HTTPS request. NetPulse may contact:

- the user-configured private-LAN TP-Link router for local telemetry, SMS and
  explicitly confirmed Cell Lock actions;
- Cloudflare, OVH, Hetzner and a bounded set from the official LibreSpeed server
  directory for requested or automatic speed tests;
- ipify, Amazon Check IP or icanhazip to detect public-IP changes;
- the configured ping target and DNS resolver for monitoring and diagnostics.

NetPulse does not send the TP-Link password, SMS content, saved contacts, LTE
history or router tokens to those internet services. A direct SMS is sent only
after confirmation and is handed to the local MR600 for delivery by the mobile
network.

## Control and deletion

The user can disable TP-Link monitoring, automatic Cell Lock, automatic speed
tests, Windows startup and tray minimisation in Settings. Closing NetPulse ends
the local router session. To remove locally stored information, exit the app,
delete `%LOCALAPPDATA%\NetPulseMonitor` and
`%USERPROFILE%\Documents\NetPulse-Monitor`, then remove the
`NetPulseMonitor:TpLinkMr600` entry from Windows Credential Manager.

For a security issue, follow the private process in
[`SECURITY.md`](.github/SECURITY.md). Do not submit private data in a public issue.
