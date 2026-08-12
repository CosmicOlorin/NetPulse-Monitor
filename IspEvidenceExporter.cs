using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace NetPulseMonitor;

/// <summary>
/// Produces a technical support bundle that can be attached to an ISP ticket.
/// Network addresses and serving-cell identifiers are intentionally included;
/// credentials, authentication material and SMS data are never included.
/// </summary>
internal static class IspEvidenceExporter
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public static string Export(
        CsvLogger logger,
        string accessTechnology,
        MonitorSnapshot monitor,
        RouterTelemetry router,
        DiagnosticResult? diagnostics,
        string? publicIpAddress,
        IReadOnlyList<LteCellRecommendation> lteHistory,
        OfficialClock clock)
    {
        string exportFolder = Path.Combine(logger.LogFolder, "ISP-Evidence");
        Directory.CreateDirectory(exportFolder);
        string timestamp = clock.Now.ToString(
            "yyyyMMdd-HHmmss",
            CultureInfo.InvariantCulture);
        string destination = Path.Combine(
            exportFolder,
            $"NetPulse-ISP-Evidence-{timestamp}.zip");
        string staging = Path.Combine(
            Path.GetTempPath(),
            "NetPulse-ISP-Evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        try
        {
            File.WriteAllText(
                Path.Combine(staging, "ISP-Evidence-Summary.txt"),
                BuildSummary(
                    accessTechnology,
                    monitor,
                    router,
                    diagnostics,
                    publicIpAddress,
                    clock),
                Utf8);
            File.WriteAllText(
                Path.Combine(staging, "EVIDENCE-CONTENTS.txt"),
                BuildEvidenceContentsNotice(),
                Utf8);

            DateTime cutoffUtc = DateTime.UtcNow.AddDays(-30);
            WriteFilteredCsv(
                logger.EventsPath,
                Path.Combine(staging, "connection-events.csv"),
                cutoffUtc,
                row => row.Length >= 4 && SafeEventKind(row[1]),
                row => [row[0], row[1], row[2], row[3]],
                ["Timestamp", "Kind", "Message", "DurationSeconds"]);
            WriteFilteredCsv(
                logger.SpeedTestsPath,
                Path.Combine(staging, "speed-tests.csv"),
                cutoffUtc,
                _ => true,
                row => row,
                [
                    "Timestamp", "LatencyMs", "JitterMs", "PacketLossPercent",
                    "DownloadMbps", "UploadMbps", "Warning"
                ]);

            if (IsLte(accessTechnology))
            {
                WriteFilteredCsv(
                    logger.RouterTelemetryPath,
                    Path.Combine(staging, "lte-router-telemetry.csv"),
                    cutoffUtc,
                    _ => true,
                    row => row,
                    [
                        "Timestamp", "Status", "ISP", "NetworkType", "Band",
                        "SignalPercent", "RSRPdBm", "RSRQdB", "SNRdB", "PCI",
                        "CellId", "EARFCN", "TotalBytes",
                        "UploadBytesPerSecond", "DownloadBytesPerSecond"
                    ]);
                WriteLteHistoryCsv(
                    Path.Combine(staging, "lte-cell-history-summary.csv"),
                    lteHistory,
                    clock);
            }

            if (File.Exists(destination))
                File.Delete(destination);
            ZipFile.CreateFromDirectory(
                staging,
                destination,
                CompressionLevel.Optimal,
                includeBaseDirectory: false);
            return destination;
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
            catch
            {
                // The completed ZIP remains valid if Windows temporarily holds
                // a staging file. No credentials or SMS data exist in staging.
            }
        }
    }

    private static string BuildSummary(
        string accessTechnology,
        MonitorSnapshot monitor,
        RouterTelemetry router,
        DiagnosticResult? diagnostics,
        string? publicIpAddress,
        OfficialClock clock)
    {
        CultureInfo culture = clock.Culture;
        var lines = new List<string>
        {
            "NETPULSE MONITOR — FULL TECHNICAL ISP EVIDENCE",
            $"Generated: {clock.FormatReport(DateTime.UtcNow)}",
            $"Country: {clock.CountryName} ({clock.CountryCode})",
            $"Official time zone: {clock.TimeZone.DisplayName} [{clock.TimeZone.Id}]",
            "Observation window in CSV files: latest 30 days",
            "",
            "CONNECTION PROFILE",
            $"Access technology: {accessTechnology}",
            "",
            "CURRENT MONITORING SESSION",
            $"Status: {(monitor.IsOnline ? "Online" : "Offline")}",
            $"Run time: {FormatDuration(monitor.RunTime)}",
            $"Availability: {monitor.AvailabilityPercent.ToString("0.###", culture)}%",
            $"Outages: {monitor.Outages.ToString(culture)}",
            $"Total downtime: {FormatDuration(monitor.TotalDowntime)}",
            $"Current ping: {FormatNullable(monitor.CurrentPingMs, " ms", culture)}",
            $"Jitter: {monitor.JitterMs.ToString("0.#", culture)} ms",
            $"Packet loss: {monitor.PacketLossPercent.ToString("0.#", culture)}%"
        };

        if (IsLte(accessTechnology))
        {
            lines.AddRange(
            [
                "",
                "CURRENT LTE RADIO AND SERVING-CELL EVIDENCE",
                $"Router status: {router.Status}",
                $"ISP: {Value(router.Isp)}",
                $"Network type: {Value(router.NetworkType)}",
                $"Band profile (PCell first): {Value(router.Band)}",
                $"Primary serving band (PCell): {Value(router.PrimaryBand)}",
                $"Primary EARFCN: {Value(router.Earfcn)}",
                $"PCI: {Value(router.Pci)}",
                $"CID / Cell ID: {Value(router.CellId)}",
                $"Signal: {FormatNullable(router.SignalPercent, "%", culture)}",
                $"RSRP: {FormatNullable(router.RsrpDbm, " dBm", culture)}",
                $"RSRQ: {FormatNullable(router.RsrqDb, " dB", culture)}",
                $"SNR: {FormatNullable(router.SnrDb, " dB", culture)}",
                $"Hardware: {Value(router.HardwareVersion)}",
                $"Firmware: {Value(router.FirmwareVersion)}"
            ]);
        }

        if (diagnostics is not null)
        {
            lines.AddRange(
            [
                "",
                "IP AND LOCAL-PATH DIAGNOSTICS",
                $"Public IP: {Value(publicIpAddress)}",
                $"Default gateway: {Value(diagnostics.Gateway)}",
                $"Gateway latency: {Value(diagnostics.GatewayPing)}",
                $"DNS lookup latency: {Value(diagnostics.DnsLookup)}",
                $"Local IPv4: {FormatAddresses(diagnostics.IPv4Addresses, diagnostics.IPv4)}",
                $"Local IPv6: {FormatAddresses(diagnostics.IPv6Addresses, diagnostics.IPv6)}"
            ]);
        }
        else
        {
            lines.AddRange(
            [
                "",
                "IP EVIDENCE",
                $"Public IP: {Value(publicIpAddress)}",
                "Local addressing: Run diagnostics before export to include it"
            ]);
        }

        lines.AddRange(
        [
            "",
            "See EVIDENCE-CONTENTS.txt for the exact inclusion/exclusion policy."
        ]);
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string BuildEvidenceContentsNotice() => string.Join(
        Environment.NewLine,
        "ISP EVIDENCE CONTENTS",
        "",
        "This support bundle intentionally includes technical identifiers needed by an ISP:",
        "- public IP, local IPv4/IPv6 addresses, and the numeric default gateway",
        "- full LTE band profile, PCell, EARFCN, PCI, CID/Cell ID, and signal measurements",
        "- connection events, outage durations, speed tests, router telemetry, and LTE history summary",
        "",
        "It intentionally excludes:",
        "- TP-Link passwords, authentication tokens, and router address",
        "- application settings and protected Windows credentials",
        "- SMS inbox, sent messages, drafts, recipients, phone numbers, and contacts",
        "- screenshots and clipboard contents",
        "",
        "LTE telemetry is included only for the Mobile LTE profile.",
        "Review the ZIP before sending it to any third party.",
        "");

    private static void WriteFilteredCsv(
        string source,
        string destination,
        DateTime cutoffUtc,
        Func<string[], bool> include,
        Func<string[], string[]> project,
        string[] header)
    {
        using var writer = new StreamWriter(destination, false, Utf8);
        WriteCsvRow(writer, header);
        if (!File.Exists(source))
            return;

        using var parser = new TextFieldParser(source, Utf8)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true
        };
        parser.SetDelimiters(",");
        if (!parser.EndOfData)
            _ = parser.ReadFields();
        while (!parser.EndOfData)
        {
            string[]? row;
            try
            {
                row = parser.ReadFields();
            }
            catch (MalformedLineException)
            {
                continue;
            }
            if (row is null || row.Length == 0 ||
                !TryReadTimestampUtc(row[0], out DateTime timestampUtc) ||
                timestampUtc < cutoffUtc || !include(row))
                continue;
            WriteCsvRow(writer, project(row));
        }
    }

    private static void WriteLteHistoryCsv(
        string destination,
        IReadOnlyList<LteCellRecommendation> history,
        OfficialClock clock)
    {
        using var writer = new StreamWriter(destination, false, Utf8);
        WriteCsvRow(writer,
        [
            "Period", "BandProfilePCellFirst", "PrimaryBand", "EARFCN", "PCI",
            "CellId", "FirstSeen", "LastSeen", "ConnectedSeconds", "Sessions",
            "Handoffs", "Disconnections", "DisconnectionsPerHour", "AveragePingMs",
            "EstimatedCellLoadPercent", "AverageDownloadMbps", "AverageUploadMbps",
            "SpeedTests", "WeightedScore", "Confidence"
        ]);

        foreach (LteCellRecommendation item in history
                     .Where(item => item.ConnectedTime > TimeSpan.Zero)
                     .OrderBy(item => item.PeriodId)
                     .ThenByDescending(item => item.LastSeenUtc))
        {
            WriteCsvRow(writer,
            [
                item.TimePeriod,
                item.Band,
                item.PrimaryBand,
                item.Earfcn,
                item.Pci,
                item.CellId ?? "",
                clock.FormatCsv(item.FirstSeenUtc),
                clock.FormatCsv(item.LastSeenUtc),
                item.ConnectedTime.TotalSeconds.ToString("0", CultureInfo.InvariantCulture),
                item.Sessions.ToString(CultureInfo.InvariantCulture),
                item.Handoffs.ToString(CultureInfo.InvariantCulture),
                item.Disconnections.ToString(CultureInfo.InvariantCulture),
                item.DisconnectionsPerHour.ToString("0.###", CultureInfo.InvariantCulture),
                FormatInvariant(item.AveragePingMs),
                FormatInvariant(item.EstimatedCellLoadPercent),
                FormatInvariant(item.AverageDownloadMbps),
                FormatInvariant(item.AverageUploadMbps),
                item.SpeedTests.ToString(CultureInfo.InvariantCulture),
                item.WeightedScore.ToString("0.###", CultureInfo.InvariantCulture),
                item.Confidence
            ]);
        }
    }

    private static bool TryReadTimestampUtc(string value, out DateTime timestampUtc)
    {
        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out DateTimeOffset timestamp))
        {
            timestampUtc = timestamp.UtcDateTime;
            return true;
        }
        timestampUtc = default;
        return false;
    }

    private static void WriteCsvRow(TextWriter writer, IEnumerable<string> values) =>
        writer.WriteLine(string.Join(",", values.Select(value =>
            "\"" + (value ?? "").Replace("\"", "\"\"") + "\"")));

    private static bool SafeEventKind(string value) => value is
        "ONLINE" or "OFFLINE" or "SPEED" or "ERROR" or "ROUTER" or "CELL LOCK";

    private static bool IsLte(string accessTechnology) =>
        accessTechnology.Contains("LTE", StringComparison.OrdinalIgnoreCase);

    private static string Value(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim() == "-"
            ? "Not measured"
            : value.Trim();

    private static string FormatInvariant(double? value) =>
        value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "";

    private static string FormatAddresses(
        IReadOnlyList<string> addresses,
        string availability) =>
        addresses.Count > 0 ? string.Join(", ", addresses) : Value(availability);

    private static string FormatNullable<T>(
        T? value,
        string suffix,
        CultureInfo culture) where T : struct =>
        value.HasValue
            ? Convert.ToString(value.Value, culture) + suffix
            : "Not measured";

    private static string FormatDuration(TimeSpan duration) =>
        $"{(int)duration.TotalDays}d {duration.Hours:00}h " +
        $"{duration.Minutes:00}m {duration.Seconds:00}s";
}
