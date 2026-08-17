# NetPulse Monitor 1.0.10

## Mobile Companion

- Identical LTE profiles are now shown once across all time-of-day periods.
- Profile identity remains strict: ordered band group, EARFCN, PCI, and CID must match.
- Carrier-aggregation order is preserved, so `B3 + B28` and `B28 + B3` remain different profiles.
- Connected time and disconnections are combined, while measured performance is aggregated into the single profile.
- Each row now shows the complete EARFCN, PCI, and CID identity instead of repeating the time-period variant.
