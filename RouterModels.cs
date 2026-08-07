namespace NetPulseMonitor;

internal sealed class RouterConnectionOptions
{
    public required Uri RouterUri { get; init; }
    public required string Password { get; init; }
    public bool AllowSessionTakeover { get; init; }
}

internal sealed class RouterCapabilities
{
    public string Model { get; init; } = "TP-Link router";
    public string HardwareVersion { get; init; } = "Unknown";
    public string FirmwareVersion { get; init; } = "Unknown";
    public bool SupportsLteTelemetry { get; init; }
}

internal sealed class RouterTelemetry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public bool IsConnected { get; init; }
    public string Status { get; init; } = "Not configured";
    public string ProviderName { get; init; } = "TP-Link MR600";
    public string Isp { get; init; } = "-";
    public string NetworkType { get; init; } = "-";
    public string Band { get; init; } = "-";
    public string PrimaryBand { get; init; } = "-";
    public string SimStatus { get; init; } = "-";
    public int? SignalPercent { get; init; }
    public double? RsrpDbm { get; init; }
    public double? RsrqDb { get; init; }
    public double? SnrDb { get; init; }
    public double? RssiDbm { get; init; }
    public string Pci { get; init; } = "-";
    public string CellId { get; init; } = "-";
    public string Earfcn { get; init; } = "-";
    public int? UnreadSmsCount { get; init; }
    public long? TotalBytes { get; init; }
    public long? UploadBytesPerSecond { get; init; }
    public long? DownloadBytesPerSecond { get; init; }
    public string HardwareVersion { get; init; } = "Unknown";
    public string FirmwareVersion { get; init; } = "Unknown";
    public string? Error { get; init; }
}

internal sealed class RouterSmsMessage
{
    public required string Stack { get; init; }
    public required string Index { get; init; }
    public required string From { get; init; }
    public required string Content { get; init; }
    public required string ReceivedTime { get; init; }
    public bool IsUnread { get; set; }
}

internal sealed class RouterCellLockTarget
{
    public required IReadOnlyList<int> Bands { get; init; }
    public required string Earfcn { get; init; }
    public required string Pci { get; init; }
    public string? CellId { get; init; }
    public bool HasCellTarget =>
        !string.IsNullOrWhiteSpace(Earfcn) &&
        !string.IsNullOrWhiteSpace(Pci) &&
        Earfcn != "-" && Pci != "-";
}

internal sealed class RouterLockState
{
    public bool BandSelectionEnabled { get; init; }
    public int BandMaskLow { get; init; }
    public int BandMaskHigh { get; init; }
    public bool CellLockEnabled { get; init; }
    public string? CellId { get; init; }
    public string Earfcn { get; init; } = "";
    public string Pci { get; init; } = "";
}

internal interface IRouterTelemetryProvider : IAsyncDisposable
{
    bool IsConnected { get; }
    Task<RouterCapabilities> ConnectAsync(
        RouterConnectionOptions options,
        CancellationToken cancellationToken);
    Task<RouterTelemetry> ReadAsync(CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
}

internal interface IRouterCellLockProvider
{
    Task<RouterLockState> ReadLockStateAsync(CancellationToken cancellationToken);
    Task ApplyCellAndBandLockAsync(
        RouterCellLockTarget target,
        CancellationToken cancellationToken);
    Task RestoreLockStateAsync(
        RouterLockState state,
        CancellationToken cancellationToken);
    Task RestoreAutomaticSelectionAsync(CancellationToken cancellationToken);
}

internal interface IRouterSmsProvider
{
    Task<IReadOnlyList<RouterSmsMessage>> ReadSmsInboxAsync(
        CancellationToken cancellationToken);
    Task MarkSmsReadAsync(
        string stack,
        CancellationToken cancellationToken);
    Task SendSmsAsync(
        string phoneNumber,
        string content,
        CancellationToken cancellationToken);
}

internal class RouterConnectionException : Exception
{
    public RouterConnectionException(string message) : base(message) { }
    public RouterConnectionException(string message, Exception innerException)
        : base(message, innerException) { }
}

internal sealed class RouterAuthenticationException : RouterConnectionException
{
    public RouterAuthenticationException(string message) : base(message) { }
}

internal sealed class RouterBusyException : RouterConnectionException
{
    public RouterBusyException(string message) : base(message) { }
}
