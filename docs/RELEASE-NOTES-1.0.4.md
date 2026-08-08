# NetPulse Monitor 1.0.4

## Improved LTE History

- Connections shorter than five connected minutes in a time period are hidden
  from the user while continuing to accumulate evidence internally.
- A profile appears automatically when it reaches the five-minute threshold.
- Explicit manual Cell Lock profiles with no measurements remain visible as
  manual entries.
- Automatic LTE History refreshes preserve the first visible profile or time
  group, so reading and scrolling no longer jump back to the top.
- Time-group rows use a single compact plus/minus indicator instead of competing
  arrow elements, and automatic refresh leaves group rows unselected.

## Unchanged

- Time-period grouping and the 50% download / 40% disconnections / 10% upload
  ranking introduced in 1.0.3 are unchanged.
- Hidden short connections are not deleted and become visible after enough
  connected time is collected.
- Router credentials, LTE history files, SMS handling and existing settings are
  fully compatible with 1.0.3.
