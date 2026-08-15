# NetPulse Mobile Companion

The mobile client uses the desktop application as the single owner of the TP-Link management session.

The first implementation is deliberately read-only. `NetPulse.Companion.Core` already provides:

- persistent `netpulse://pair` profile parsing;
- signed requests with timestamp and nonce replay protection;
- AES-GCM telemetry decryption;
- strongly typed router, LTE and Internet state.

The pairing remains valid until the desktop user selects **Revoke all and regenerate**. It does not expire automatically.

An Android UI project will consume this library after the Android/MAUI workload is installed. Router credentials are never included in the pairing profile or sent to the phone.
