# NetPulse Monitor 1.0.2

## New

- MR600 SIM inbox with unread Windows notifications, read/reply and
  user-confirmed direct SMS sending.
- Fixed-fit manual Cell Lock tab for known band, EARFCN, PCI and optional CID
  profiles, including save-to-history without fabricated measurements.
- Sortable LTE history headers and collapsible grouping by primary cell.
- Optional TP-Link Archer MR600 v5 local telemetry.
- Password-only first-run setup with Windows Credential Manager protection.
- One-second serialized LTE refresh with cancellation and protective backoff.
- Live ISP, network, PCell/SCell bands, primary EARFCN, signal, RSRP, RSRQ,
  SNR, SIM, usage and rate cards; PCI/CID appear only when firmware exposes them.
- Independent access profiles for LTE, combined ADSL/VDSL and combined FTTB/FTTH.
- Independent auto-detect, Wi-Fi and Ethernet PC-link selection.
- Active Windows adapter and negotiated link-speed display.
- Dedicated privacy-filtered router telemetry CSV log.
- Dependency-free encrypted MR600 protocol regression test.
- Persistent LTE history using band combination and primary EARFCN, with PCI/CID
  added when available.
- Reliability-first cell/band suggestions using confirmed disconnects, download
  and upload results in that order.
- Night/morning/afternoon/evening learning with visible time-evidence and data-use
  share; usage affects confidence without replacing performance ranking.
- Confirmed manual Cell Lock plus opt-in adaptive locking with a 30-minute dwell,
  hysteresis, six-change daily limit, 90-second validation, rollback and
  interrupted-session recovery.
- Automatic attributed 20 MB / 5 MB speed tests after outage recovery and stable
  LTE band, cell or public-IP changes, independent of the periodic-test setting.

## Improved

- Known PCI/CID are preserved across carrier-aggregation changes only while
  PCell and EARFCN remain unchanged.
- Main-window dimensions are derived from the current Windows working area and
  cannot shrink below the complete fixed-fit layout.
- Upload testing now falls back from the primary endpoint to a bounded, cached
  set of independent HTTPS backends from the official LibreSpeed directory.
- Every dashboard value uses DPI-aware fitting or safe elision.
- Every main tab and the TP-Link setup dialog use fixed-fit layouts without
  scroll-only controls at supported sizes.
- Measurement cards start empty and add values and units only after real samples;
  elapsed times use compact units instead of padded zero fields.
- Band/EARFCN learning remains active when MR600 Auto mode hides PCI/CID;
  optimization safely falls back to band-only selection in that case.
- Router timeouts, cancellation, session expiry, busy state and wrong-password
  behavior now have explicit handling.
- Opening router setup releases the active telemetry session before testing and
  reconnects it after the dialog closes, avoiding self-created busy conflicts.
- When another MR600 management session exists, setup can explicitly take it over
  using the router firmware's normal login behavior; takeover is never silent.
- The Test connection action and save controls remain visible at the setup
  window's minimum supported size.
- The source and release remain a Windows GUI application with no console window.

## Important TP-Link note

MR600 firmware permits only one local administration session. NetPulse owns that
session while live telemetry runs, so close NetPulse before using the router web
page. TP-Link ID cloud login is not included; use a private VPN for remote access
to the local provider.
