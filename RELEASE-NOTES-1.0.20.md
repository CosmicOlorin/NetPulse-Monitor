# NetPulse Monitor 1.0.20

## Clearer dashboard scopes

- Reorganized the Dashboard into three explicit sections: Current Connection,
  Session Since Open / Reset and Last Speed Test.
- Split session averages into independent average ping, average jitter and
  average packet-loss cards, each with its own quality color and hover help.
- Changed Session Failures to show both the failed-sample count and its
  percentage of all session samples.
- Kept recent/current measurements separate from complete-session statistics
  so values from different time scopes are no longer mixed in one row.

## Display and diagnostics fixes

- Corrected the connection-health score layout so a score of 100 is never
  clipped to 10 at Windows display scaling levels.
- Renamed and clarified Local Network Checks, now showing the gateway address,
  gateway latency, DNS latency and IPv4/IPv6 availability with full hover detail.
- Simplified LTE History RF hover details to the measured TP-Link signal,
  SNR/SINR, RSRQ and RSRP values without repeating component score math.

The Windows and Android packages retain the persistent Companion pairing,
authenticated in-app updates and direct-main release workflow from 1.0.19.
