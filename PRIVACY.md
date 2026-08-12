# Privacy notice

Last updated: 10 August 2026

NetPulse Monitor is a local-first Windows desktop application. It has no NetPulse
account, advertising SDK, analytics SDK, crash-reporting service or central
telemetry collector.

## Data stored on the computer

- General settings, explicitly saved SMS contact names/numbers, LTE history and
  guarded Cell Lock recovery state are stored under
  `%LOCALAPPDATA%\NetPulseMonitor`.
- Connection events, speed-test results and full technical router telemetry are
  stored under `%USERPROFILE%\Documents\NetPulse-Monitor`.
- The TP-Link password is not written to those files. When the user chooses to
  remember it, Windows Credential Manager protects it for the Windows account.
- SMS content and the router's Inbox/Sent/Drafts timeline remain in memory and
  are never written to settings, diagnostics, events or CSV logs. NetPulse keeps
  only bounded SHA-256 message-identity fingerprints locally so the same unread
  message does not notify again after restart; the file contains no plaintext
  phone number or message body.
- Full cell identifiers exist in local LTE history and router-telemetry CSV so
  antenna performance, outages and Cell Lock results can be correlated.
- Public-IP values used to detect a connection change remain in memory and are
  not written to logs or settings.
- A user-requested ISP evidence ZIP intentionally contains a full technical
  session summary, up to 30 days of relevant events/speed results, the current
  public and local IP details, gateway, and full LTE antenna identifiers and
  telemetry and LTE time-period history where available. Credentials, tokens,
  settings, SMS data, contacts,
  screenshots and router authentication material are excluded.
- UI, CSV and ISP-evidence timestamps use the country and official time zone
  selected by the user, independently of the Windows display time zone.
- Theme, dashboard-layout and update-check preferences are stored with the other
  local settings. The last successful update-check time is stored to enforce the
  once-per-day limit.

## Network requests

Normal internet traffic reveals the computer's public IP address to the remote
service, as with any HTTPS request. NetPulse may contact:

- the user-configured private-LAN TP-Link router for local telemetry, SMS and
  explicitly confirmed Cell Lock actions;
- Cloudflare, OVH, Hetzner and a bounded set from the official LibreSpeed server
  directory for requested or automatic speed tests;
- ipify, Amazon Check IP or icanhazip to detect public-IP changes;
- GitHub's public release API, when the user requests an update check or enables
  the daily check, to read the latest NetPulse tag and release-page URL;
- the configured ping target and DNS resolver for monitoring and diagnostics.

NetPulse does not send the TP-Link password, SMS content, saved contacts, LTE
history or router tokens to those internet services. A direct SMS is sent only
after confirmation and is handed to the local MR600 for delivery by the mobile
network.

Update checks do not download an executable, send router/SMS/LTE history, or
require a GitHub account. Opening a newer release page remains an explicit user
action.

## Control and deletion

The user can disable TP-Link monitoring, automatic Cell Lock, automatic speed
tests, Windows startup and tray minimisation in Settings. Closing NetPulse ends
the local router session. To remove locally stored information, exit the app,
delete `%LOCALAPPDATA%\NetPulseMonitor` and
`%USERPROFILE%\Documents\NetPulse-Monitor`, then remove the
`NetPulseMonitor:TpLinkMr600` entry from Windows Credential Manager.

For a security issue, follow the private process in
[`SECURITY.md`](.github/SECURITY.md). Do not submit private data in a public issue.
