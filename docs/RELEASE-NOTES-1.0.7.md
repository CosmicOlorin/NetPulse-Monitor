# NetPulse Monitor 1.0.7

## Interface and accessibility

- Corrected high-DPI text clipping in dashboard and router metric cards by
  measuring each caption against its actual card and font.
- Reworked dark-theme contrast across LTE History, SMS conversations, manual
  profiles, selected rows, disabled controls and input hints.
- Dark-theme combo boxes and text inputs now use a consistent input palette.
- The complete tab strip now follows the selected theme, including the area
  after the final tab.
- The native Windows title bar, caption text and outer border now follow the
  selected System, Light or Dark theme instead of retaining a light frame.
- Dark mode now uses one restrained surface, border and accent palette across
  tabs, page frames, grids and buttons; native white 3-D outlines are removed.
- Tab and button outlines now use a darker low-contrast border instead of a
  bright frame.
- The LTE Simple dashboard is a balanced three-by-two metric layout, while the
  Advanced dashboard retains the complete four-by-three view.
- The Updates card and Settings show the application version in concise,
  user-facing language.
- Removed the Wi-Fi/Ethernet PC-link selector and related report field because
  they do not affect NetPulse Internet or LTE monitoring. The released space now
  shows outage count and total downtime instead.

## LTE history and SMS

- Incomplete carrier-aggregation observations reuse a uniquely known PCell
  identity only when the primary band and EARFCN remain compatible; matching
  rows are merged without inventing PCI or CID values.
- The current recommendation is concise, and LTE History contains only direct
  history, experiment, automatic-mode and lock-copy actions.
- SMS uses conversation threads with the active recipient fixed automatically;
  **New SMS** starts an empty thread and **Send SMS** sits directly below the
  composer.
- Country-aware number matching joins national, `+` and `00` formats into the
  same conversation. For example, Greek local and `+30` forms no longer split.

## TP-Link integration

- Added a firmware-aware **Mobile network mode** control under Settings. The
  Archer MR600 exposes only 4G preferred, 4G only and 3G only; compatible modern
  TP-Link firmware supplies its own 5G/4G option list. Every change is confirmed,
  validated against the router-reported choices and read back after application.

- Added a cancelable **Band & Cell Discovery** mode. The verified MR600(EU) V5
  profile scans B1/B3/B5/B7/B8/B20/B28/B38/B40/B41 one at a time, records every
  distinct serving EARFCN/PCI/CID exposed during each window, writes a dedicated
  CSV and restores the exact previous router lock state.
- Exact discovery results and earlier exact band-group identities are restored
  to LTE History as unranked, lock-ready candidates. Real measurements remain
  empty until the profile is observed or tested.
- LTE History actions include explanatory hover text and an individual
  **Delete selected profile...** action.
- The discovery action is permanently visible in the first action row as
  **Scan bands & cells**, including at smaller supported window widths.
- Documented the LTE compatibility matrix: fully validated MR600(EU) V5,
  network-mode coverage for NX200/NX210, and capability-detected fallback for
  other TP-Link LTE/5G firmware without overstating full support.
- Discovery-induced transitions are excluded from recommendations, outage
  attribution and automatic speed-test triggers. Unknown router revisions scan
  observed bands only rather than receiving speculative band masks.

- The interface no longer assumes a fixed router model. NetPulse reads and
  displays the model reported by the connected TP-Link device.
- General TP-Link monitoring language is used throughout setup and Settings.
- LTE telemetry, Cell Lock and SIM SMS remain capability-dependent because
  TP-Link firmware families do not expose the same local management APIs.
- The existing Archer MR600 v5 integration remains fully supported and is the
  validated LTE reference implementation.

## Compatibility

Settings, protected TP-Link credentials, LTE history, saved SMS contact names
and CSV files remain compatible with 1.0.6. The Windows x64 executable remains a
self-contained GUI application with no console window.
The executable also carries the author identity marker
`CosmicOlorin 2026 (c)` alongside its Windows copyright metadata.
