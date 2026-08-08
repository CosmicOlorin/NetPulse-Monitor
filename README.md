# NetPulse Monitor

NetPulse Monitor 1.0.4 is a native .NET 8 WinForms application for continuous
Windows connection monitoring. It runs as a graphical Windows application and
does not open a console window during normal use.

## Why NetPulse exists

NetPulse was conceived from the practical difficulty of maintaining dependable
mobile broadband in rural Greece. Terrain, distance from the serving site,
sector load, carrier aggregation and handovers can make a connection change
substantially throughout the day even when a simple signal indicator appears
acceptable. Intermittent faults are also difficult to reproduce during a
support call, and diagnosis or escalation by an internet provider can take
considerable time.

The standard router interface is useful for configuration, but it primarily
presents a current snapshot. It does not build a long-term, time-aware record of
which primary cell and band combinations are most reliable, and its automatic
selection is not based on the user's measured disconnects and throughput history.
NetPulse turns those observations into local evidence: confirmed outages first,
download performance second and upload performance third. The goal is to give the
user an understandable record, safer manual control and guarded automation while
keeping every router change explicit and recoverable.

NetPulse Monitor is an independent community project. It is not affiliated with,
endorsed by or supported by TP-Link or any mobile/internet provider.

## Features

- Continuous asynchronous ICMP monitoring with a live latency chart
- Current ping, jitter, rolling packet loss, successes and failures
- Confirmed outage detection, recovery events, uptime, downtime and availability
- Switchable access profiles: Mobile/LTE, ADSL/VDSL and FTTB/FTTH
- Independent PC-link selection: auto-detect, Wi-Fi or Ethernet
- Active Windows adapter type and negotiated link speed
- Optional TP-Link Archer MR600 v5 live LTE telemetry
- LTE signal, RSRP, RSRQ, SNR, PCell/SCell bands, primary EARFCN, SIM, usage and rates
- PCI/CID display and cell-specific locking when the router firmware exposes them
- Local LTE cell history with connection time, confirmed disconnects and speed tests
- Ranked cell + band suggestions: 50% download, 40% disconnections and 10% upload
- Time-of-day learning with visible usage share and evidence weighting
- Manual profile entry, Cell Lock and opt-in guarded automatic locking with rollback
- Time-period-grouped LTE history with sortable columns and preserved PCell identity
- MR600 SIM inbox, unread Windows notifications, read/reply and direct SMS sending
- Password-only TP-Link setup with Windows Credential Manager protection
- Persistent connection-event, speed-test and masked router-telemetry CSV logs
- Manual, scheduled and connection-change 20 MB download / 5 MB upload speed tests
- Automatic tests after confirmed-outage recovery and LTE band, cell or public-IP changes
- Provider fallback, explicit timeouts and immediate cancellation
- Gateway, DNS, IPv4 and IPv6 diagnostics
- System tray, optional Windows startup and editable monitoring settings
- An embedded application and tray icon

Application settings are stored in `%LOCALAPPDATA%\NetPulseMonitor`. The router
password is not stored in that JSON file; when remembering is enabled it is kept
as a generic credential in Windows Credential Manager. CSV logs are stored in
`%USERPROFILE%\Documents\NetPulse-Monitor`.

## Access profiles

The **Connection details** tab has two independent selectors:

1. **Access**: Mobile/LTE, ADSL/VDSL or FTTB/FTTH.
2. **PC link**: Auto detect, Wi-Fi or Ethernet.

General health, speed-test, gateway, DNS and Windows link values work without
router credentials. LTE radio values are supplied by the MR600 provider.
Attenuation, DSL SNR margin, sync rates, optical power and ONT status require a
future compatible router/ONT provider; 1.0.4 labels these fields as requiring
router or ONT data instead of inventing values.

All tabs use fixed-fit DPI-aware layouts. Settings are arranged in two columns,
and the TP-Link setup actions stay visible without scroll bars at their supported
window sizes. The initial and minimum application size are derived from the
current Windows working area, preventing the window from being reduced below the
layout size required to keep tab labels and controls visible.

## TP-Link Archer MR600 setup

On first launch, TP-Link setup can be completed or skipped. It requests only:

- enable/disable monitoring;
- the local router address, normally `http://192.168.1.1/`;
- the local administration password;
- whether Windows should protect and remember that password.

No username is requested. Monitoring uses serialized, read-only local requests
with a one-second refresh target. Slow requests never overlap. Busy sessions and
network failures use protective backoff, and a rejected password is not retried
automatically. If another management session is signed in, **Test connection**
offers the same explicit takeover choice as the MR600 login page. Accepting it
signs the other management session out but does not change router settings.

Opening TP-Link setup temporarily releases NetPulse's live router session so the
**Test connection** action can use the MR600's single local-management slot. Live
monitoring reconnects after setup is saved or cancelled.

The MR600 permits only one management login. NetPulse owns that login while live
telemetry is running. Close NetPulse before using the router webpage; normal app
shutdown logs its router session out.

The **LTE history** tab groups observations by local-time period: Night, Morning,
Afternoon and Evening. Band combinations remain separate rows inside each period;
PCI and CID are added when the router supplies them. Column headers sort the rows
inside each period. A recommendation needs at least ten connected minutes and one
speed test. Eligible profiles receive a normalized score made from 50% download,
40% confirmed disconnections per connected hour and 10% upload. Provisional rows
remain visible while evidence is collected. Each period is blended gradually with
all-time data, so sparse data does not cause abrupt decisions. The grid shows the
time-period evidence weight and each cell's observed data-usage share (or
connection-time share when traffic counters are unavailable). Usage affects
confidence only and does not add a hidden ranking bonus.

Connections remain recorded internally but do not appear in LTE History until
they reach five connected minutes in that time period. Automatic refreshes keep
the currently visible row and scroll position instead of returning the grid to
the top.

Download and upload are scored relative to the fastest eligible profile in the
same period. The reliability component is `100 / (1 + drops per hour)`, so zero
drops receives 100 points while repeated drops are penalized progressively rather
than acting as an absolute veto.

NetPulse queues a fresh 20 MB download / 5 MB upload measurement after a
confirmed outage recovers and after a stable LTE band, cell or public-IP change.
Changes arriving together are measured once and listed together on the speed-test
event. A 12-second stability window prevents a result from being assigned to an
LTE state that disappeared immediately. Periodic tests remain independently
configurable and can be disabled without disabling these change-driven tests.

Manual Cell Lock always asks for confirmation. Automatic locking is off by
default, requires separate opt-in and only uses medium/high-confidence history.
It re-evaluates as the time period and results change, but uses a 30-minute
minimum dwell, material-improvement hysteresis and at most six changes per day.
Before every change NetPulse saves the existing band and cell state, validates
internet and LTE for 90 seconds, and restores the old state if validation fails.
An interrupted validation is recovered on the next launch. **Restore automatic
selection** disables both adaptive optimization and Cell Lock, then returns band
selection to Auto.

On the validated MR600 v5 firmware, Auto mode exposes the live PCell/SCell bands
and primary EARFCN but does not expose live PCI or CID. NetPulse still learns and
ranks each primary-cell/band-combination profile. When PCI is available it can
apply a cell + band lock; otherwise it applies only the measured band mask and
explicitly leaves cell selection automatic. It never invents missing identifiers.

Remote TP-Link ID login is intentionally not implemented because TP-Link does
not publish a supported desktop cloud API for this workflow. A user-managed VPN
back to the home LAN can use the same local provider without sharing TP-Link
account credentials with NetPulse.

## Requirements

- Windows 10 or Windows 11, x64
- For building: Visual Studio 2022 with the **.NET desktop development** workload,
  or the .NET 8 SDK

The published Windows release is self-contained and does not require a separate
.NET installation on the target PC.

## Build and run

Open PowerShell in the repository root and run:

```powershell
dotnet restore .\NetPulseMonitor.sln
dotnet build .\NetPulseMonitor.sln -c Release
dotnet run --project .\NetPulseMonitor.csproj -c Release
```

Run the dependency-free MR600 protocol test:

```powershell
dotnet run --project .\tests\NetPulseMonitor.ProtocolTests\NetPulseMonitor.ProtocolTests.csproj -c Release
```

Create the single-file, self-contained Windows x64 release:

```powershell
.\build-release.ps1
```

Or double-click `BUILD-RELEASE.bat`. The executable is written to:

```text
artifacts\publish\win-x64\NetPulseMonitor.exe
```

## Speed-test behavior

Download testing tries multiple providers in order and stops the stream after
the configured sample size, so the default transfer is 20 MB. Upload capability
is selected independently and the default payload is 5 MB. If the primary upload
endpoint fails, NetPulse obtains a bounded, cached list of independent HTTPS
backends from the official LibreSpeed server directory and tries up to four
different hosts. Connection, discovery and transfer stages have bounded timeouts,
the complete run has a 180-second limit, and the UI can cancel the operation.

These measurements are operational estimates rather than certified line-rate
benchmarks. Results vary with provider location, routing, Wi-Fi and server load.

## Privacy and router-change safety

- Router passwords, cookies, tokens and encrypted request bodies are never logged.
- Telemetry polling never changes router configuration.
- Cell/band changes require manual confirmation or explicit automatic-lock opt-in.
- The previous router state is retained locally only while rollback is pending.
- IMEI, MAC, IP, DNS and Wi-Fi identifiers are not requested from the MR600.
- Cell IDs are masked in CSV logs.
- Full cell IDs in LTE history remain only in the local settings folder and are
used solely for the optional focused lock target.
- SMS sender, recipient and message content remain in memory only and are never
  written to settings, diagnostics, events or CSV logs.
- Router destinations are restricted to private LAN addresses.
- Redirects away from the configured router are disabled.

## Repository layout

- `MainForm.cs` — responsive WinForms UI, profiles, settings, tray and scheduling
- `MonitorEngine.cs` — continuous ping and outage state
- `RouterMonitor.cs` — serialized router operations and protective backoff
- `TpLinkMr600Provider.cs` — encrypted MR600 telemetry and guarded Cell Lock provider
- `LteCellHistoryStore.cs` — local LTE history and ranked recommendations
- `SpeedTestEngine.cs` — provider selection, timeouts and measurements
- `NetworkDiagnostics.cs` — gateway, DNS and IP diagnostics
- `LocalNetworkInfo.cs` — active Wi-Fi/Ethernet adapter and link speed
- `CsvLogger.cs` — durable privacy-filtered CSV output
- `AutoFitLabel.cs` — DPI-aware metric text fitting
- `tests/NetPulseMonitor.ProtocolTests` — encrypted mock-router and ranking verification

See [`docs/USER-GUIDE.md`](docs/USER-GUIDE.md) for plain-language explanations
of diagnostics, LTE history columns, time periods, Cell Lock and SMS behavior.
