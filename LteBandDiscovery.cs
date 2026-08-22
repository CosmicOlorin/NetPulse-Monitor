using System.Globalization;
using System.Text.RegularExpressions;

namespace NetPulseMonitor;

internal sealed record LteBandScanPlan(
    IReadOnlyList<int> Bands,
    string RouterProfile,
    string Source,
    bool IsComplete);

internal sealed record LteBandCellObservation(
    int RequestedBand,
    string ServingProfile,
    string PrimaryBand,
    string Earfcn,
    string Pci,
    string CellId,
    double? RsrpDbm,
    double? RsrqDb,
    double? SnrDb,
    DateTime FirstSeen,
    DateTime LastSeen,
    int Samples,
    string Status)
{
    public string IdentityKey => string.Join("|",
        RequestedBand.ToString(CultureInfo.InvariantCulture),
        ServingProfile,
        PrimaryBand,
        Earfcn,
        Pci,
        CellId);

    public bool HasCompleteIdentity =>
        Earfcn != "-" && Pci != "-" && CellId != "-";

    public LteBandCellObservation Merge(LteBandCellObservation sample) => this with
    {
        RsrpDbm = sample.RsrpDbm ?? RsrpDbm,
        RsrqDb = sample.RsrqDb ?? RsrqDb,
        SnrDb = sample.SnrDb ?? SnrDb,
        LastSeen = sample.LastSeen,
        Samples = Samples + 1,
        Status = sample.Status
    };

    public static LteBandCellObservation NotObserved(
        int requestedBand,
        string status = "No serving cell observed") => new(
        requestedBand,
        "-",
        "-",
        "-",
        "-",
        "-",
        null,
        null,
        null,
        DateTime.Now,
        DateTime.Now,
        0,
        status);
}

internal static class LteBandDiscovery
{
    // Archer MR600(EU) V5 official radio specification:
    // LTE-FDD B1/B3/B5/B7/B8/B20/B28 and LTE-TDD B38/B40/B41.
    private static readonly int[] Mr600EuV5Bands =
        [1, 3, 5, 7, 8, 20, 28, 38, 40, 41];

    private static readonly HashSet<string> EuropeanCountryCodes = new(
        [
            "AL", "AD", "AT", "BE", "BA", "BG", "HR", "CY", "CZ", "DK",
            "EE", "FI", "FR", "DE", "GR", "HU", "IS", "IE", "IT", "XK",
            "LV", "LI", "LT", "LU", "MT", "MD", "MC", "ME", "NL", "MK",
            "NO", "PL", "PT", "RO", "SM", "RS", "SK", "SI", "ES", "SE",
            "CH", "TR", "UA", "GB", "VA"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static LteBandScanPlan CreatePlan(
        RouterTelemetry telemetry,
        string? countryCode,
        IEnumerable<string?> observedProfiles)
    {
        string identity = string.Join(" ",
            telemetry.Model,
            telemetry.HardwareVersion,
            telemetry.FirmwareVersion);
        bool isMr600 = identity.Contains("MR600", StringComparison.OrdinalIgnoreCase);
        bool isV5 = Regex.IsMatch(
            identity,
            @"(?:\bV\s*5(?:\.0)?\b|\bMR600[^\r\n]{0,35}\b5\.0\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        bool isEuropean = EuropeanCountryCodes.Contains(countryCode ?? "");

        if (isMr600 && isV5 && isEuropean)
        {
            return new LteBandScanPlan(
                Mr600EuV5Bands,
                "TP-Link Archer MR600(EU) V5",
                "verified model and regional radio specification",
                true);
        }

        int[] observed = observedProfiles
            .Append(telemetry.Band)
            .SelectMany(ExtractBands)
            .Where(band => band is >= 1 and <= 64)
            .Distinct()
            .Order()
            .ToArray();
        string routerName = Clean(telemetry.Model) is { Length: > 0 } model
            ? model
            : "TP-Link LTE router";
        return new LteBandScanPlan(
            observed,
            routerName,
            observed.Length == 0
                ? "no verified radio profile or observed LTE bands"
                : "bands already observed by this router",
            false);
    }

    public static IReadOnlyList<int> ExtractBands(string? profile) =>
        Regex.Matches(
                profile ?? "",
                @"(?:^|[^A-Z0-9])B?(?<band>\d{1,2})(?=$|[^0-9])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(match => int.Parse(
                match.Groups["band"].Value,
                CultureInfo.InvariantCulture))
            .Distinct()
            .ToArray();

    public static bool TryReadServingCell(
        int requestedBand,
        RouterTelemetry telemetry,
        out LteBandCellObservation? observation,
        bool requireSingleBand = true)
    {
        observation = null;
        if (!telemetry.IsConnected)
            return false;

        int[] activeBands = ExtractBands(telemetry.Band).ToArray();
        if (activeBands.Length == 0 ||
            (requireSingleBand
                ? activeBands.Length != 1 || activeBands[0] != requestedBand
                : !activeBands.Contains(requestedBand)))
            return false;

        string servingProfile = NormalizeBandProfile(telemetry.Band, requestedBand);
        string primaryBand = NormalizeBandProfile(
            telemetry.PrimaryBand,
            requestedBand);
        string earfcn = CleanRadioValue(telemetry.Earfcn);
        string pci = CleanRadioValue(telemetry.Pci);
        string cellId = CleanRadioValue(telemetry.CellId);
        bool completeIdentity = earfcn != "-" && pci != "-" && cellId != "-";
        DateTime observed = telemetry.Timestamp == default
            ? DateTime.Now
            : telemetry.Timestamp;
        observation = new LteBandCellObservation(
            requestedBand,
            servingProfile,
            primaryBand,
            earfcn,
            pci,
            cellId,
            telemetry.RsrpDbm,
            telemetry.RsrqDb,
            telemetry.SnrDb,
            observed,
            observed,
            1,
            completeIdentity
                ? "Complete serving PCell observed"
                : "Serving band observed; waiting for complete EARFCN/PCI/CID");
        return true;
    }

    private static string NormalizeBandProfile(string? value, int fallbackBand)
    {
        int[] bands = ExtractBands(value).ToArray();
        return bands.Length == 0
            ? "B" + fallbackBand.ToString(CultureInfo.InvariantCulture)
            : string.Join(" + ", bands.Select(band => "B" +
                band.ToString(CultureInfo.InvariantCulture)));
    }

    private static string CleanRadioValue(string? value)
    {
        string cleaned = Clean(value);
        return cleaned.Length == 0 || cleaned is "0" or "N/A" or "Unknown"
            ? "-"
            : cleaned;
    }

    private static string Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim() == "-"
            ? ""
            : value.Trim();
}

