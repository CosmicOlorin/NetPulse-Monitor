# NetPulse Monitor 1.0.9

## Mobile companion

- Adds SMS timeline, mark-read, delete, and send controls.
- Adds LTE history, manual band/cell locking, and restore-automatic controls.
- Adds one notification per newly observed unread SMS while the companion is active.
- Adds persistent in-app update checks through the paired desktop computer; pairing is retained.
- Serializes router operations so SMS and LTE actions do not compete with each other.
- Extends slow router-operation timeouts and displays the router's actual error message.

## TP-Link reliability

- Fixes SMS timeline loading failures caused by transient HTTP 500 responses.
- Adds bounded retry and backoff for HTTP 500, 503, and 429 responses.
- Uses a dedicated 25-second timeout for TP-Link SMS requests.
- Adds small pacing delays while reading multi-page Inbox, Sent, and Draft folders.

No router credentials, SMS contents, LTE history, or other user data are included in the release artifacts.
