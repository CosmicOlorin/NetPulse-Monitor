# NetPulse Monitor 1.0.13

This maintenance release makes the serving-cell CID a required part of every measured LTE history identity.

- Profiles with the same ordered band set, EARFCN, and PCI but different CIDs are stored and evaluated separately.
- Telemetry and discovery results without a real CID remain unclassified instead of being merged into a known cell.
- Manual Cell Lock requires a decimal or hexadecimal CID in both the Windows app and Mobile Companion.
- Legacy CID-less evidence is preserved but is not assigned to a known CID or ranked as an eligible profile.
- PCell identity inheritance remains limited to the verified transition from the same PCell to that PCell plus one or more SCells.
