using System.Text.Json;
using System.Text.RegularExpressions;

namespace NetPulseMonitor;

internal sealed class LteCellRecommendation
{
    public required string Key { get; init; }
    public required string Band { get; init; }
    public string PrimaryBand { get; init; } = "-";
    public required string Earfcn { get; init; }
    public required string Pci { get; init; }
    public string? CellId { get; init; }
    public DateTime FirstSeenUtc { get; init; }
    public DateTime LastSeenUtc { get; init; }
    public TimeSpan ConnectedTime { get; init; }
    public int Sessions { get; init; }
    public int Handoffs { get; init; }
    public int Disconnections { get; init; }
    public double DisconnectionsPerHour { get; init; }
    public int SpeedTests { get; init; }
    public double? AverageDownloadMbps { get; init; }
    public double? AverageUploadMbps { get; init; }
    public double WeightedScore { get; set; }
    public int PeriodId { get; init; }
    public required string TimePeriod { get; init; }
    public TimeSpan PeriodConnectedTime { get; init; }
    public int PeriodDisconnections { get; init; }
    public long PeriodTrafficBytes { get; init; }
    public double UsageSharePercent { get; init; }
    public required string UsageBasis { get; init; }
    public double TimeEvidenceWeightPercent { get; init; }
    public bool IsEligible { get; init; }
    public bool UserAdded { get; init; }
    public required string Confidence { get; init; }
}

internal sealed class LteCellHistoryStore : IDisposable
{
    internal const int MinimumObservationSeconds = 10 * 60;
    internal const int MinimumSpeedTests = 1;
    internal static readonly TimeSpan MinimumVisiblePeriodTime =
        TimeSpan.FromMinutes(5);

    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OutageAttributionWindow = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _path;
    private CellHistoryDocument _document;
    private string? _activeKey;
    private string? _lastKnownKey;
    private DateTime _lastConnectedSampleUtc;
    private DateTime _lastKnownCellUtc;
    private long? _lastTotalBytes;
    private int _activePeriod = -1;
    private DateTime _lastSaveUtc = DateTime.MinValue;
    private bool _dirty;
    private bool _disposed;
    private long _revision;

    public LteCellHistoryStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            AppSettings.SettingsFolder,
            "lte-cell-history.json");
        _document = Load(_path);
    }

    public string StoragePath => _path;

    public long Revision
    {
        get
        {
            lock (_gate)
                return _revision;
        }
    }

    public string? GetActiveProfileKey()
    {
        lock (_gate)
            return _activeKey;
    }

    public void RecordTelemetry(RouterTelemetry telemetry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DateTime observedUtc = ToUtc(telemetry.Timestamp);

        lock (_gate)
        {
            if (!telemetry.IsConnected || !TryGetIdentity(telemetry, out CellIdentity? identity))
            {
                EndActiveSession();
                SaveIfDue(observedUtc);
                return;
            }

            LteCellHistoryRecord record = ResolveRecord(identity!);
            int period = GetTimePeriod(observedUtc.ToLocalTime());
            LteTimeBucketRecord periodStats = GetPeriodStats(record, period);
            if (!string.Equals(_activeKey, record.Key, StringComparison.Ordinal))
            {
                if (_activeKey is not null && FindByKey(_activeKey) is { } previous)
                {
                    previous.Handoffs++;
                    if (_activePeriod >= 0)
                        GetPeriodStats(previous, _activePeriod).Handoffs++;
                }

                _activeKey = record.Key;
                record.Sessions++;
                periodStats.Sessions++;
                _lastConnectedSampleUtc = observedUtc;
                _lastTotalBytes = telemetry.TotalBytes;
                _activePeriod = period;
            }
            else
            {
                double elapsed = (observedUtc - _lastConnectedSampleUtc).TotalSeconds;
                if (elapsed > 0 && elapsed <= 5)
                {
                    record.ConnectedSeconds += elapsed;
                    periodStats.ConnectedSeconds += elapsed;
                }
                if (period != _activePeriod)
                {
                    periodStats.Sessions++;
                    _activePeriod = period;
                }

                long trafficDelta = ReadTrafficDelta(telemetry.TotalBytes);
                if (trafficDelta > 0)
                {
                    record.TrafficBytes += trafficDelta;
                    periodStats.TrafficBytes += trafficDelta;
                }
                _lastConnectedSampleUtc = observedUtc;
            }

            record.FirstSeenUtc = Earlier(record.FirstSeenUtc, observedUtc);
            record.LastSeenUtc = Later(record.LastSeenUtc, observedUtc);
            record.Samples++;
            periodStats.Samples++;
            periodStats.LastSeenUtc = Later(periodStats.LastSeenUtc, observedUtc);
            AddRadioSample(record, telemetry);
            _lastKnownKey = record.Key;
            _lastKnownCellUtc = observedUtc;
            MarkDirty();
            SaveIfDue(observedUtc);
        }
    }

    public void RecordConfirmedOutage(DateTime timestamp)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DateTime outageUtc = ToUtc(timestamp);

        lock (_gate)
        {
            if (_lastKnownKey is null ||
                outageUtc - _lastKnownCellUtc > OutageAttributionWindow ||
                FindByKey(_lastKnownKey) is not { } record)
                return;

            record.Disconnections++;
            GetPeriodStats(record, GetTimePeriod(outageUtc.ToLocalTime()))
                .Disconnections++;
            MarkDirty();
            SaveIfDue(outageUtc);
        }
    }

    public bool RecordSpeedTest(RouterTelemetry telemetry, SpeedTestResult result)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!telemetry.IsConnected || !TryGetIdentity(telemetry, out CellIdentity? identity))
            return false;
        if (!result.DownloadMbps.HasValue && !result.UploadMbps.HasValue)
            return false;

        lock (_gate)
        {
            LteCellHistoryRecord record = ResolveRecord(identity!);
            DateTime observedUtc = ToUtc(telemetry.Timestamp);
            LteTimeBucketRecord period = GetPeriodStats(
                record,
                GetTimePeriod(observedUtc.ToLocalTime()));
            record.SpeedTests++;
            period.SpeedTests++;
            if (result.DownloadMbps.HasValue)
            {
                record.DownloadSamples++;
                record.DownloadTotalMbps += result.DownloadMbps.Value;
                record.BestDownloadMbps = Math.Max(
                    record.BestDownloadMbps,
                    result.DownloadMbps.Value);
                period.DownloadSamples++;
                period.DownloadTotalMbps += result.DownloadMbps.Value;
            }
            if (result.UploadMbps.HasValue)
            {
                record.UploadSamples++;
                record.UploadTotalMbps += result.UploadMbps.Value;
                record.BestUploadMbps = Math.Max(
                    record.BestUploadMbps,
                    result.UploadMbps.Value);
                period.UploadSamples++;
                period.UploadTotalMbps += result.UploadMbps.Value;
            }

            MarkDirty();
            SaveIfDue(DateTime.UtcNow);
            return true;
        }
    }

    public IReadOnlyList<LteCellRecommendation> GetRecommendations(
        DateTime? localTime = null)
    {
        lock (_gate)
        {
            int period = GetTimePeriod(localTime ?? DateTime.Now);
            return BuildRecommendations(period, _document.Records);
        }
    }

    public IReadOnlyList<LteCellRecommendation> GetHistoryRecommendations(
        DateTime? localTime = null)
    {
        lock (_gate)
        {
            int currentPeriod = GetTimePeriod(localTime ?? DateTime.Now);
            var history = new List<LteCellRecommendation>();
            for (int period = 0; period < 4; period++)
            {
                IEnumerable<LteCellHistoryRecord> records = _document.Records
                    .Where(record =>
                        GetExistingPeriodStats(record, period) is not null ||
                        (period == currentPeriod && record.TimeBuckets.Count == 0));
                history.AddRange(BuildRecommendations(period, records));
            }
            return history;
        }
    }

    public IReadOnlyList<LteCellRecommendation> GetObservedLockProfiles(
        DateTime? localTime = null)
    {
        lock (_gate)
        {
            return BuildRecommendations(
                    GetTimePeriod(localTime ?? DateTime.Now),
                    _document.Records)
                .Where(item => item.ConnectedTime >= MinimumVisiblePeriodTime)
                .OrderByDescending(item => string.Equals(
                    item.Key,
                    _activeKey,
                    StringComparison.Ordinal))
                .ThenByDescending(item => item.LastSeenUtc)
                .ThenByDescending(item => item.ConnectedTime)
                .ToArray();
        }
    }

    internal static bool IsVisibleToUser(LteCellRecommendation item) =>
        item.PeriodConnectedTime >= MinimumVisiblePeriodTime ||
        (item.UserAdded && item.ConnectedTime <= TimeSpan.Zero);

    private IReadOnlyList<LteCellRecommendation> BuildRecommendations(
        int period,
        IEnumerable<LteCellHistoryRecord> records)
    {
        long totalPeriodTraffic = _document.Records.Sum(record =>
            GetExistingPeriodStats(record, period)?.TrafficBytes ?? 0);
        double totalPeriodSeconds = _document.Records.Sum(record =>
            GetExistingPeriodStats(record, period)?.ConnectedSeconds ?? 0);
        LteCellRecommendation[] recommendations = records
            .Select(record =>
            {
                CellIdentity display = EnrichIdentity(new CellIdentity(
                    record.Band,
                    NormalizePrimaryBand(record.PrimaryBand, record.Band),
                    record.Earfcn,
                    record.Pci,
                    record.CellId));
                return ToRecommendation(
                    record,
                    display,
                    period,
                    totalPeriodTraffic,
                    totalPeriodSeconds);
            })
            .ToArray();
        LteRecommendationScoring.AssignScores(recommendations);
        return recommendations
            .OrderByDescending(item => item.IsEligible)
            .ThenByDescending(item => item.WeightedScore)
            .ThenByDescending(item => item.ConnectedTime)
            .ThenByDescending(item => item.LastSeenUtc)
            .ToArray();
    }

    public void AddManualProfile(
        string bandProfile,
        string earfcn,
        string pci,
        string? cellId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!TryNormalizeBandProfile(bandProfile, out string normalizedBand, out string error))
            throw new ArgumentException(error, nameof(bandProfile));
        if (!int.TryParse(earfcn, out int earfcnValue) || earfcnValue is < 1 or > 65535)
            throw new ArgumentException("EARFCN must be a number from 1 to 65535.", nameof(earfcn));
        if (!int.TryParse(pci, out int pciValue) || pciValue is < 0 or > 512)
            throw new ArgumentException("PCI must be a number from 0 to 512.", nameof(pci));
        string? normalizedCellId = NormalizePart(cellId);
        if (normalizedCellId is not null && !uint.TryParse(normalizedCellId, out _))
            throw new ArgumentException(
                "CID is optional; when supplied it must contain digits only.",
                nameof(cellId));

        var identity = new CellIdentity(
            normalizedBand,
            GetPrimaryBand(normalizedBand),
            earfcnValue.ToString(),
            pciValue.ToString(),
            normalizedCellId);
        DateTime now = DateTime.UtcNow;
        lock (_gate)
        {
            LteCellHistoryRecord record = ResolveRecord(identity);
            record.PrimaryBand = identity.PrimaryBand;
            record.UserAdded = true;
            record.FirstSeenUtc = Earlier(record.FirstSeenUtc, now);
            record.LastSeenUtc = Later(record.LastSeenUtc, now);
            MarkDirty();
            SaveCore(now);
        }
    }

    internal static bool TryNormalizeBandProfile(
        string? value,
        out string normalized,
        out string error)
    {
        MatchCollection matches = Regex.Matches(
            value ?? "",
            @"(?:^|[^A-Z0-9])B?(?<band>\d{1,2})(?=$|[^0-9])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        int[] bands = matches
            .Select(match => int.Parse(match.Groups["band"].Value))
            .Distinct()
            .ToArray();
        if (bands.Length == 0 || bands.Any(band => band is < 1 or > 64))
        {
            normalized = "";
            error = "Enter one or more LTE bands, for example B3 or B3 + B20.";
            return false;
        }
        normalized = string.Join(" + ", bands.Select(band => "B" + band));
        error = "";
        return true;
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            _document = new CellHistoryDocument();
            _activeKey = null;
            _lastKnownKey = null;
            _lastConnectedSampleUtc = default;
            _lastKnownCellUtc = default;
            _lastTotalBytes = null;
            _activePeriod = -1;
            MarkDirty();
            SaveCore(DateTime.UtcNow);
        }
    }

    public void Flush()
    {
        lock (_gate)
            SaveCore(DateTime.UtcNow);
    }

    private LteCellHistoryRecord ResolveRecord(CellIdentity identity)
    {
        identity = EnrichIdentity(identity);
        string exactKey = identity.ToKey();
        LteCellHistoryRecord? record = FindByKey(exactKey);
        if (record is not null)
        {
            if (string.IsNullOrWhiteSpace(record.PrimaryBand))
                record.PrimaryBand = identity.PrimaryBand;
            return record;
        }

        if (identity.Pci != "-")
        {
            LteCellHistoryRecord[] incompleteMatches = _document.Records
                .Where(item =>
                    Equal(item.Band, identity.Band) &&
                    Equal(NormalizePrimaryBand(item.PrimaryBand, item.Band),
                        identity.PrimaryBand) &&
                    Equal(item.Earfcn, identity.Earfcn) &&
                    (string.IsNullOrWhiteSpace(item.Pci) || item.Pci == "-"))
                .ToArray();
            if (incompleteMatches.Length == 1)
            {
                LteCellHistoryRecord incomplete = incompleteMatches[0];
                string oldKey = incomplete.Key;
                incomplete.PrimaryBand = identity.PrimaryBand;
                incomplete.Pci = identity.Pci;
                incomplete.CellId ??= identity.CellId;
                incomplete.Key = new CellIdentity(
                    incomplete.Band,
                    identity.PrimaryBand,
                    incomplete.Earfcn,
                    incomplete.Pci,
                    incomplete.CellId).ToKey();
                if (string.Equals(_activeKey, oldKey, StringComparison.Ordinal))
                    _activeKey = incomplete.Key;
                if (string.Equals(_lastKnownKey, oldKey, StringComparison.Ordinal))
                    _lastKnownKey = incomplete.Key;
                return incomplete;
            }
        }

        LteCellHistoryRecord[] sameRadio = _document.Records
            .Where(item =>
                Equal(item.Band, identity.Band) &&
                Equal(item.Earfcn, identity.Earfcn) &&
                Equal(item.Pci, identity.Pci))
            .ToArray();

        if (identity.CellId is not null)
        {
            LteCellHistoryRecord? unspecified = sameRadio
                .SingleOrDefault(item => string.IsNullOrWhiteSpace(item.CellId));
            if (unspecified is not null && sameRadio.Length == 1)
            {
                string oldKey = unspecified.Key;
                unspecified.CellId = identity.CellId;
                unspecified.Key = exactKey;
                if (string.Equals(_activeKey, oldKey, StringComparison.Ordinal))
                    _activeKey = exactKey;
                if (string.Equals(_lastKnownKey, oldKey, StringComparison.Ordinal))
                    _lastKnownKey = exactKey;
                return unspecified;
            }
        }
        else
        {
            LteCellHistoryRecord? active = sameRadio.FirstOrDefault(item =>
                string.Equals(item.Key, _activeKey, StringComparison.Ordinal));
            if (active is not null)
                return active;
            if (sameRadio.Length == 1)
                return sameRadio[0];
        }

        var created = new LteCellHistoryRecord
        {
            Key = exactKey,
            Band = identity.Band,
            PrimaryBand = identity.PrimaryBand,
            Earfcn = identity.Earfcn,
            Pci = identity.Pci,
            CellId = identity.CellId,
            FirstSeenUtc = DateTime.UtcNow,
            LastSeenUtc = DateTime.UtcNow
        };
        _document.Records.Add(created);
        return created;
    }

    private CellIdentity EnrichIdentity(CellIdentity identity)
    {
        if (identity.Pci != "-" && identity.CellId is not null)
            return identity;

        LteCellHistoryRecord[] samePrimaryCell = _document.Records
            .Where(record =>
                Equal(NormalizePrimaryBand(record.PrimaryBand, record.Band),
                    identity.PrimaryBand) &&
                Equal(record.Earfcn, identity.Earfcn))
            .ToArray();
        string[] knownPci = samePrimaryCell
            .Select(record => NormalizePart(record.Pci, zeroIsMissing: false))
            .Where(value => value is not null && value != "-")
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string pci = identity.Pci;
        if (pci == "-" && knownPci.Length == 1)
            pci = knownPci[0];

        string? cellId = identity.CellId;
        if (cellId is null && pci != "-")
        {
            string[] knownCellIds = samePrimaryCell
                .Where(record => record.Pci == pci)
                .Select(record => NormalizePart(record.CellId))
                .Where(value => value is not null)
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (knownCellIds.Length == 1)
                cellId = knownCellIds[0];
        }
        return identity with { Pci = pci, CellId = cellId };
    }

    private static bool TryGetIdentity(
        RouterTelemetry telemetry,
        out CellIdentity? identity)
    {
        string? band = NormalizePart(telemetry.Band);
        string? earfcn = NormalizePart(telemetry.Earfcn);
        string pci = NormalizePart(telemetry.Pci, zeroIsMissing: false) ?? "-";
        if (band is null || earfcn is null)
        {
            identity = null;
            return false;
        }

        identity = new CellIdentity(
            band,
            NormalizePrimaryBand(telemetry.PrimaryBand, band),
            earfcn,
            pci,
            NormalizePart(telemetry.CellId));
        return true;
    }

    private static string? NormalizePart(string? value, bool zeroIsMissing = true)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        string trimmed = value.Trim();
        return trimmed is "-" or "N/A" or "Unknown" ||
               (zeroIsMissing && trimmed == "0")
            ? null
            : trimmed;
    }

    private static string NormalizePrimaryBand(string? primaryBand, string band)
    {
        string? normalized = NormalizePart(primaryBand);
        return normalized ?? GetPrimaryBand(band);
    }

    private static string GetPrimaryBand(string band) =>
        band.Split('+', 2)[0].Trim();

    private long ReadTrafficDelta(long? currentTotalBytes)
    {
        long? previous = _lastTotalBytes;
        _lastTotalBytes = currentTotalBytes;
        if (!previous.HasValue || !currentTotalBytes.HasValue ||
            currentTotalBytes.Value < previous.Value)
            return 0;

        long delta = currentTotalBytes.Value - previous.Value;
        return delta is > 0 and <= 1_000_000_000 ? delta : 0;
    }

    internal static int GetTimePeriod(DateTime localTime) =>
        Math.Clamp(localTime.Hour / 6, 0, 3);

    internal static string GetTimePeriodLabel(int period) => period switch
    {
        0 => "Night 00–06",
        1 => "Morning 06–12",
        2 => "Afternoon 12–18",
        _ => "Evening 18–24"
    };

    private static LteTimeBucketRecord GetPeriodStats(
        LteCellHistoryRecord record,
        int period)
    {
        LteTimeBucketRecord? existing = GetExistingPeriodStats(record, period);
        if (existing is not null)
            return existing;
        var created = new LteTimeBucketRecord { Period = period };
        record.TimeBuckets.Add(created);
        return created;
    }

    private static LteTimeBucketRecord? GetExistingPeriodStats(
        LteCellHistoryRecord record,
        int period) =>
        record.TimeBuckets.FirstOrDefault(item => item.Period == period);

    private static void AddRadioSample(
        LteCellHistoryRecord record,
        RouterTelemetry telemetry)
    {
        if (telemetry.RsrpDbm.HasValue)
        {
            record.RsrpSamples++;
            record.RsrpTotal += telemetry.RsrpDbm.Value;
        }
        if (telemetry.RsrqDb.HasValue)
        {
            record.RsrqSamples++;
            record.RsrqTotal += telemetry.RsrqDb.Value;
        }
        if (telemetry.SnrDb.HasValue)
        {
            record.SnrSamples++;
            record.SnrTotal += telemetry.SnrDb.Value;
        }
    }

    private static LteCellRecommendation ToRecommendation(
        LteCellHistoryRecord record,
        CellIdentity displayIdentity,
        int periodId,
        long totalPeriodTraffic,
        double totalPeriodSeconds)
    {
        LteTimeBucketRecord period =
            GetExistingPeriodStats(record, periodId) ?? new LteTimeBucketRecord
            {
                Period = periodId
            };
        bool eligible = record.ConnectedSeconds >= MinimumObservationSeconds &&
                        record.SpeedTests >= MinimumSpeedTests &&
                        record.DownloadSamples > 0;

        double globalDropRate = RatePerHour(
            record.Disconnections,
            record.ConnectedSeconds);
        double periodDropRate = period.ConnectedSeconds > 0
            ? RatePerHour(period.Disconnections, period.ConnectedSeconds)
            : globalDropRate;
        double evidenceWeight = CalculateTimeEvidenceWeight(period);
        double weightedDropRate = Blend(globalDropRate, periodDropRate, evidenceWeight);

        double? globalDownload = Average(
            record.DownloadTotalMbps,
            record.DownloadSamples);
        double? periodDownload = Average(
            period.DownloadTotalMbps,
            period.DownloadSamples);
        double? globalUpload = Average(
            record.UploadTotalMbps,
            record.UploadSamples);
        double? periodUpload = Average(
            period.UploadTotalMbps,
            period.UploadSamples);

        double usageShare;
        string usageBasis;
        if (totalPeriodTraffic > 0)
        {
            usageShare = period.TrafficBytes * 100D / totalPeriodTraffic;
            usageBasis = "data";
        }
        else
        {
            usageShare = totalPeriodSeconds > 0
                ? period.ConnectedSeconds * 100D / totalPeriodSeconds
                : 0;
            usageBasis = "time";
        }

        string confidence = !eligible
            ? record.ConnectedSeconds < MinimumObservationSeconds
                ? "Gathering data"
                : "Needs speed test"
            : period.ConnectedSeconds >= 60 * 60 && period.SpeedTests >= 2
                ? "High"
                : period.ConnectedSeconds >= 30 * 60 && period.SpeedTests >= 1
                    ? "Medium"
                    : "Basic";

        return new LteCellRecommendation
        {
            Key = record.Key,
            Band = record.Band,
            PrimaryBand = NormalizePrimaryBand(record.PrimaryBand, record.Band),
            Earfcn = record.Earfcn,
            Pci = displayIdentity.Pci,
            CellId = displayIdentity.CellId,
            FirstSeenUtc = record.FirstSeenUtc,
            LastSeenUtc = record.LastSeenUtc,
            ConnectedTime = TimeSpan.FromSeconds(record.ConnectedSeconds),
            Sessions = record.Sessions,
            Handoffs = record.Handoffs,
            Disconnections = record.Disconnections,
            DisconnectionsPerHour = weightedDropRate,
            SpeedTests = record.SpeedTests,
            AverageDownloadMbps = Blend(
                globalDownload,
                periodDownload,
                evidenceWeight),
            AverageUploadMbps = Blend(
                globalUpload,
                periodUpload,
                evidenceWeight),
            PeriodId = periodId,
            TimePeriod = GetTimePeriodLabel(periodId),
            PeriodConnectedTime = TimeSpan.FromSeconds(period.ConnectedSeconds),
            PeriodDisconnections = period.Disconnections,
            PeriodTrafficBytes = period.TrafficBytes,
            UsageSharePercent = usageShare,
            UsageBasis = usageBasis,
            TimeEvidenceWeightPercent = evidenceWeight * 100D,
            IsEligible = eligible,
            UserAdded = record.UserAdded,
            Confidence = record.UserAdded && record.Samples == 0
                ? "Manual entry"
                : confidence
        };
    }

    private static double RatePerHour(int disconnections, double connectedSeconds)
    {
        double hours = connectedSeconds / 3600D;
        return hours <= 0 ? disconnections : disconnections / hours;
    }

    private static double CalculateTimeEvidenceWeight(LteTimeBucketRecord period)
    {
        double timeWeight = Math.Clamp(period.ConnectedSeconds / 3600D, 0, 1);
        double testWeight = Math.Clamp((period.SpeedTests + 1) / 3D, 0, 1);
        return timeWeight * testWeight;
    }

    private static double Blend(double global, double period, double weight) =>
        global * (1 - weight) + period * weight;

    private static double? Blend(double? global, double? period, double weight)
    {
        if (!global.HasValue)
            return period;
        if (!period.HasValue)
            return global;
        return Blend(global.Value, period.Value, weight);
    }

    private static double? Average(double total, int count) =>
        count > 0 ? total / count : null;

    private void EndActiveSession()
    {
        _activeKey = null;
        _lastConnectedSampleUtc = default;
        _lastTotalBytes = null;
        _activePeriod = -1;
    }

    private LteCellHistoryRecord? FindByKey(string key) =>
        _document.Records.FirstOrDefault(item =>
            string.Equals(item.Key, key, StringComparison.Ordinal));

    private void MarkDirty()
    {
        _dirty = true;
        _revision++;
    }

    private void SaveIfDue(DateTime nowUtc)
    {
        if (_dirty && nowUtc - _lastSaveUtc >= SaveInterval)
            SaveCore(nowUtc);
    }

    private void SaveCore(DateTime nowUtc)
    {
        if (!_dirty)
            return;

        try
        {
            string? folder = Path.GetDirectoryName(_path);
            if (string.IsNullOrWhiteSpace(folder))
                return;
            Directory.CreateDirectory(folder);
            string temporaryPath = _path + ".tmp";
            string json = JsonSerializer.Serialize(_document, JsonOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _path, true);
            _dirty = false;
            _lastSaveUtc = nowUtc;
        }
        catch
        {
            // History is helpful but must never interrupt monitoring.
        }
    }

    private static CellHistoryDocument Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new CellHistoryDocument();
            string json = File.ReadAllText(path);
            CellHistoryDocument? loaded =
                JsonSerializer.Deserialize<CellHistoryDocument>(json, JsonOptions);
            if (loaded is null || loaded.Version != 1)
                return new CellHistoryDocument();
            loaded.Records = loaded.Records
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.Key) &&
                    !string.IsNullOrWhiteSpace(item.Band) &&
                    int.TryParse(item.Earfcn, out int earfcn) &&
                    earfcn is >= 1 and <= 65535 &&
                    (item.Pci == "-" ||
                     int.TryParse(item.Pci, out int pci) && pci is >= 0 and <= 512))
                .Take(500)
                .ToList();
            foreach (LteCellHistoryRecord record in loaded.Records)
            {
                record.PrimaryBand = NormalizePrimaryBand(
                    record.PrimaryBand,
                    record.Band);
                record.TimeBuckets ??= [];
                record.TimeBuckets = record.TimeBuckets
                    .Where(item => item.Period is >= 0 and <= 3)
                    .GroupBy(item => item.Period)
                    .Select(group => group.First())
                    .ToList();
            }
            return loaded;
        }
        catch
        {
            return new CellHistoryDocument();
        }
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
    };

    private static DateTime Earlier(DateTime left, DateTime right) =>
        left == default || right < left ? right : left;

    private static DateTime Later(DateTime left, DateTime right) =>
        right > left ? right : left;

    private static bool Equal(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (_disposed)
            return;
        Flush();
        _disposed = true;
    }

    private sealed record CellIdentity(
        string Band,
        string PrimaryBand,
        string Earfcn,
        string Pci,
        string? CellId)
    {
        public string ToKey() =>
            string.Join("|", Band.ToUpperInvariant(), Earfcn, Pci, CellId ?? "*");
    }

    private sealed class CellHistoryDocument
    {
        public int Version { get; set; } = 1;
        public List<LteCellHistoryRecord> Records { get; set; } = [];
    }

    private sealed class LteCellHistoryRecord
    {
        public string Key { get; set; } = "";
        public string Band { get; set; } = "";
        public string PrimaryBand { get; set; } = "";
        public string Earfcn { get; set; } = "";
        public string Pci { get; set; } = "";
        public string? CellId { get; set; }
        public DateTime FirstSeenUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public double ConnectedSeconds { get; set; }
        public int Sessions { get; set; }
        public int Handoffs { get; set; }
        public int Disconnections { get; set; }
        public long Samples { get; set; }
        public long TrafficBytes { get; set; }
        public int SpeedTests { get; set; }
        public int DownloadSamples { get; set; }
        public double DownloadTotalMbps { get; set; }
        public double BestDownloadMbps { get; set; }
        public int UploadSamples { get; set; }
        public double UploadTotalMbps { get; set; }
        public double BestUploadMbps { get; set; }
        public int RsrpSamples { get; set; }
        public double RsrpTotal { get; set; }
        public int RsrqSamples { get; set; }
        public double RsrqTotal { get; set; }
        public int SnrSamples { get; set; }
        public double SnrTotal { get; set; }
        public bool UserAdded { get; set; }
        public List<LteTimeBucketRecord> TimeBuckets { get; set; } = [];
    }

    private sealed class LteTimeBucketRecord
    {
        public int Period { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public double ConnectedSeconds { get; set; }
        public int Sessions { get; set; }
        public int Handoffs { get; set; }
        public int Disconnections { get; set; }
        public long Samples { get; set; }
        public long TrafficBytes { get; set; }
        public int SpeedTests { get; set; }
        public int DownloadSamples { get; set; }
        public double DownloadTotalMbps { get; set; }
        public int UploadSamples { get; set; }
        public double UploadTotalMbps { get; set; }
    }
}
