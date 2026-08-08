# TP-Link Archer MR600 integration

## Supported scope

NetPulse Monitor 1.0.4 includes optional local LTE telemetry for the
TP-Link Archer MR600 v5 firmware family. The validated target is hardware v5
running firmware `1.5.0 0.9.1 v0001.0 Build 251231 Rel.54154n`.

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

Telemetry requests are read-only. Reboot, SMS, SIM PIN, network selection,
firmware, Wi-Fi and general router settings are not implemented.

## LTE history and Cell Lock

The app maintains a private local history keyed by LTE band combination and
primary EARFCN, adding PCI and CID when available. Confirmed ping outages
are attributed to the most recently observed cell; transient router-page errors
are not counted as mobile disconnections. Speed-test results are attached only
when the same cell remains active for the complete test.

A ranked recommendation requires at least ten minutes of connected observation
and one speed test. Confirmed disconnections per connected hour are compared
first, average download speed second and average upload speed third. Medium or
high confidence requires longer observation and multiple speed tests.

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
   confirmation, matching the MR600 web login's takeover behavior.
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

Router telemetry uses a separate `router-telemetry.csv` file. Cell ID is masked.
Passwords, hashes, AES keys, RSA signatures, cookies, tokens, raw request bodies,
raw responses, IMEI, MAC addresses, IP addresses, DNS addresses and SSIDs are
excluded.

The unmasked optional CID can exist in `lte-cell-history.json` and in a temporary
rollback record in `settings.json`. Both stay under `%LOCALAPPDATA%\NetPulseMonitor`
and are never included in CSV or diagnostics.

## SMS operations

The validated MR600 v5 firmware exposes the SIM inbox through
`LTE_SMS_RECVMSGBOX` and `LTE_SMS_RECVMSGENTRY`. NetPulse pages through at most
100 messages, marks only the opened entry with `unread=0`, and sends through
`LTE_SMS_SENDNEWMSG` using the same serialized provider gate as telemetry.
Sending is always initiated and confirmed by the user. `sendResult` is polled
with a bounded timeout; busy and failure states are reported without an automatic
retry that could duplicate a message.

The unread notification count comes from `smsUnreadCount` in the existing
`LTE_NET_STATUS` read. Message sender, recipient and content are not written to
settings, diagnostics, events or CSV logs.

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
- [Archer MR600 V1 login FAQ](https://www.tp-link.com/us/user-guides/archer-mr600_v1/faq)
- [TP-Link remote-management guidance](https://www.tp-link.com/us/support/faq/1553/)
- [TP-Link Tether remote-management guidance](https://www.tp-link.com/us/support/faq/1971/)
- [Official Archer MR600 v5 emulator](https://emulator.tp-link.com/MR600v5%20emulator/index.htm)
- [TP-Link Cell Lock guide](https://www.tp-link.com/uk/support/faq/4986/)
