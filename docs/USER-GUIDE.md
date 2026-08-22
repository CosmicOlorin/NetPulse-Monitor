# NetPulse Monitor user guide

## Dashboard and Connection Health

The Dashboard begins with three relative-size cards. **Connection Health** gives
a 0-100 summary derived from confirmed Internet state, loss, latency, jitter,
availability, local gateway/DNS checks and, when available, LTE quality. The
score explains the strongest measured factor; it is a diagnostic summary, not a
provider service guarantee.

**Smart LTE recommendation** shows the best eligible profile for the selected
location's current time period. **Apply safely...** uses the same confirmation,
connectivity validation and rollback as LTE History. **Test current** records a
new comparable 20 MB download / 5 MB upload result without changing the router.

Settings offers **Simple**, **LTE Advanced**, **DSL / Fiber** and **ISP
troubleshooting** dashboard layouts plus System, Light and Dark themes. These
change presentation only; monitoring and logging continue unchanged.

## Diagnostics

The Dashboard's **Local path** strip refreshes every 30 seconds and shows the
current gateway latency, DNS lookup latency, IPv4 and IPv6 availability. These
checks are useful for LTE, DSL and fiber: they distinguish a local gateway or
DNS problem from a wider internet outage. They run asynchronously and do not
change Windows or router settings.

The **Diagnostics** tab shows the same latest measurements in full, includes the
default gateway address, provides an immediate **Run diagnostics** action and
 hosts the full technical ISP evidence export.

- **Default gateway**: the local address through which the PC reaches the router
  or upstream network. A missing gateway usually means the active Windows adapter
  is not fully connected.
- **Gateway latency**: round-trip time from the PC to that gateway. This mainly
  reflects the local path to the router, not the complete internet route.
- **DNS lookup latency**: time required to resolve a hostname. A slow result can
  indicate DNS delay even when ordinary ping remains healthy.
- **IPv4**: whether an operational IPv4 path is available to the PC.
- **IPv6**: whether an operational IPv6 path is available to the PC. IPv6 may be
  unavailable even when IPv4 internet works normally.

Values are measured automatically after startup; **Run diagnostics** requests an
additional immediate refresh.

## LTE history columns

### Band & Cell Discovery

Select **Scan bands & cells** below LTE History to run the complete three-stage
discovery flow. The MR600(EU) V5 plan is
B1/B3/B5/B7/B8/B20/B28/B38/B40/B41:

1. Each band is locked alone for at least 30 seconds. If a serving band appears
   before its identifiers, NetPulse waits up to 75 seconds for three consecutive
   matching EARFCN/PCI/CID readings.
2. Each complete PCell is locked while every band actually found in Stage 1 is
   made available. The modem-selected ordered aggregation sets are recorded.
3. Every unique PCell + ordered set is reapplied and measured. The first band is
   always the PCell, so B20 + B3 and B3 + B20 are different profiles.

**Cancel discovery** safely stops the remaining work.

The result window lists the requested band, serving profile, EARFCN, PCI, CID,
RSRP, RSRQ, SNR and sample count. `No serving cell observed` means that the modem
did not register and expose a complete serving identity in the allowed window;
it does not prove
that no distant signal exists. Firmware that hides an identifier leaves it blank
rather than receiving an inferred value. The complete result is also appended to
`Documents\NetPulse-Monitor\band-cell-discovery.csv`. Only exact results with a
valid EARFCN, PCI and CID appear in LTE History as lock-ready candidates. Stage
3 supplies real radio measurements; a result is never made lock-ready using an
incomplete or inferred identity.

All LTE History buttons explain their action on hover. **Delete selected
profile...** removes only the selected identity; **Clear LTE history** remains
the separate whole-history action.

Discovery deliberately changes the selected band and may interrupt Internet
access. Its changes are not counted as ordinary LTE-history failures or used to
rank cells. NetPulse reads the exact existing router lock state first and restores
it after completion, cancellation or error. A real process exit waits for that
restoration. The router API exposes serving cells, not a complete RF neighbor
database, so this mode does not claim to find cells that the modem never selects
or reports.

Each normal row is one observed performance profile: an ordered band combination
on one exact PCell CID/EARFCN/PCI identity. The grid is a single ungrouped list.
NetPulse still selects the relevant Night, Morning, Afternoon or Evening evidence
internally from the connection location's official time zone.

A measured connection is stored immediately but remains hidden from LTE History
until it reaches five connected minutes in that time period. This removes
short-lived carrier-aggregation combinations without discarding their evidence.
An unmeasured profile explicitly added in the Cell Lock tab remains visible as a
manual entry. Automatic refreshes preserve the visible row and do not move the
grid back to the top while the user is reading or scrolling.

The profile that the router is using now is highlighted in green after it has accumulated the five minutes
required to appear in the history.

- **Rank**: recommendation order. Rank 1 is the preferred eligible profile.
- **Band**: LTE band profile, for example B3 or B3 + B20.
- **EARFCN**: channel number of the primary LTE carrier.
- **PCI**: Physical Cell Identity, when known.
- **CID**: required Cell ID for a history identity and recommendation.
- **Seen**: connected time collected in the current period.
- **Avg ping**: mean successful ping measured while this profile was active.
- **Cell load\***: an estimate derived from the profile's current-period download
  result compared with its own observed best. It is not a carrier-provided tower
  utilization percentage; `-` means that no comparable speed evidence exists.
- **Drops P/A**: confirmed disconnects in the current **Period / All time**. A
  value of `1 / 4` means one disconnect in this period and four in total.
- **Drop/h**: time-weighted confirmed disconnects per connected hour.
- **Down**: time-weighted average speed-test download result.
- **Up**: time-weighted average speed-test upload result.
- **RF score**: 50% SINR, 35% RSRQ and 15% RSRP.
- **Confidence**: whether enough connected time and complete radio evidence exists.

Click any column header to sort the single ungrouped list; click it again to
reverse the order. Sorting changes only the display. Rank uses 50% SINR, 35% RSRQ
and 15% RSRP. Download, upload, confirmed disconnects, average ping and estimated
load remain explanatory measurements and do not add a hidden score.

## Time periods

Time periods use the official time zone selected for the connection location,
not the Windows machine's local clock:

- **Night 00-06**: 00:00 through 05:59.
- **Morning 06-12**: 06:00 through 11:59.
- **Afternoon 12-18**: 12:00 through 17:59.
- **Evening 18-24**: 18:00 through 23:59.

NetPulse blends each period with all-time history. A small amount of night data
therefore cannot immediately override a well-established baseline.

## PCell, carrier aggregation and identifiers

The first band is the primary carrier (PCell); additional bands are secondary
carriers (SCells). For example, B3 becoming B3 + B20 normally means that B20 was
added to the existing B3 primary connection.

When a PCell-only state becomes PCell + SCell, NetPulse carries EARFCN, PCI and
CID only from the immediately preceding live state and only if the PCell remains
the same. It never searches unrelated older rows to fill missing identifiers. If
the PCell changes, nothing is inherited and the new values remain unknown until
the router exposes them.

Configured **Cell Lock** target values are never treated as live serving-cell
telemetry. EARFCN is also checked against the serving PCell band before a sample
enters history. A one-time local migration repairs mismatched legacy EARFCNs,
merges unambiguous complete/incomplete duplicates and keeps a small safety backup
beside the history file. EARFCN 100 is valid for Band 1; it is rejected or
repaired when it had been incorrectly attached to another PCell band.

The different combinations remain separate performance profiles because B3 and
B3 + B20 may have different speed and stability even when their PCell is the
same. Confirmed disconnects are attributed to the profile that was active before
the outage.

## Manual Cell Lock

The **Cell Lock** tab accepts:

- one or more bands, such as `B3` or `B3 + B20`;
- primary EARFCN;
- PCI;
- required decimal or hexadecimal CID, such as the synthetic example `ABCDE`
  or `0xABCDE`.

Its first field lists previously observed sets that have at least five connected
minutes, with the currently active set first. Choosing one fills its band profile,
primary EARFCN and any known PCI/CID into the manual fields for review. It never
applies a lock automatically from this list.

**Save profile to history** records only the supplied identity. It does not add
fake connection time, speed tests or reliability results. **Save and apply
lock...** requests confirmation, stores the current MR600 state, applies the
target and validates connectivity. If validation fails, the previous state is
restored. **Restore automatic selection** disables Cell Lock and returns band
selection to Auto.

**Run controlled experiment...** compares up to three eligible profiles in the
current official-time period. The user chooses the per-profile duration in
Settings. Every profile change uses normal rollback validation, the comparable
speed-test sample is recorded, cancellation is available, and the exact router
state from before the experiment is restored in a `finally` recovery path. Only
after restoration does NetPulse offer to apply the measured winner. The ranking
remains 50% SINR, 35% RSRQ and 15% RSRP.

## SMS

The **SMS** tab uses the SIM installed in the MR600. **Conversations** groups
Inbox, Sent and Draft entries by contact/number and keeps each conversation in
chronological order; **Timeline** shows the full newest-first list. Search matches
saved contact names, numbers and message text in memory only.

- unread count is obtained with the regular one-second LTE status update;
- Windows queues one notification for each newly discovered unread message;
- the same message never repeats a notification, including after an app restart;
- the complete Inbox/Sent/Drafts timeline is refreshed automatically every 30
  minutes, including while the SMS tab is not open;
- unread message content remains hidden while NetPulse asks the MR600 to mark it
  read; the list and action controls are locked until the router confirms, and
  only then is the content displayed;
- a refresh preserves the selected message and scroll position instead of moving
  the selection to another row;
- Inbox, Sent and Draft messages appear in one newest-first timeline;
- drafts have **Time unavailable** because the MR600 does not expose a draft
  timestamp;
- selecting an unread message displays it and marks it read on the router;
- **Mark read** and **Mark unread** update the selected Inbox entry on the router;
- **Delete...** removes the selected Inbox, Sent or Draft entry after confirmation;
- read/unread and delete report success only after a router read-back confirms
  that the selected message actually changed or disappeared;
- an open conversation automatically keeps its number in the recipient field;
  write the response and use **Send SMS** directly below the composer;
- **New SMS** opens a blank conversation without showing unrelated history. If
  the entered number matches an existing contact, NetPulse joins the existing
  conversation without discarding the text being composed;
- **Save draft** stores the current message in the router's Drafts folder;
- **Save contact...** assigns or removes a local name for a phone number;
- **Send SMS** validates the number and encoding, asks for confirmation and
  sends through the MR600.

Phone numbers may contain spaces, parentheses, hyphens and an optional
international prefix. Conversation matching uses the country selected during
first setup, so—for Greece—the synthetic examples `+306991234567`,
`00306991234567` and `6991234567`
are treated as the same number. The router payload is limited to 20 digits. The
message maximum is 765 GSM-7 units or 335 Unicode characters, and the composer
displays the applicable limit.

SMS content and router message history remain in application memory only. A
SHA-256 fingerprint (not the number or content) is retained locally solely to
prevent duplicate notifications after restart. SMS data is never written to CSV
files, diagnostics, settings or event logs. Contact
names and normalized phone numbers are saved in the local settings file only
when the user explicitly requests it. NetPulse does not send a test SMS
automatically.

The **Timeline** tab and `connection-events.csv` record each outgoing attempt,
final confirmation and error without the recipient or message content. After the
MR600 accepts a message, NetPulse waits through its intermediate sending states
without an overall confirmation deadline. Only exiting the application cancels
that wait, preventing an already accepted message from being sent twice.

## Timeline, notifications and update checks

The Timeline combines connectivity, LTE profile changes, speed tests, SMS
operations and system/diagnostic events. Category and text filters affect only
the visible grid. Confirmed offline and online transitions can also appear as
Windows tray notifications; unread-SMS notifications remain one notification per
message identity.

Update checks query only the repository's latest public GitHub release metadata.
They can run manually or, when enabled, at most once per day. After confirmation,
NetPulse downloads, verifies and installs the Windows executable without opening
a browser. A newer executable placed beside the application as
`NetPulse Monitor.update.exe` is used before GitHub. The stable
`NetPulse Monitor.exe` path and Windows identity preserve pins, shortcuts,
startup configuration and notification identity across releases.

## Connected devices

The **Devices** tab reads the router's current active LAN-client table and shows
the device name, IP address, MAC address and Wi-Fi/Ethernet type when the
firmware exposes them. The Android Companion provides the same view through the
encrypted paired-PC protocol. This list is live UI data: NetPulse does not save
it to settings, CSV logs, LTE history or release artifacts.

## Country and official timestamps

LTE history period groups (`Night 00–06`, `Morning 06–12`, `Afternoon 12–18`
and `Evening 18–24`) use the selected location's official time zone. They do
not use the Windows machine time zone. Changing the Windows clock or running
NetPulse remotely therefore does not move new observations into another period.

On first run, NetPulse asks for the country and exact official time zone. This
selection controls country-style dates and the UTC conversion used in the UI,
CSV logs and ISP evidence. It does not read from or change the Windows display
time zone, so an unusual PC clock cannot alter the official timeline submitted
to an ISP. The selection can be changed later under **Settings**.

## Full technical ISP evidence

The **Diagnostics** tab can create an ISP evidence ZIP for the currently selected
ADSL/VDSL, FTTB/FTTH or Mobile/LTE profile. It contains a session summary and up
to 30 days of outage, speed-test and router-telemetry evidence, plus an LTE
history summary grouped by official time period. The summary
includes the current public IP, local IPv4/IPv6 addresses, gateway and, for LTE,
the full band profile, PCell band, EARFCN, PCI and CID exposed by the MR600.
The history summary preserves those identifiers alongside connected time,
disconnections, average ping, estimated cell load and speed results.

The ZIP never contains TP-Link credentials or tokens, application settings, SMS
content or phone numbers, saved contacts, screenshots or router authentication
material. It intentionally retains relevant technical identifiers and event
details so an ISP can investigate the actual line, address and antenna path.
Review the included `EVIDENCE-CONTENTS.txt` before sending the ZIP.

The MR600 exposes SMS but no incoming voice-call event or call log. NetPulse
therefore cannot reliably notify for calls made to the SIM. If the mobile
provider sends a missed-call notification as an SMS, it appears normally in the
timeline and triggers the unread-SMS notification.

## Single MR600 management session

The MR600 permits only one active management login. While NetPulse monitoring is
running, it automatically takes and retains that session so one-second telemetry
and SMS remain available, including after a browser or app has replaced the
session. Exit NetPulse before signing into the router webpage. Normal application
exit logs the router session out.

## Mobile network mode

Open **Settings**, then use **Mobile network mode > Refresh**. NetPulse reads the
current mode and supported choices from the connected TP-Link firmware rather
than presenting a universal hard-coded list. Select a reported choice and press
**Apply**; a confirmation appears because the router may briefly disconnect while
it registers again.

For an Archer MR600, the list is **4G preferred (4G / 3G)**, **4G only** and
**3G only**. No 5G choice is shown because the MR600 is a 4G+ LTE-A router.
Compatible modern TP-Link 5G firmware supplies its own list, which can include
**5G / 4G / 3G**, **5G preferred**, **5G only**, **4G preferred**, **4G only**
and **3G only**. Availability still depends on the exact model, hardware revision,
firmware and region.

## Window sizing

NetPulse opens at a screen-relative size. The window can be reduced to a smaller
screen-relative minimum (never below 920 x 640 logical pixels), while tabs and
their main labels remain visible. Large data grids continue to use their own
normal scrolling area.

Settings controls use fixed-height, vertically centered rows so labels and
inputs remain aligned on tall and high-DPI screens. Diagnostics uses five
flexible result rows plus a dedicated button row, preventing overlap at the
minimum window size.

