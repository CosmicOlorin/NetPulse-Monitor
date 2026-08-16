# NetPulse Mobile Companion

The mobile client uses the desktop application as the single owner of the TP-Link management session.

The first implementation is deliberately read-only. `NetPulse.Companion.Core` already provides:

- persistent `netpulse://pair` profile parsing;
- signed requests with timestamp and nonce replay protection;
- AES-GCM telemetry decryption;
- strongly typed router, LTE and Internet state.

The pairing remains valid until the desktop user selects **Revoke all and regenerate**. It does not expire automatically.

`NetPulse.Companion.App` is the Android 6.0+ UI. Open **Settings → Mobile companion** on Windows. The **DOWNLOAD ANDROID APP** QR downloads the APK directly from that PC over the same Wi-Fi/LAN—no GitHub or external hosting. After installation, scan the separate **PAIR THIS PHONE** QR. Pairing is stored in Android secure storage; **Forget this PC** removes it locally.

The dashboard refreshes once per second and shows Internet state, independent router/LTE state, ping, jitter, packet loss, availability, outages, LTE profile, EARFCN, PCI, CID, signal values and unread-SMS count. Router credentials are never included in the pairing profile or sent to the phone.

Build with .NET 8 MAUI Android:

```powershell
dotnet workload install maui-android
dotnet build .\NetPulse.Companion.App\NetPulse.Companion.App.csproj -c Release
```
