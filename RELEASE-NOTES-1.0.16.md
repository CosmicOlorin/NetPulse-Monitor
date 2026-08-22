# NetPulse Monitor 1.0.16

## Complete three-stage LTE discovery

- Stage 1 locks every supported band individually and waits for a stable,
  complete PCell EARFCN/PCI/CID identity before advancing.
- A registered band whose identifiers are still pending can be observed for up
  to 75 seconds; it is never saved as a lock-ready cell without CID.
- Stage 2 locks each discovered PCell while all bands actually found in Stage 1
  are available and records the ordered aggregation sets created by the modem.
- Stage 3 reapplies and measures every unique PCell + ordered band set, then
  stores its real radio samples in LTE History.
- B20 + B3 and B3 + B20 remain separate profiles because the first band is the
  PCell.

## MR600 identity reliability

- Adds a firmware-compatible fallback for MR600 V5 builds that expose PCI/CID
  through the live Cell Lock status rather than the normal LTE status response.
- The fallback is accepted only when its EARFCN exactly matches the current live
  serving EARFCN, so a stale configured target cannot be recorded as telemetry.
- Discovery now reports each stage explicitly in the UI and Event log. If a
  complete PCell was not exposed, it explains why Stages 2 and 3 did not run
  instead of reporting a misleading successful completion.

## Persistence

- Complete candidates are deduplicated by ordered band set and exact
  EARFCN/PCI/CID before being written to discovery CSV and LTE History.
- The result summary states the number of complete candidates actually saved.
- The original router band and Cell Lock state is still restored after normal
  completion, cancellation, or error.

The Android Companion is unchanged in this desktop-focused release and remains
version 1.0.15.

