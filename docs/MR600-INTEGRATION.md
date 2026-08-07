# TP-Link Archer MR600 integration plan for v1.0.2

## Scope

NetPulse Monitor v1.0.2 should add optional, read-only LTE router telemetry without changing router configuration. The first supported target is an Archer MR600 v5 running firmware `1.5.0 0.9.1 v0001.0 Build 251231 Rel.54154n`.

The local web interface was inspected in a signed-in session on 2026-08-07. No password, session token, cookie, IMEI, MAC address, IP address, DNS address, Wi-Fi name, or other identifying value was copied into this repository.

## What the router exposes

The live MR600 v5 status page exposes the following useful read-only values:

- ISP and network type
- LTE band and PCI
- SIM readiness
- signal percentage
- RSRP, RSRQ, and SNR
- cell ID and EARFCN
- monthly data usage
- current upload and download rates
- hardware and firmware versions

The local administration login is password-only. NetPulse must not display or require a username for this mode.

## Supported connection modes

### 1. Local router mode for v1.0.2

This is the supported implementation target. NetPulse connects directly to the router on the user's LAN, normally through `http://192.168.1.1` or `http://tplinkmodem.net`.

Requirements:

- user supplies only the local router address and local administration password
- password is stored with Windows credential protection, never in settings JSON, CSV, diagnostics, or logs
- one session at a time with automatic re-authentication after expiry
- strict same-origin requests and no redirects away from the configured router
- private/LAN addresses only by default
- cancellation on application shutdown and short, explicit connect/request timeouts
- serialized router requests to avoid the firmware's busy-session behavior
- configurable polling, initially 10 seconds, with exponential backoff after failures
- read-only requests only; no reboot, SMS, band lock, cell lock, PIN, network selection, firmware update, or configuration changes

### 2. Remote access

TP-Link documents remote use through a TP-Link ID and the Tether mobile app. It also documents direct WAN remote management, which exposes the web interface through a configured WAN port. NetPulse v1.0.2 should not implement either path as an unofficial cloud client or automatically enable WAN management.

A later remote feature needs one of these supported designs:

- an official TP-Link desktop/cloud API or SDK with documented authentication, or
- a user-managed VPN back to the home LAN, allowing the existing local provider to work unchanged, or
- a separate NetPulse home agent and secured relay designed specifically for remote telemetry

NetPulse must never collect a TP-Link account password until TP-Link provides an official integration method. A VPN is the recommended interim remote-access option.

## Proposed architecture

```text
Router monitoring UI
        |
        v
IRouterTelemetryProvider
        |
        +-- TpLinkMr600LocalProvider (v1.0.2)
        +-- Unsupported/diagnostic provider

TpLinkMr600LocalProvider
        +-- capability probe
        +-- password-only session
        +-- cancellable polling
        +-- normalized telemetry
        +-- secret redaction
```

Suggested interfaces:

```csharp
public interface IRouterTelemetryProvider : IAsyncDisposable
{
    Task<RouterCapabilities> ProbeAsync(Uri routerUri, CancellationToken cancellationToken);
    Task ConnectAsync(RouterConnectionOptions options, CancellationToken cancellationToken);
    Task<RouterTelemetry> ReadAsync(CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
}
```

`RouterTelemetry` should normalize firmware-specific names into nullable fields. Missing fields are a supported result, not a failure, because MR600 hardware and firmware revisions expose different capabilities.

## Protocol work required

The official MR600 v5 emulator uses TP-Link's local `/cgi` object protocol, a session token header, cookies, and firmware-dependent request encryption. The live v5 page proves the desired values exist, but implementation must not assume the emulator's login exchange is byte-for-byte identical to firmware build 251231.

Before implementing the provider:

1. Add an opt-in protocol diagnostic that records only request paths, response status, field names, timing, and firmware version.
2. Redact request bodies, response values, headers, cookies, tokens, passwords, and identifiers before data reaches logging code.
3. Capture one local login and one status refresh from the target firmware.
4. Implement the smallest firmware adapter needed for password-only login and status reads.
5. Verify logout, timeout, cancellation, session expiry, wrong-password handling, router reboot, and temporary loss of LAN connectivity.

No captured router traffic or proprietary firmware assets should be committed to the repository.

## User experience

Add a **Router** section in Settings:

- Enable TP-Link MR600 monitoring
- Router address
- Password (masked, with a Show button)
- Remember password on this PC
- Test connection
- Polling interval
- Clear saved password

The dashboard can show a separate LTE card group with signal quality, RSRP, RSRQ, SNR, band, PCI, cell/EARFCN, carrier, SIM status, monthly use, and router traffic rates. All value controls must auto-fit or elide safely so unexpected firmware strings remain inside their boxes.

## Logging and privacy

- keep LTE telemetry in a separate CSV file
- mask cell ID by default in diagnostics exports
- omit IMEI, MAC addresses, IP addresses, DNS addresses, SSIDs, and session details
- never log secrets or raw router responses
- emit events for connection loss, SIM state changes, band/cell changes, and configurable signal thresholds
- provide an explicit **Include network identifiers** opt-in for user-generated support bundles

## Acceptance criteria

- password-only local login works on the validated MR600 v5 firmware
- no router configuration is changed
- all operations have cancellation and bounded timeouts
- session expiry recovers without UI freezes
- wrong credentials produce a clear local error without logging the password
- all LTE values remain inside their cards at 100%, 125%, 150%, 175%, and 200% Windows scaling
- the feature degrades cleanly when a field is absent on another MR600 revision
- secret scans and packet/log review show no password, token, cookie, or personal network identifier

## Official references

- [Archer MR600 V1 administration guide](https://www.tp-link.com/us/user-guides/archer-mr600_v1/chapter-12-administrate-your-network)
- [Archer MR600 V1 login FAQ](https://www.tp-link.com/us/user-guides/archer-mr600_v1/faq)
- [TP-Link remote-management guidance](https://www.tp-link.com/us/support/faq/1553/)
- [TP-Link Tether remote-management guidance](https://www.tp-link.com/us/support/faq/1971/)
- [Official Archer MR600 v5 emulator](https://emulator.tp-link.com/MR600v5%20emulator/index.htm)

