# NetPulse Monitor 1.0.21

## Adaptive connection dashboard

- Merged Connection details into the Dashboard and removed the redundant tab.
- Added the Mobile/LTE, ADSL/VDSL and FTTB/FTTH selector directly to the live view.
- Added expandable router/line details without taking space from the ping chart when collapsed.
- LTE mode shows router RF, band and PCell data; DSL/Fiber mode keeps general Internet, gateway, DNS and session measurements without inventing unavailable line telemetry.

## Measurement-source clarity

- Labels PC-to-Internet ping, jitter, loss and speed tests separately from PC-to-router/DNS diagnostics.
- Labels LTE RF, band, CID, EARFCN, PCI and router rates as router telemetry.
- Documents that heavy local traffic, Wi-Fi/Ethernet and router queueing can affect end-to-end PC measurements.
- Excludes stale LTE radio penalties from the health score when a DSL/Fiber access profile is selected.

## Interface refinement

- Keeps session totals, current-connection identity/timer/outages and speed-test results in distinct sections.
- Adds source-aware hover help to the merged connection cards.
- Removes trailing dots from the Smart LTE Apply button.
