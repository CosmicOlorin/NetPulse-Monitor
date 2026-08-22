namespace NetPulseMonitor;

internal enum RouterManagementState
{
    Disabled,
    NotConfigured,
    Connecting,
    Connected,
    SlowResponse,
    Reconnecting,
    AuthenticationRequired,
    Busy,
    Unreachable
}

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

internal sealed record RouterMobileNetworkModeOption(
    string Value,
    string DisplayName);

internal sealed class RouterMobileNetworkModeState
{
    public string Model { get; init; } = "TP-Link router";
    public string CurrentValue { get; init; } = "";
    public IReadOnlyList<RouterMobileNetworkModeOption> SupportedModes { get; init; } = [];

    public RouterMobileNetworkModeOption? CurrentMode =>
        SupportedModes.FirstOrDefault(mode =>
            mode.Value.Equals(CurrentValue, StringComparison.OrdinalIgnoreCase));
}

internal sealed class RouterTelemetry
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public bool IsConnected { get; init; }
    public string Status { get; init; } = "Not configured";
    public string ProviderName { get; init; } = "TP-Link router";
    public string Model { get; init; } = "TP-Link router";
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

internal enum RouterSmsFolder
{
    Inbox,
    Sent,
    Draft
}

internal sealed class RouterSmsMessage
{
    public required string Stack { get; init; }
    public required string Index { get; init; }
    public int PageNumber { get; init; } = 1;
    public required string Address { get; init; }
    public required string Content { get; init; }
    public required string TimeText { get; init; }
    public DateTime? Timestamp { get; init; }
    public RouterSmsFolder Folder { get; init; }
    public bool IsUnread { get; set; }
    public string Identity =>
        $"{Folder}|{Index}|{TimeText}|{Address}|{Content}";
}

internal sealed class RouterConnectedDevice
{
    public string Name { get; init; } = "Unknown device";
    public string IpAddress { get; init; } = "-";
    public string MacAddress { get; init; } = "-";
    public string ConnectionType { get; init; } = "Unknown";
    public bool IsActive { get; init; }
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

internal static class LteRadioIdentifier
{
    public static bool TryNormalizeCellId(string? value, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        string candidate = value.Trim();
        if (candidate == "-")
            return true;
        if (candidate.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            candidate = candidate[2..];
        if (candidate.Equals("FFFFFFFF", StringComparison.OrdinalIgnoreCase) ||
            candidate.Equals("FFFFFFFFFFFFFFFF", StringComparison.OrdinalIgnoreCase) ||
            candidate == "4294967295")
            return true;
        candidate = candidate.TrimStart('0');
        if (candidate.Length == 0)
            return true;
        if (candidate.Length > 16 ||
            candidate.Any(character => !char.IsAsciiHexDigit(character)))
            return false;

        normalized = candidate.ToUpperInvariant();
        return true;
    }
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

internal interface IRouterMobileNetworkModeProvider
{
    Task<RouterMobileNetworkModeState> ReadMobileNetworkModeAsync(
        CancellationToken cancellationToken);
    Task SetMobileNetworkModeAsync(
        string value,
        CancellationToken cancellationToken);
}

internal interface IRouterSmsProvider
{
    Task<IReadOnlyList<RouterSmsMessage>> ReadSmsTimelineAsync(
        CancellationToken cancellationToken);
    Task SetSmsUnreadAsync(
        string stack,
        string index,
        int pageNumber,
        bool unread,
        CancellationToken cancellationToken);
    Task DeleteSmsAsync(
        RouterSmsFolder folder,
        string stack,
        string index,
        int pageNumber,
        CancellationToken cancellationToken);
    Task SendSmsAsync(
        string phoneNumber,
        string content,
        CancellationToken cancellationToken);
    Task SaveSmsDraftAsync(
        string phoneNumber,
        string content,
        CancellationToken cancellationToken);
}

internal interface IRouterConnectedDevicesProvider
{
    Task<IReadOnlyList<RouterConnectedDevice>> ReadConnectedDevicesAsync(
        CancellationToken cancellationToken);
}

internal interface IRouterRebootProvider
{
    Task RebootRouterAsync(CancellationToken cancellationToken);
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
