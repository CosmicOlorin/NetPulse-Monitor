# NetPulse Monitor 1.0.3

## Changed

- LTE History is grouped by local-time period instead of band or PCell.
- Night, Morning, Afternoon and Evening are independent collapsible groups.
- Band combinations and PCell identities remain normal sortable rows inside
  each period.
- Recommendation ranking now uses a visible weighted score: 50% download,
  40% confirmed disconnections per connected hour and 10% upload.
- Download and upload are normalized against the fastest eligible result in the
  same period.
- Reliability uses `100 / (1 + confirmed drops per connected hour)`, penalizing
  repeated drops without making one zero-drop sample an absolute veto over much
  stronger speed evidence.
- Adaptive automatic Cell Lock follows the same weighted score shown by LTE
  History and retains a five-point switch margin to prevent rapid oscillation.

## Compatibility

- Existing LTE history is preserved. Measurements appear under every time
  period in which they were collected.
- Existing manual profiles without period evidence remain visible in the
  current local-time group until measurements are collected.
- TP-Link MR600 credentials, settings, SMS data handling and router protocol
  behavior are unchanged from 1.0.2.
