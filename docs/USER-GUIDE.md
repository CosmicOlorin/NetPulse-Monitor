# NetPulse Monitor user guide

## Diagnostics

The **Diagnostics** tab is an on-demand check. It does not change Windows or
router settings.

- **Default gateway**: the local address through which the PC reaches the router
  or upstream network. A missing gateway usually means the active Windows adapter
  is not fully connected.
- **Gateway latency**: round-trip time from the PC to that gateway. This mainly
  reflects the local Wi-Fi/Ethernet path, not the complete internet route.
- **DNS lookup latency**: time required to resolve a hostname. A slow result can
  indicate DNS delay even when ordinary ping remains healthy.
- **IPv4**: whether an operational IPv4 path is available to the PC.
- **IPv6**: whether an operational IPv6 path is available to the PC. IPv6 may be
  unavailable even when IPv4 internet works normally.

Values remain **Not measured** until **Run diagnostics** is selected.

## LTE history columns

Each normal row is one observed performance profile: a band combination on a
specific primary cell during the time period shown by its group. Night, Morning,
Afternoon and Evening are separate collapsible groups. Band combinations and
PCell identities remain normal rows; they no longer create group headers.

A measured connection is stored immediately but remains hidden from LTE History
until it reaches five connected minutes in that time period. This removes
short-lived carrier-aggregation combinations without discarding their evidence.
An unmeasured profile explicitly added in the Cell Lock tab remains visible as a
manual entry. Automatic refreshes preserve the visible row and do not move the
grid back to the top while the user is reading or scrolling.

- **Rank**: recommendation order. Rank 1 is the preferred eligible profile.
- **Period**: local-time bucket used for the current recommendation.
- **Band**: LTE band profile, for example B3 or B3 + B20.
- **EARFCN**: channel number of the primary LTE carrier.
- **PCI**: Physical Cell Identity, when known.
- **CID**: optional Cell ID, when known.
- **Seen**: connected time collected in the current period.
- **Use %**: share of observed data traffic; connection-time share is used when
  traffic counters are unavailable.
- **Time wt.**: influence of current-period evidence on the all-time baseline.
- **Drops P/A**: confirmed disconnects in the current **Period / All time**. A
  value of `1 / 4` means one disconnect in this period and four in total.
- **Drop/h**: time-weighted confirmed disconnects per connected hour.
- **Down**: time-weighted average speed-test download result.
- **Up**: time-weighted average speed-test upload result.
- **Confidence**: whether enough connected time and speed-test evidence exists.

Click any column header to sort the rows inside each time-period group; click it
again to reverse the order. Click a time-period group row to collapse or expand
it. Sorting changes only the display. Rank is calculated from a normalized score:
50% download, 40% confirmed disconnections per connected hour and 10% upload.
Confidence controls whether a profile has enough evidence to rank; usage share
does not add a hidden score.

Download and upload receive `current result / fastest eligible result × 100`
points inside the same time period. Reliability receives
`100 / (1 + confirmed drops per connected hour)` points. This gives zero-drop
profiles full reliability credit without allowing reliability alone to override
the combined 60% download/upload contribution.

## Time periods

Time periods use the Windows local clock:

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

When PCell and EARFCN remain unchanged, NetPulse keeps a previously known PCI and
CID while the secondary-band combination changes. If either PCell or EARFCN
changes, those identifiers are not copied. This prevents an unrelated primary
cell from inheriting old identity data.

The different combinations remain separate performance profiles because B3 and
B3 + B20 may have different speed and stability even when their PCell is the
same. Confirmed disconnects are attributed to the profile that was active before
the outage.

## Manual Cell Lock

The **Cell Lock** tab accepts:

- one or more bands, such as `B3` or `B3 + B20`;
- primary EARFCN;
- PCI;
- optional CID.

**Save profile to history** records only the supplied identity. It does not add
fake connection time, speed tests or reliability results. **Save and apply
lock...** requests confirmation, stores the current MR600 state, applies the
target and validates connectivity. If validation fails, the previous state is
restored. **Restore automatic selection** disables Cell Lock and returns band
selection to Auto.

## SMS

The **SMS** tab uses the SIM installed in the MR600:

- unread count is obtained with the regular one-second LTE status update;
- Windows shows a notification when the unread count increases;
- selecting an unread message displays it and marks it read on the router;
- **Reply** copies the sender into the recipient field;
- **New message** clears the composer;
- **Send SMS...** validates the number and encoding, asks for confirmation and
  sends through the MR600.

The firmware permits phone numbers of up to 20 characters including an optional
leading `+`. Its maximum is 765 GSM-7 units or 335 Unicode characters. The
composer displays the applicable limit.

SMS sender, recipient and content remain in application memory only. They are
never written to CSV files, diagnostics, settings or event logs. NetPulse does
not send a test SMS automatically.

## Single MR600 management session

The MR600 permits only one active management login. While NetPulse monitoring is
running, it owns that session so one-second telemetry and SMS remain available.
Exit NetPulse before signing into the router webpage. Normal application exit
logs the router session out.
