# NetPulse Monitor 1.0.15

## Companion SMS

- Shows Inbox, Sent and Draft folder labels in the Companion SMS list.
- Selecting a draft loads its recipient and content into the composer.
- A loaded draft can be reviewed and sent again; after the router confirms delivery, the saved draft is removed.

## LTE Cell Lock

- Cell Lock now sends only the measured PCell band, EARFCN, PCI and CID to the router.
- Carrier-aggregation SCells remain under the modem's automatic control, matching the TP-Link interface.
- LTE History continues to show the complete observed PCell + SCell profile for measurement and comparison.
- Band Lock is available independently from Cell Lock in both the desktop and Companion apps.
- A dedicated single-band scan locks one selected band temporarily and records every distinct serving EARFCN, PCI and CID exposed during the scan directly as LTE History candidates.
- Full discovery now runs three stages: every single band, every discovered CID lock, then every unique serving aggregation set for radio measurement.
- LTE History shows every known set once. Current-period evidence is projected onto it; sets without current-period usage remain trial-ready and show an awaiting-usage status.

## LTE scoring

- Recommendations now use measured radio quality: 50% SINR/SNR, 35% RSRQ and 15% RSRP.
- Throughput and disconnection history remain visible evidence but no longer determine the recommendation score.
- Removes the obsolete Period column while retaining official-location time-of-day projection internally.
- Rejects and migrates away legacy history rows that have no CID instead of presenting ambiguous cell identities.
- Applying an aggregation profile now writes the complete ordered band set; the first band remains the PCell for Cell Lock.
- Scheduled speed tests are cancelled when a new LTE profile change starts and no longer block the LTE controls.
- Discovery shows an activity bar and disables competing profile mutations until the original router state is restored.
- Active Cell Lock details show CID, PCI and EARFCN even when a transient telemetry response omits them.

## Dependency policy

- Runtime features rely on Windows/Android platform components or versioned project artifacts.
- Optional build dependencies are acquired by the GitHub build workflow instead of being left as unmanaged local SDK downloads.
- Temporary development downloads are tracked and removed after verified release packaging; user settings, logs and LTE/SMS history are never part of that cleanup.

## Connected devices

- Adds a live **Devices** tab to the Windows app and Android Companion.
- Shows active clients reported by the TP-Link router with device name, IP address, MAC address and Wi-Fi/Ethernet connection type when exposed by the firmware.
- The device list is read on demand, refreshed while visible and never written to settings, history or release files.

## Stable in-app updates

- The installed/portable program now keeps the stable filename `NetPulse Monitor.exe` and the stable Windows identity `CosmicOlorin.NetPulseMonitor` across releases.
- Update checks use a newer executable placed in the local production folder first, or the latest GitHub release asset otherwise.
- Updates download with in-app progress, validate the executable version and available SHA-256 checksum, replace the same executable after shutdown, then restart automatically.
- No browser hand-off is required, so Start/taskbar pins, the startup entry and the notification/tray identity continue to target the same application path.
