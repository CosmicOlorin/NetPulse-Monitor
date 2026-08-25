namespace NetPulseMonitor;

internal sealed class MonitorSnapshot
{
    public DateTime StartedAt { get; init; }
    public bool IsOnline { get; init; }
    public bool IsPaused { get; init; }
    public long? CurrentPingMs { get; init; }
    public double? AveragePingMs { get; init; }
    public double JitterMs { get; init; }
    public double SessionAverageJitterMs { get; init; }
    public double PacketLossPercent { get; init; }
    public double SessionPacketLossPercent { get; init; }
    public long SuccessfulPings { get; init; }
    public long FailedPings { get; init; }
    public int Outages { get; init; }
    public TimeSpan RunTime { get; init; }
    public TimeSpan TotalDowntime { get; init; }
    public double AvailabilityPercent { get; init; }
}

internal sealed class MonitorEvent
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string Kind { get; init; } = "INFO";
    public string Message { get; init; } = "";
}

internal sealed class SpeedTestResult
{
    public string Provider { get; init; } = "Unknown";
    public double LatencyMs { get; init; }
    public double JitterMs { get; init; }
    public double PacketLossPercent { get; init; }
    public double? DownloadMbps { get; init; }
    public double? UploadMbps { get; init; }
    public string? Warning { get; init; }
}

internal sealed class DiagnosticResult
{
    public string Gateway { get; init; } = "Not detected";
    public string GatewayPing { get; init; } = "N/A";
    public string DnsLookup { get; init; } = "N/A";
    public string IPv4 { get; init; } = "Unknown";
    public string IPv6 { get; init; } = "Unknown";
    public IReadOnlyList<string> IPv4Addresses { get; init; } = [];
    public IReadOnlyList<string> IPv6Addresses { get; init; } = [];
}
