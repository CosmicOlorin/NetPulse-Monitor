using System.Globalization;
using System.Text;

namespace NetPulseMonitor;

internal sealed class CsvLogger
{
    private readonly object _gate = new();
    private OfficialClock _clock;

    public string LogFolder { get; }
    public string EventsPath { get; }
    public string SpeedTestsPath { get; }
    public string RouterTelemetryPath { get; }
    public string BandDiscoveryPath { get; }

    public CsvLogger(OfficialClock clock)
    {
        _clock = clock;
        LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NetPulse-Monitor");

        EventsPath = Path.Combine(LogFolder, "connection-events.csv");
        SpeedTestsPath = Path.Combine(LogFolder, "speed-tests.csv");
        RouterTelemetryPath = Path.Combine(LogFolder, "router-telemetry.csv");
        BandDiscoveryPath = Path.Combine(LogFolder, "band-cell-discovery.csv");

        Directory.CreateDirectory(LogFolder);
        EnsureFile(EventsPath, "Timestamp,Kind,Message,DurationSeconds");
        EnsureFile(SpeedTestsPath,
            "Timestamp,LatencyMs,JitterMs,PacketLossPercent,DownloadMbps,UploadMbps,Warning");
        EnsureRouterTelemetryFile();
        EnsureFile(BandDiscoveryPath,
            "Timestamp,ScanId,RouterModel,HardwareVersion,RequestedBand,ServingProfile,PrimaryBand,EARFCN,PCI,CellId,RSRPdBm,RSRQdB,SNRdB,Samples,Status");
    }

    public void SetOfficialClock(OfficialClock clock)
    {
        lock (_gate)
            _clock = clock;
    }

    private void EnsureFile(string path, string header)
    {
        if (!File.Exists(path))
            File.WriteAllText(path, header + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private void EnsureRouterTelemetryFile()
    {
        const string header =
            "Timestamp,Status,ISP,NetworkType,Band,SignalPercent,RSRPdBm,RSRQdB,SNRdB,PCI,CellId,EARFCN,TotalBytes,UploadBytesPerSecond,DownloadBytesPerSecond";
        if (!File.Exists(RouterTelemetryPath))
        {
            EnsureFile(RouterTelemetryPath, header);
            return;
        }

        string firstLine;
        using (var reader = new StreamReader(RouterTelemetryPath, Encoding.UTF8,
                   detectEncodingFromByteOrderMarks: true))
            firstLine = reader.ReadLine() ?? "";
        if (!firstLine.Contains("CellIdMasked", StringComparison.Ordinal))
            return;

        string temporary = RouterTelemetryPath + ".header-update";
        using (var reader = new StreamReader(RouterTelemetryPath, Encoding.UTF8,
                   detectEncodingFromByteOrderMarks: true))
        using (var writer = new StreamWriter(temporary, false,
                   new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
        {
            _ = reader.ReadLine();
            writer.WriteLine(firstLine.Replace(
                "CellIdMasked",
                "CellId",
                StringComparison.Ordinal));
            while (reader.ReadLine() is { } line)
                writer.WriteLine(line);
        }
        File.Move(temporary, RouterTelemetryPath, overwrite: true);
    }

    public void LogEvent(MonitorEvent evt, double? durationSeconds = null)
    {
        Append(EventsPath,
            Csv(evt.Timestamp, evt.Kind, evt.Message,
                durationSeconds?.ToString("0.0", CultureInfo.InvariantCulture) ?? ""));
    }

    public void LogSpeedTest(SpeedTestResult result)
    {
        Append(SpeedTestsPath,
            Csv(DateTime.UtcNow,
                result.LatencyMs.ToString("0.0", CultureInfo.InvariantCulture),
                result.JitterMs.ToString("0.0", CultureInfo.InvariantCulture),
                result.PacketLossPercent.ToString("0.0", CultureInfo.InvariantCulture),
                result.DownloadMbps?.ToString("0.00", CultureInfo.InvariantCulture) ?? "",
                result.UploadMbps?.ToString("0.00", CultureInfo.InvariantCulture) ?? "",
                result.Warning ?? ""));
    }

    public void LogRouterTelemetry(RouterTelemetry telemetry)
    {
        Append(RouterTelemetryPath,
            Csv(telemetry.Timestamp,
                telemetry.Status,
                telemetry.Isp,
                telemetry.NetworkType,
                telemetry.Band,
                telemetry.SignalPercent,
                telemetry.RsrpDbm,
                telemetry.RsrqDb,
                telemetry.SnrDb,
                telemetry.Pci,
                string.IsNullOrWhiteSpace(telemetry.CellId) || telemetry.CellId == "-"
                    ? ""
                    : telemetry.CellId.Trim(),
                telemetry.Earfcn,
                telemetry.TotalBytes,
                telemetry.UploadBytesPerSecond,
                telemetry.DownloadBytesPerSecond));
    }

    public void LogBandDiscovery(
        string scanId,
        RouterTelemetry router,
        LteBandCellObservation observation)
    {
        Append(BandDiscoveryPath,
            Csv(observation.LastSeen,
                scanId,
                router.Model,
                router.HardwareVersion,
                "B" + observation.RequestedBand.ToString(
                    CultureInfo.InvariantCulture),
                observation.ServingProfile,
                observation.PrimaryBand,
                observation.Earfcn == "-" ? "" : observation.Earfcn,
                observation.Pci == "-" ? "" : observation.Pci,
                observation.CellId == "-" ? "" : observation.CellId,
                observation.RsrpDbm,
                observation.RsrqDb,
                observation.SnrDb,
                observation.Samples,
                observation.Status));
    }

    private void Append(string path, string row)
    {
        lock (_gate)
            File.AppendAllText(path, row + Environment.NewLine, Encoding.UTF8);
    }

    private string Csv(params object?[] values)
    {
        return string.Join(",", values.Select(value =>
        {
            string text = value switch
            {
                DateTime dt => _clock.FormatCsv(dt),
                null => "",
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
            };
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }));
    }

}
