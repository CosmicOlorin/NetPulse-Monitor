using System.Globalization;
using System.Text;

namespace NetPulseMonitor;

internal sealed class CsvLogger
{
    private readonly object _gate = new();

    public string LogFolder { get; }
    public string EventsPath { get; }
    public string SpeedTestsPath { get; }
    public string RouterTelemetryPath { get; }

    public CsvLogger()
    {
        LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NetPulse-Monitor");

        EventsPath = Path.Combine(LogFolder, "connection-events.csv");
        SpeedTestsPath = Path.Combine(LogFolder, "speed-tests.csv");
        RouterTelemetryPath = Path.Combine(LogFolder, "router-telemetry.csv");

        Directory.CreateDirectory(LogFolder);
        EnsureFile(EventsPath, "Timestamp,Kind,Message,DurationSeconds");
        EnsureFile(SpeedTestsPath,
            "Timestamp,LatencyMs,JitterMs,PacketLossPercent,DownloadMbps,UploadMbps,Warning");
        EnsureFile(RouterTelemetryPath,
            "Timestamp,Status,ISP,NetworkType,Band,SignalPercent,RSRPdBm,RSRQdB,SNRdB,PCI,CellIdMasked,EARFCN,TotalBytes,UploadBytesPerSecond,DownloadBytesPerSecond");
    }

    private void EnsureFile(string path, string header)
    {
        if (!File.Exists(path))
            File.WriteAllText(path, header + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
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
            Csv(DateTime.Now,
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
                MaskIdentifier(telemetry.CellId),
                telemetry.Earfcn,
                telemetry.TotalBytes,
                telemetry.UploadBytesPerSecond,
                telemetry.DownloadBytesPerSecond));
    }

    private void Append(string path, string row)
    {
        lock (_gate)
            File.AppendAllText(path, row + Environment.NewLine, Encoding.UTF8);
    }

    private static string Csv(params object?[] values)
    {
        return string.Join(",", values.Select(value =>
        {
            string text = value switch
            {
                DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
                null => "",
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
            };
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }));
    }

    private static string MaskIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "-")
            return "";
        string trimmed = value.Trim();
        return trimmed.Length <= 4
            ? new string('•', trimmed.Length)
            : new string('•', trimmed.Length - 4) + trimmed[^4..];
    }
}
