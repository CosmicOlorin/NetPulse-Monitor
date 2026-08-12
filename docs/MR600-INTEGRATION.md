# TP-Link Archer MR600 integration

## Supported scope

NetPulse Monitor 1.0.7 includes optional local LTE telemetry for the
TP-Link Archer MR600 v5 firmware family. The validated target is hardware v5
running firmware `1.5.0 0.9.1 v0001.0 Build 251231 Rel.54154n`.

### Compatibility matrix

| Router | Status | LTE features covered |
| --- | --- | --- |
| **Archer MR600(EU) V5** | **Fully validated** | Local password login, telemetry, LTE History, band selection, Cell Lock, Band & Cell Discovery, 4G/3G network mode and SIM SMS. Cell Lock depends on firmware support. |
| **Archer NX200** | **Network-mode protocol tested** | Firmware-reported 5G/4G/3G mode list and selection. Full telemetry, Cell Lock, discovery and SMS are not yet claimed as physically validated. |
| **Archer NX210** | **Network-mode family support** | Firmware-reported network modes for the documented NX200/NX210 family. Full telemetry, Cell Lock, discovery and SMS still require physical-router validation. |
| **Other TP-Link LTE/5G models** | **Capability-detected fallback** | Only features that match a known local API and are reported by the router. Band discovery uses observed bands only when no exact verified radio profile exists. |

Hardware region, hardware revision and firmware build are part of compatibility.
Models not listed as fully validated must not be treated as equivalent to the
MR600(EU) V5.

The local administration login is password-only. NetPulse does not display,
request or store a username for this provider.

## Telemetry

The provider requests an allowlisted set of read-only values:

- ISP, network type and registration state;
- PCell/SCell band information and primary EARFCN;
- PCI and cell ID when the firmware/Cell Lock state exposes them;
- SIM readiness and signal percentage;
- RSRP, RSRQ, SNR and RSSI;
- total usage and current upload/download rates;
- hardware and firmware versions.

Telemetry requests are read-only. SMS is isolated behind explicit SMS actions.
Mobile network mode is the only general radio setting exposed outside the
Cell Lock workflow; reboot, SIM PIN, carrier search, firmware and Wi-Fi settings
are not implemented.

## LTE history and Cell Lock

### Band & Cell Discovery

The LTE History action **Scan bands & cells** uses a model-profile registry
rather than scanning arbitrary mask bits. The verified MR600(EU) V5 profile
contains LTE-FDD B1/B3/B5/B7/B8/B20/B28 and LTE-TDD B38/B40/B41. Other router
revisions fall back to already observed bands until a verified profile is added.

For each candidate, the app applies a band-only selection, ignores the first four
seconds of potentially stale telemetry, and observes the remaining part of a
30-second window. It retains distinct serving EARFCN/PCI/CID identities plus
RSRP/RSRQ/SNR when exposed. A single-band result is accepted only when the live
band profile contains exactly the requested band, preventing a stale PCell+SCell
snapshot from being assigned to the next scan step.

The operation snapshots the complete existing band mask and Cell Lock state,
writes a pending-recovery record before changing the router, and restores the
snapshot on completion, cancellation or error. Exit waits for the restoration.
Scan-induced transitions are excluded from recommendation history, outage
attribution, automatic speed-test triggers and public-IP-change triggers. Full
results are appended to `band-cell-discovery.csv`. Lock-ready results are also
added to the normal LTE History store as candidates with no invented score,
speed, latency, or outage evidence.

### Mobile network mode

The validated MR600 web API exposes `networkPreferredMode` on `LTE_WAN_CFG`.
NetPulse maps the exact firmware values `3`, `2` and `1` to **4G preferred**,
**4G only** and **3G only**. It reads the current value before every write,
validates the selection against this list, writes only the selected property and
then reads it back for confirmation.

Newer TP-Link mobile firmware uses `DEV2_LTE_WAN_CFG` and reports both
`networkPreferredModeOptionList` and `networkPreferredModeSelected`. NetPulse
uses that returned option list for capability detection, so 5G choices appear
only when the connected firmware advertises them. This keeps the MR600 free of
invalid 5G settings while allowing compatible 5G/4G firmware to describe its own
supported modes.

The app maintains a private local history keyed by LTE band combination and
primary EARFCN, adding PCI and CID when available. Confirmed ping outages
are attributed to the most recently observed cell; transient router-page errors
are not counted as mobile disconnections. Speed-test results are attached only
when the same cell remains active for the complete test.

A ranked recommendation requires at least ten minutes of connected observation
and one speed test. Its normalized score is 50% average download, 40% confirmed
disconnections per connected hour and 10% average upload. Medium or high
confidence requires longer observation and multiple speed tests.

Each cell also has four local-time periods: night (00–06), morning (06–12),
afternoon (12–18) and evening (18–24). Current-period results are blended with
the all-time baseline as connected time and speed-test samples accumulate. The UI
shows that evidence weight plus the cell's share of observed traffic; if traffic
counters are unavailable it shows connection-time share instead. Usage changes
confidence, never the reliability/download/upload priority.

The MR600 v5 Cell Lock fields and band masks were verified against the installed
router page definitions. EARFCN and PCI are required. CID is optional. Manual
changes require confirmation. Automatic locking is disabled by default and, when
explicitly enabled, uses only medium/high-confidence recommendations. It checks
for a changing time-of-day winner every minute, requires a material improvement,
keeps a 30-minute minimum dwell, allows at most six changes per day and uses a
90-second internet/LTE validation window.

Before a change, NetPulse reads and stores the current band and Cell Lock state.
If validation fails, it restores that state. The rollback record remains in the
local settings file until validation succeeds or restoration completes, so an
interrupted app session can recover on the next launch. The user can always
choose **Restore automatic selection** to disable Cell Lock and set band selection
to Auto.

The validated v5 firmware returns live `rfInfoPCellBand`,
`rfInfoPCellChannel`, `rfInfoSCellBand` and `rfInfoSCellChannel`; the primary
channel is used as live EARFCN. In automatic mode it does not return live PCI or
CID. NetPulse therefore learns the B3/B3+B1/B3+B20-style profiles without those
identifiers, and applies a measured band-only profile with Cell Lock disabled.
When PCI is available, the same workflow applies the full cell + band target.
Missing values are never synthesized.

After a stable band or cell transition, NetPulse runs a 20 MB download / 5 MB
upload test and attributes it only when the same LTE state remains active through
the complete measurement. Confirmed outage recovery and public-IP changes also
queue an attributed test. Simultaneous triggers are coalesced after a 12-second
stability window; periodic tests are a separate setting.

## Local session behavior

1. The destination is normalized and restricted to a private LAN address.
2. Redirects are disabled.
3. NetPulse checks whether the router web interface already has an active user.
4. Setup can replace an existing management session only after explicit user
   confirmation. It then mirrors the MR600 web login: a busy request gets a
   short bounded wait, after which the takeover login proceeds even if the
   firmware keeps its `isBusy` flag set.
5. Login uses the firmware's AES-CBC and RSA signature exchange.
6. The session cookie and token remain in memory for that provider instance.
7. Telemetry object requests are serialized with a one-second refresh target.
8. Requests that take longer than one second consume their tick; they do not overlap.
9. Busy and offline failures back off automatically.
10. A rejected password stops automatic authentication attempts.
11. Shutdown attempts a short logout and then clears the in-memory session.

Connect and request stages have explicit time limits and support cancellation.

## Password storage

When **Remember password** is enabled, the password is stored as a generic
credential by Windows Credential Manager. It is never written to settings JSON,
CSV, diagnostic output, release packages or source control. Disabling remember
removes the stored credential and keeps the password only for the current app
session.

## Logging and privacy

Router telemetry uses a separate `router-telemetry.csv` file. Full Cell ID is
retained so results can be correlated with the actual serving antenna.
Passwords, hashes, AES keys, RSA signatures, cookies, tokens, raw request bodies,
raw responses, IMEI, MAC addresses, IP addresses, DNS addresses and SSIDs are
excluded.

The optional CID can also exist in `lte-cell-history.json` and in a temporary
rollback record in `settings.json`. A user-created ISP evidence package includes
full cell identifiers and IP diagnostics but excludes all authentication data.

## SMS operations

The validated MR600 firmware family exposes Inbox, Sent and Draft folders through
the corresponding `LTE_SMS_*MSGBOX` and `LTE_SMS_*MSGENTRY` objects. NetPulse
pages through at most 100 entries per folder using the firmware's GS action,
merges dated messages newest-first,
marks an opened Inbox entry read using `LTE_SMS_RECVMSGENTRY`, can restore unread
state, and deletes the exact selected Inbox/Sent/Draft stack. Every read-state
change and deletion is read back from the router before success is reported.
Sending and draft
saving use `LTE_SMS_SENDNEWMSG` through the same serialized provider gate as
telemetry.
Sending is always initiated and confirmed by the user. `sendResult` values `2`
and `3` are transient after this firmware accepts the message; NetPulse keeps
polling until `1` confirms completion, with no overall confirmation deadline.
Only application shutdown cancels the wait. Events record
the attempt, confirmation or error without recipient or message content.

The unread notification count comes from `smsUnreadCount` in the existing
`LTE_NET_STATUS` read. Draft objects do not expose a timestamp, so the UI labels
their time as unavailable. Message content and router history are not written to
settings, diagnostics, events or CSV logs. Explicitly saved contact names and
normalized numbers remain only in local settings.

The MR600 administration surface does not expose incoming voice-call events or a
call log, so call notifications cannot be implemented reliably. Carrier-generated
missed-call SMS messages continue to work as normal unread SMS.

## Remote access

TP-Link ID/Tether remote login is not implemented. No supported public desktop
cloud API for this router workflow is documented by TP-Link. NetPulse therefore
does not collect TP-Link account credentials or enable WAN administration. A
user-managed VPN back to the router LAN is the recommended remote approach.

## Verification

`tests/NetPulseMonitor.ProtocolTests` runs a local encrypted mock MR600 and checks:

- login AES/RSA compatibility and token propagation;
- LTE stack discovery and allowlisted telemetry request construction;
- telemetry parsing, LTE band mapping and 64-bit counters;
- unified SMS Inbox/Sent/Drafts parsing, unread updates, sending and draft saving;
- optional-CID Cell Lock construction and LTE band-mask encoding;
- restoration of the original automatic-selection state;
- LTE history filtering, time-period grouping and 50/40/10 weighted ranking;
- different morning/evening winners, evidence weighting and traffic-use share;
- clear wrong-password rejection;
- refusal to connect to a public Internet destination;
- outage, band, cell and public-IP speed-test trigger coordination;
- migration of ADSL/VDSL and FTTB/FTTH into their combined profiles.

No captured router traffic or proprietary firmware assets are committed.

## Official references

- [Archer MR600 V1 administration guide](https://www.tp-link.com/us/user-guides/archer-mr600_v1/chapter-12-administrate-your-network)
- [Archer MR600 SMS guide](https://www.tp-link.com/us/user-guides/archer-mr600_v1/chapter-8-sms)
- [Archer MR600 V1 login FAQ](https://www.tp-link.com/us/user-guides/archer-mr600_v1/faq)
- [TP-Link remote-management guidance](https://www.tp-link.com/us/support/faq/1553/)
- [TP-Link Tether remote-management guidance](https://www.tp-link.com/us/support/faq/1971/)
- [Official Archer MR600 v5 emulator](https://emulator.tp-link.com/MR600v5%20emulator/index.htm)
- [Archer MR600 network-mode guide](https://www.tp-link.com/us/user-guides/archer-mr600_v1/chapter-4-set-up-internet-connections)
- [Archer NX200/NX210 user guide](https://static.tp-link.com/upload/manual/2025/202505/20250509/1910013657_Archer%20NX210%28EU%291.0_UG_REV1.0.0.pdf)
- [TP-Link Cell Lock guide](https://www.tp-link.com/uk/support/faq/4986/)
