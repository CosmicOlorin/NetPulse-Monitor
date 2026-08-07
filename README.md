# NetPulse Monitor

NetPulse Monitor is a native .NET 8 WinForms application for continuous Windows
network monitoring. It has no console window during normal use.

## Features

- Continuous asynchronous ICMP monitoring with live latency chart
- Current ping, jitter, rolling packet loss, successes and failures
- Confirmed outage detection, recovery events, uptime, downtime and availability
- Persistent connection-event and speed-test CSV logs
- Manual and scheduled 20 MB download / 5 MB upload speed tests
- Provider-based download fallback with independent timeout and cancellation
- Gateway, DNS, IPv4 and IPv6 diagnostics
- System tray, optional Windows startup and editable monitoring settings

Application data is stored in `%LOCALAPPDATA%\NetPulseMonitor`. CSV logs are
stored in `%USERPROFILE%\Documents\NetPulse-Monitor`.

## Requirements

- Windows 10 or Windows 11, x64
- Visual Studio 2022 with the **.NET desktop development** workload, or the
  .NET 8 SDK

The published release is self-contained and does not require .NET to be
installed on the target PC.

## Build and run

From PowerShell in the repository root:

```powershell
dotnet restore .\NetPulseMonitor.sln
dotnet build .\NetPulseMonitor.sln -c Release
dotnet run --project .\NetPulseMonitor.csproj
```

To create the single-file, self-contained Windows release:

```powershell
.\build-release.ps1
```

The executable is written to
`artifacts\publish\win-x64\NetPulseMonitor.exe`.

## Speed-test behavior

Download testing uses a provider abstraction and tries Cloudflare, OVH, then
Hetzner until a measurement succeeds. The stream stops after the configured
sample size, so the default download transfer is 20 MB. Upload capability is
selected independently; currently Cloudflare is the built-in upload provider
and the default payload is 5 MB. Each connection and transfer stage has a
bounded timeout (75 seconds per download provider, 45 seconds for upload, and
180 seconds overall), and the UI can cancel the entire operation immediately.

These measurements are useful operational estimates, not certified line-rate
benchmarks. Results vary with provider location, routing, Wi-Fi and server load.

## Repository layout

- `MainForm.cs` — WinForms UI, settings, tray and scheduling
- `MonitorEngine.cs` — continuous ping and outage state
- `SpeedTestEngine.cs` — provider selection and measurements
- `NetworkDiagnostics.cs` — gateway, DNS and IP diagnostics
- `CsvLogger.cs` — durable CSV output
- `PingChartControl.cs` — dependency-free live chart
