# NetPulse Monitor 1.0.18

## Android Companion updater reliability

- Fixed the Android Companion update failure that reported a cached APK was in
  use by another process.
- Downloads now use a unique staging file that is closed before SHA-256
  verification.
- If Android still holds an older cached package open, NetPulse completes the
  update with a new package name instead of failing or overwriting the file in
  use.
- Added a regression test covering an update while the previous APK is locked.
- Closed the three CodeQL authentication-bypass findings: Companion metadata
  and in-app updates now require the paired HMAC identity, while the initial QR
  download uses a separate constant-time-validated token instead of exposing
  the pairing secret.

This release otherwise preserves the desktop and Companion behavior introduced
in 1.0.17.
