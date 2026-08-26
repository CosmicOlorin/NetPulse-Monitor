# NetPulse Monitor

**Professional Windows network and LTE monitoring with explicit router control,
diagnostics and ISP-ready evidence.**

[Latest release](https://github.com/CosmicOlorin/NetPulse-Monitor/releases/latest)
· [User guide](docs/USER-GUIDE.md)
· [Security policy](.github/SECURITY.md)
· [Privacy notice](PRIVACY.md)

NetPulse Monitor 1.0.25 is a native .NET 8 WinForms application for continuous
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
which primary cell and band combinations have the best measured radio quality.
NetPulse scores complete profiles as 50% SINR, 35% RSRQ and 15% RSRP. The goal
is to give the user an
understandable record, safer manual control and guarded automation while keeping
every router change explicit and recoverable.

NetPulse Monitor is an independent community project. It is not affiliated with,
endorsed by or supported by TP-Link or any mobile/internet provider.

## Features

- Continuous asynchronous ICMP monitoring with a live latency chart
- A 0-100 Connection Health score with a plain-language explanation
- A measured smart LTE recommendation card with guarded Apply and current-profile test actions
- A searchable connection timeline for outages, recoveries, LTE changes, speed tests, SMS and system events
- Current ping, jitter, rolling packet loss, successes and failures
- Confirmed outage detection, recovery events, uptime, downtime and availability
- Switchable access profiles: Mobile/LTE, ADSL/VDSL and FTTB/FTTH
- One adaptive Dashboard with the access selector, expandable router/line details
  and source labels separating PC-to-Internet, PC-to-router and router LTE data
- Optional capability-based TP-Link router integration, with live LTE telemetry,
  Cell Lock and SIM SMS where the router firmware exposes compatible local APIs
- LTE signal, RSRP, RSRQ, SNR, PCell/SCell bands, primary EARFCN, SIM, usage and rates
- PCI/CID display and cell-specific locking when the router firmware exposes them
- Local LTE cell history with connection time, confirmed disconnects and speed tests
- Ranked cell + band suggestions: 50% SINR, 35% RSRQ and 15% RSRP
- Time-of-day learning with average ping and an explained cell-load estimate
- Manual profile entry, Cell Lock and opt-in guarded automatic locking with rollback
- Controlled multi-profile Cell Experiment mode that restores the original router state
- Cancelable Band & Cell Discovery mode with a verified per-model band plan,
  serving-cell capture and exact router-state restoration
- Ungrouped LTE history with sortable columns and preserved PCell identity
- Complete chronological Inbox/Sent conversation threads, a separate Drafts view,
  and a newest-first message timeline on Windows and Android
- Country-aware phone matching joins national, `+` and `00` number formats
- Stable SMS refresh, one alert per unread message, verified read/unread state, deletion and sending
- Local SMS contact names for saved phone numbers
- Password-only TP-Link setup with Windows Credential Manager protection
- Firmware-reported mobile network modes: 4G-only devices never receive 5G
  choices, while compatible 5G firmware can expose its own supported list
- First-run country and official-time-zone selection for regional dates and ISP evidence
- Persistent connection-event, speed-test and full technical router-telemetry CSV logs
- Manual, scheduled and connection-change 20 MB download / 5 MB upload speed tests
- Automatic tests after confirmed-outage recovery and LTE band, cell or public-IP changes
- Provider fallback, bounded per-request timeouts and cancellation
- Live Dashboard gateway/DNS/IPv4/IPv6 diagnostics with a 30-second refresh
- Guided "Why is my connection slow?" troubleshooting across local, Internet and LTE measurements
- Full technical ISP evidence ZIP with IP, antenna, outage, telemetry and LTE-history details
- LTE Simple, LTE Advanced, DSL/Fiber and ISP-troubleshooting dashboard layouts
- System/Light/Dark themes, system tray, optional Windows startup and editable settings
- Manual or daily update checks with in-app download, verification and restart
- Stable `NetPulse Monitor.exe` path and Windows identity across updates, preserving shortcuts, pins and startup configuration
- Live TP-Link connected-device list in the Windows app and Android Companion (device name, IP, MAC and connection type when reported by firmware)
- Reproducible SHA-256 release packaging
- An embedded application and tray icon

Build tooling that is not supplied by Windows or Android is acquired by the
versioned GitHub workflow. Temporary SDK archives used during development are
tracked and removed after release verification; application settings, logs,
contacts, SMS state and LTE history are never included in that cleanup.

Application settings are stored in `%LOCALAPPDATA%\NetPulseMonitor`. The router
password is not stored in that JSON file; when remembering is enabled it is kept
as a generic credential in Windows Credential Manager. CSV logs are stored in
`%USERPROFILE%\Documents\NetPulse-Monitor`.

## Band & Cell Discovery

Choose **Scan bands & cells** in **LTE history** to run a controlled,
three-stage discovery pass. For the detected Archer MR600(EU) V5 profile,
NetPulse scans the complete documented set B1, B3, B5, B7, B8, B20, B28, B38,
B40 and B41. Unknown router revisions are restricted to bands already observed;
NetPulse does not send speculative lock masks.

Stage 1 selects each band alone for at least 30 seconds. When the modem has
registered but has not yet exposed all identifiers, NetPulse waits up to 75
seconds for a stable, repeated EARFCN/PCI/CID identity before moving to the next
band. Stage 2 locks each complete PCell while making every actually discovered
band available, then records the ordered aggregation sets created by the modem.
Stage 3 reapplies and measures every unique PCell + ordered band set. B20 + B3
and B3 + B20 remain different because the first band is the PCell.

After stale pre-change snapshots are discarded, NetPulse records each distinct
serving profile and its EARFCN, PCI, CID, RSRP, RSRQ and SNR. A band-only scan
discovers serving cells selected by the router; it cannot reveal a private
neighbor-cell list that the router API does not provide. Intentional scan
changes are excluded from outage attribution and do not trigger speed tests.
Results are appended to `band-cell-discovery.csv`. Only results with a verified
EARFCN, PCI and CID are inserted into LTE History as lock-ready candidates; the
stage-3 radio samples are recorded as real measurement evidence. No identifiers
or measurements are invented.

Hover over any LTE History action for a concise explanation. Use **Delete
selected profile...** to remove one identity while preserving every other
history entry.

The scan can be cancelled at any time. Before its first change, NetPulse stores
the exact current band-selection and Cell Lock state. That state is restored in
all normal completion, cancellation and error paths; application exit waits for
restoration rather than abandoning an active scan.

## Access profiles

The **Dashboard** provides an **Access** selector for Mobile/LTE, ADSL/VDSL or
FTTB/FTTH. Its expandable router/line section replaces the old separate
Connection details tab. LTE selection exposes real router RF/cell telemetry and
the recommendation; DSL/Fiber selection keeps the general Internet session,
speed, gateway and DNS views while clearly marking line values that require a
compatible router or ONT provider. NetPulse does not expose separate Wi-Fi or
Ethernet access profiles because those describe the PC-to-router link rather
than the ISP access technology.

Measurement sources are explicit. Ping, jitter, loss and speed tests run from
the Windows PC to an Internet target, so local downloads, Wi-Fi/Ethernet,
router queueing and the ISP path can all affect them. Gateway latency runs from
the PC to the router. LTE band, CID, EARFCN, PCI, SINR/SNR, RSRQ, RSRP and router
traffic rates are read from the compatible router local API. A poor PC-to-
Internet result is therefore not presented as proof of a poor LTE radio link.

General health, speed-test, gateway, DNS and Windows link values work without
router credentials. LTE radio values are supplied by the MR600 provider.
Attenuation, DSL SNR margin, sync rates, optical power and ONT status require a
future compatible router/ONT provider; 1.0.6 labels these fields as requiring
router or ONT data instead of inventing values.

All tabs use fixed-fit DPI-aware layouts. Settings use vertically aligned rows in
two columns, Diagnostics gives its button a dedicated row, and TP-Link actions
stay visible without page-level scroll bars. Initial and minimum sizes are derived
from the current Windows working area.

## TP-Link router setup

### LTE router compatibility

Support is firmware- and hardware-version-specific. A TP-Link product name alone
is not enough to claim full compatibility.

| Router | NetPulse support | Verified scope |
| --- | --- | --- |
| **TP-Link Archer MR600(EU) V5** | **Fully validated LTE integration** | Password-only local login, one-second telemetry, LTE bands and signal values, LTE History, band selection, Cell Lock, Band & Cell Discovery, mobile network mode and SIM SMS. Cell Lock requires firmware that exposes the feature; the validated baseline is `1.5.0 0.9.1 v0001.0 Build 251231 Rel.54154n`. |
| **TP-Link Archer NX200** | **Mobile-network-mode compatibility** | The modern `DEV2_LTE_WAN_CFG` capability is covered by protocol tests. NetPulse reads only the modes reported by the firmware. Live telemetry, SMS, Cell Lock and discovery are not yet declared fully validated on physical NX200 hardware. |
| **TP-Link Archer NX210** | **Mobile-network-mode compatibility family** | Uses the same firmware-reported 5G/4G mode approach documented for the NX200/NX210 family. Full physical-router telemetry, SMS, Cell Lock and discovery validation is still required. |
| Other TP-Link LTE/5G routers | **Detection/fallback only** | Basic connection is attempted only through known local API shapes. Network modes are shown only when the router reports them. Discovery is restricted to already observed bands unless an exact verified model profile exists. No full-support claim is made. |

The compatibility level detected for one hardware or firmware revision must not be
assumed for another revision. See [`docs/MR600-INTEGRATION.md`](docs/MR600-INTEGRATION.md)
for the exact API and safety boundaries.

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

Under **Settings > Mobile network mode**, **Refresh** reads the current mode and
the choices exposed by the connected router firmware. **Apply** requires explicit
confirmation because changing radio generation can briefly disconnect mobile
service. NetPulse never adds 5G choices to a 4G-only device; the MR600 list is
limited to **4G preferred**, **4G only** and **3G only**.

The validated MR600 firmware permits only one management login. NetPulse owns that login while live
telemetry is running. Close NetPulse before using the router webpage; normal app
shutdown logs its router session out.

The **LTE history** tab shows one row for each distinct ordered band set and exact
PCell CID/EARFCN/PCI identity. Time-of-day evidence is selected internally using
the connection location's official time zone and is not repeated as a redundant
Period column. A recommendation needs at least ten connected minutes plus complete
SINR, RSRQ and RSRP evidence. Eligible profiles receive a normalized radio score:
50% SINR, 35% RSRQ and 15% RSRP. Provisional rows remain visible while evidence is
collected. Current-period evidence is blended gradually with all-time data, so
sparse data does not cause abrupt decisions. The grid shows
  the successful-ping average collected on each profile and a cell-load estimate
  derived from period download versus that profile's observed best. The load
  value is explicitly an estimate, not a carrier-reported tower metric; neither
  display value adds a hidden ranking bonus.

Connections remain recorded internally but do not appear in LTE History until
they reach five connected minutes with a complete CID identity. Automatic refreshes keep
the currently visible row and scroll position instead of returning the grid to
the top. The profile being used by the router is highlighted in green.

Download, upload, ping, load estimates and confirmed disconnects remain visible
diagnostic evidence but do not add a hidden bonus or penalty to the RF score.

NetPulse queues a fresh 20 MB download / 5 MB upload measurement after a
confirmed outage recovers and after a stable LTE band, cell or public-IP change.
Changes arriving together are measured once and listed together on the speed-test
event. A 12-second stability window prevents a result from being assigned to an
LTE state that disappeared immediately. Periodic tests remain independently
configurable and can be disabled without disabling these change-driven tests.

Manual Cell Lock always asks for confirmation. Automatic locking is off by
default, requires separate opt-in and only uses medium/high-confidence history.
The Cell Lock tab starts with a list of previously observed five-minute band and
EARFCN sets, ordered with the active set first, so known values can be reused
without retyping them.
It re-evaluates as the time period and results change, but uses a 30-minute
minimum dwell, material-improvement hysteresis and at most six changes per day.
Before every change NetPulse saves the existing band and cell state, validates
internet and LTE for 90 seconds, and restores the old state if validation fails.
An interrupted validation is recovered on the next launch. **Restore automatic
selection** disables both adaptive optimization and Cell Lock, then returns band
selection to Auto.

On the validated MR600 v5 firmware, Auto mode exposes the live PCell/SCell bands
and primary EARFCN. PCI/CID can be present in a single-carrier state and omitted
again while carrier aggregation is active. NetPulse still learns and ranks each
primary-cell/band-combination profile. When PCI is available it can apply a cell
+ band lock; otherwise it applies only the measured band mask and explicitly
leaves cell selection automatic. It never invents missing identifiers.

Serving EARFCN comes from live LTE status. On validated MR600 V5 firmware,
PCI/CID may be exposed only by the live `LTE_CELL_LOCK` status object after the
modem registers. NetPulse accepts that live identity only when its reported
EARFCN exactly matches the current live serving EARFCN, preventing a stale lock
target from being assigned to a new PCell. When carrier aggregation is added
and the PCell remains unchanged, missing identifiers can also be carried only
from the immediately previous live state. Legacy band/EARFCN mismatches are
repaired once and unambiguous duplicate rows are merged while preserving their
measurement totals.

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
artifacts\publish\win-x64\NetPulse Monitor.exe
```

The builder also creates `artifacts\packages\NetPulse-Monitor-win-x64.zip` and
its SHA-256 file. An optional installer recipe is available for Inno Setup 6:

```powershell
.\build-release.ps1 -BuildInstaller
```

Trusted Authenticode signing requires a validated code-signing certificate in
the current Windows user's certificate store. No key or password belongs in the
repository:

```powershell
.\build-release.ps1 `
  -SigningCertificateThumbprint YOUR_40_CHARACTER_THUMBPRINT `
  -BuildInstaller
```

See [`docs/CODE-SIGNING.md`](docs/CODE-SIGNING.md). The Windows publisher is the
certificate's verified legal person or organisation identity; `CosmicOlorin`
remains the product author and copyright brand unless that exact name is also a
validated certificate subject.

## Keep local disk usage low

Self-contained Windows publishing temporarily creates several hundred megabytes
under `bin`, `obj` and `artifacts`. These files are generated and are not part of
the source repository. After testing or packaging a release, run:

```powershell
.\clean-workspace.ps1
```

Or double-click `CLEAN-WORKSPACE.bat`. The cleanup removes only known generated
build/cache folders inside the repository. It does not touch source files, Git
history, releases, application settings or user CSV logs.

For the smallest everyday installation, keep only the latest standalone EXE and
download ZIP/source archives from GitHub when needed. CSV logs grow gradually;
after closing NetPulse, old logs may be archived or removed by the user.

## Speed-test behavior

Download testing tries multiple providers in order and stops the stream after
the configured sample size, so the default transfer is 20 MB. Upload capability
is selected independently and the default payload is 5 MB. If the primary upload
endpoint fails, NetPulse obtains a bounded, cached list of independent HTTPS
backends from the official LibreSpeed server directory and tries up to four
different hosts. Connection, discovery and transfer stages have bounded timeouts,
the complete run has a 180-second limit, and the UI can cancel the operation.

These measurements are operational estimates rather than certified line-rate
benchmarks. Results vary with provider location, routing and server load.

## Privacy and router-change safety

- Router passwords, cookies, tokens and encrypted request bodies are never logged.
- Telemetry polling never changes router configuration.
- Cell/band changes require manual confirmation or explicit automatic-lock opt-in.
- The previous router state is retained locally only while rollback is pending.
- IMEI, MAC and DNS identifiers are not requested from the MR600.
- Router telemetry CSV and local LTE history retain full cell identifiers because
  they are required to correlate antenna performance, outages and focused locks.
- An ISP evidence ZIP intentionally includes the current public/local IP details,
  gateway and full LTE antenna identifiers after a clear confirmation prompt.
- SMS message content and router message history remain in memory only and are
  never written to settings, diagnostics, events or CSV logs.
- Contact names and normalized numbers are stored locally only when the user
  explicitly selects **Save contact...**.
- Official timestamps use the country and time zone selected during first setup;
  they do not depend on, or alter, the Windows display time zone.
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
- `CsvLogger.cs` — durable privacy-filtered CSV output
- `AutoFitLabel.cs` — DPI-aware metric text fitting
- `ExperienceServices.cs` — health, troubleshooting, conversations, timeline and experiment ranking
- `AppThemeManager.cs` — System/Light/Dark WinForms styling
- `UpdateChecker.cs` — user-controlled GitHub release check
- `sign-release.ps1` — SHA-256 Authenticode signing and Windows verification
- `installer/NetPulseMonitor.iss` — optional per-user Windows installer recipe
- `tests/NetPulseMonitor.ProtocolTests` — encrypted mock-router and ranking verification

See [`docs/USER-GUIDE.md`](docs/USER-GUIDE.md) for plain-language explanations
of diagnostics, LTE history columns, time periods, Cell Lock and SMS behavior.

## Security, privacy and ownership

Security reports must use GitHub's private vulnerability-reporting channel; do
not place credentials, SMS content, router exports or unredacted logs in a public
issue. See [`SECURITY.md`](.github/SECURITY.md) for the reporting process and
[`PRIVACY.md`](PRIVACY.md) for the exact local and third-party data flows.

Copyright © 2026 CosmicOlorin. All rights reserved. NetPulse Monitor is an
independent project and is not affiliated with or endorsed by TP-Link or any
internet/mobile provider. See [`COPYRIGHT.md`](COPYRIGHT.md).
