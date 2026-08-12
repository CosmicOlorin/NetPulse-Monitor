# NetPulse Monitor 1.0.6

## Corrected

- MR600 outgoing SMS statuses `2` and `3` are handled as intermediate sending
  states instead of reporting a false “SMS service busy” error after the router
  has already accepted the message.
- NetPulse waits for final status `1` without an overall confirmation deadline.
  Only application shutdown cancels the wait, preventing duplicate messages.
- Every outgoing SMS attempt, final confirmation and error is recorded in the
  Events tab and `connection-events.csv` without recipient or message content.

## Project presentation and policies

- Added professional repository metadata and copyright ownership for
  CosmicOlorin.
- Added a privacy notice, private vulnerability-reporting policy, privacy-safe
  issue forms and a pull-request safety checklist.
- Documented local storage, Credential Manager use, third-party speed-test and
  public-IP services, data deletion and router safety boundaries.

## Compatibility

Settings, protected TP-Link credentials, LTE history, saved SMS contact names
and CSV files remain compatible with 1.0.5. The Windows x64 executable remains a
self-contained GUI application with no console window.
