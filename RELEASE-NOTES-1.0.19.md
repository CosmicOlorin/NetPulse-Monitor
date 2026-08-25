# NetPulse Monitor 1.0.19

## LTE ranking, history and dashboard

- Changed LTE Rank to 50% controlled connection reliability, 25% normalized
  download and 25% normalized upload. Missing evidence contributes zero.
- Kept RF score separate from Rank and added detailed RF hover breakdowns for
  TP-Link Signal, SINR/SNR, RSRQ, RSRP and their weighted contributions.
- Added blue active-set and purple recommended-set highlighting, plus graded
  controlled-test failure/rollback coloring in LTE History.
- Added current LTE set/cell, public IP and since-open session averages to the
  Dashboard, with continuous deep-red-to-green quality coloring.
- Preserved the existing RUN TIME and added an independent current connection
  + set + IP timer. It resets only when the ordered band set, CID, EARFCN, PCI
  or public IP changes.
- Added a per-connection confirmed-outage counter with the same reset identity;
  an outage increments the counter without resetting its timer.

## Desktop reliability and interface help

- Reopening NetPulse from the taskbar now detects the existing process and
  restores its tray-minimized window instead of starting a duplicate instance.
- Expanded hover help for controls, LTE History column headings and measured
  values.
- Corrected dark-theme rendering in setup and discovery windows.

The Windows and Android packages retain the persistent Companion pairing and
authenticated in-app update flow from 1.0.18.
