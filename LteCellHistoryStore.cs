using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;

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
    public double? AveragePingMs { get; init; }
    public double? EstimatedCellLoadPercent { get; init; }
    public double? AverageSinrDb { get; init; }
    public double? AverageRsrqDb { get; init; }
    public double? AverageRsrpDbm { get; init; }
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
    public bool DiscoveryCandidate { get; init; }
    public required string Confidence { get; init; }
}

internal sealed class LteCellHistoryStore : IDisposable
{
    private const int HistoryFormatVersion = 2;
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
    private TimeZoneInfo _officialTimeZone;
    private CellHistoryDocument _document;
    private string? _activeKey;
    private string? _lastKnownKey;
    private CellIdentity? _lastObservedIdentity;
    private DateTime _lastConnectedSampleUtc;
    private DateTime _lastKnownCellUtc;
    private long? _lastTotalBytes;
    private int _activePeriod = -1;
    private DateTime _lastSaveUtc = DateTime.MinValue;
    private bool _dirty;
    private bool _disposed;
    private long _revision;

    public LteCellHistoryStore(
        string? path = null,
        TimeZoneInfo? officialTimeZone = null)
    {
        _officialTimeZone = officialTimeZone ?? TimeZoneInfo.Utc;
        _path = path ?? Path.Combine(
            AppSettings.SettingsFolder,
            "lte-cell-history.json");
        _document = Load(_path, out bool repaired);
        if (repaired)
        {
            _dirty = true;
            SaveCore(DateTime.UtcNow);
        }
    }

    public string StoragePath => _path;

    public void SetOfficialTimeZone(TimeZoneInfo timeZone)
    {
        lock (_gate)
            _officialTimeZone = timeZone;
    }

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
            if (!telemetry.IsConnected ||
                !TryResolveTelemetryIdentity(telemetry, out CellIdentity? identity))
            {
                EndActiveSession();
                SaveIfDue(observedUtc);
                return;
            }

            LteCellHistoryRecord record = ResolveRecord(identity!);
            record.DiscoveryCandidate |= IsLockReadyIdentity(identity!);
            int period = GetTimePeriod(ToOfficialLocal(observedUtc));
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
            AddRadioSample(record, periodStats, telemetry);
            _lastKnownKey = record.Key;
            _lastKnownCellUtc = observedUtc;
            MarkDirty();
            SaveIfDue(observedUtc);
        }
    }

    public void RecordPingSample(long? latencyMs, DateTime? timestamp = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!latencyMs.HasValue || latencyMs.Value < 0)
            return;

        lock (_gate)
        {
            if (_activeKey is null || FindByKey(_activeKey) is not { } record)
                return;
            DateTime observedUtc = ToUtc(timestamp ?? DateTime.Now);
            LteTimeBucketRecord period = GetPeriodStats(
                record,
                GetTimePeriod(ToOfficialLocal(observedUtc)));
            record.PingSamples++;
            record.PingTotalMs += latencyMs.Value;
            period.PingSamples++;
            period.PingTotalMs += latencyMs.Value;
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
            GetPeriodStats(record, GetTimePeriod(ToOfficialLocal(outageUtc)))
                .Disconnections++;
            MarkDirty();
            SaveIfDue(outageUtc);
        }
    }

    public bool RecordSpeedTest(RouterTelemetry telemetry, SpeedTestResult result)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!telemetry.IsConnected)
            return false;
        if (!result.DownloadMbps.HasValue && !result.UploadMbps.HasValue)
            return false;

        lock (_gate)
        {
            LteCellHistoryRecord? record = ResolveSpeedTestRecord(telemetry);
            if (record is null)
                return false;
            DateTime observedUtc = ToUtc(telemetry.Timestamp);
            LteTimeBucketRecord period = GetPeriodStats(
                record,
                GetTimePeriod(ToOfficialLocal(observedUtc)));
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
        DateTime? timestamp = null)
    {
        lock (_gate)
        {
            int period = GetTimePeriod(ToOfficialTime(timestamp));
            return BuildRecommendations(period, _document.Records);
        }
    }

    public IReadOnlyList<LteCellRecommendation> GetHistoryRecommendations(
        DateTime? timestamp = null)
    {
        lock (_gate)
        {
            int currentPeriod = GetTimePeriod(ToOfficialTime(timestamp));
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
        DateTime? timestamp = null)
    {
        lock (_gate)
        {
            return BuildRecommendations(
                    GetTimePeriod(ToOfficialTime(timestamp)),
                    _document.Records)
                .Where(item =>
                    item.ConnectedTime >= MinimumVisiblePeriodTime ||
                    item.DiscoveryCandidate)
                .Where(item => IsEarfcnValidForPrimaryBand(
                    item.PrimaryBand,
                    item.Earfcn) && IsValidPci(item.Pci))
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
        item.ConnectedTime >= MinimumVisiblePeriodTime ||
        item.PeriodConnectedTime >= MinimumVisiblePeriodTime ||
        item.DiscoveryCandidate ||
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
                ToRecommendation(
                    record,
                    period,
                    totalPeriodTraffic,
                    totalPeriodSeconds))
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
        if (!LteRadioIdentifier.TryNormalizeCellId(cellId, out string? normalizedCellId))
            throw new ArgumentException(
                "CID must be a decimal or hexadecimal value " +
                "(for example ABCDE).",
                nameof(cellId));
        if (normalizedCellId is null)
            throw new ArgumentException(
                "CID is required so measurements from different serving cells are never combined.",
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
            record.DiscoveryCandidate = true;
            record.FirstSeenUtc = Earlier(record.FirstSeenUtc, now);
            record.LastSeenUtc = Later(record.LastSeenUtc, now);
            MarkDirty();
            SaveCore(now);
        }
    }

    public bool AddDiscoveryCandidate(
        string bandProfile,
        string earfcn,
        string pci,
        string? cellId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!TryNormalizeBandProfile(
                bandProfile,
                out string normalizedBand,
                out _) ||
            !int.TryParse(earfcn, out int earfcnValue) ||
            earfcnValue is < 1 or > 65535 ||
            !TryNormalizeOptionalPci(pci, out string normalizedPci) ||
            !LteRadioIdentifier.TryNormalizeCellId(
                cellId,
                out string? normalizedCellId) ||
            normalizedCellId is null)
            return false;

        var identity = new CellIdentity(
            normalizedBand,
            GetPrimaryBand(normalizedBand),
            earfcnValue.ToString(),
            normalizedPci,
            normalizedCellId);
        if (!IsEarfcnValidForPrimaryBand(identity.PrimaryBand, identity.Earfcn))
            return false;

        DateTime now = DateTime.UtcNow;
        lock (_gate)
        {
            LteCellHistoryRecord record = ResolveRecord(identity);
            record.PrimaryBand = identity.PrimaryBand;
            record.DiscoveryCandidate = true;
            record.FirstSeenUtc = Earlier(record.FirstSeenUtc, now);
            record.LastSeenUtc = Later(record.LastSeenUtc, now);
            MarkDirty();
            SaveCore(now);
            return true;
        }
    }

    public int ImportDiscoveryCandidates(string csvPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!File.Exists(csvPath))
            return 0;

        int accepted = 0;
        using var parser = new TextFieldParser(csvPath)
        {
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = true
        };
        parser.SetDelimiters(",");
        string[]? header = parser.ReadFields();
        if (header is null)
            return 0;
        var columns = header
            .Select((name, index) => (name, index))
            .ToDictionary(
                item => item.name.TrimStart('\uFEFF'),
                item => item.index,
                StringComparer.OrdinalIgnoreCase);
        if (!columns.TryGetValue("ServingProfile", out int bandIndex) ||
            !columns.TryGetValue("EARFCN", out int earfcnIndex) ||
            !columns.TryGetValue("PCI", out int pciIndex) ||
            !columns.TryGetValue("CellId", out int cellIdIndex) ||
            !columns.TryGetValue("Samples", out int samplesIndex))
            return 0;

        while (!parser.EndOfData)
        {
            string[]? fields;
            try
            {
                fields = parser.ReadFields();
            }
            catch (MalformedLineException)
            {
                continue;
            }
            int required = new[]
                { bandIndex, earfcnIndex, pciIndex, cellIdIndex, samplesIndex }
                .Max();
            if (fields is null || fields.Length <= required ||
                !int.TryParse(fields[samplesIndex], out int samples) ||
                samples <= 0)
                continue;

            string band = fields[bandIndex];
            string earfcn = fields[earfcnIndex];
            if (band.Length == 0 || band == "-" || earfcn.Length == 0)
                continue;
            if (AddDiscoveryCandidate(
                    band,
                    earfcn,
                    fields[pciIndex],
                    fields[cellIdIndex]))
                accepted++;
        }
        return accepted;
    }

    public bool DeleteProfile(string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            LteCellHistoryRecord? record = FindByKey(key);
            if (record is null)
                return false;

            _document.Records.Remove(record);
            if (string.Equals(_activeKey, key, StringComparison.Ordinal))
                EndActiveSession();
            if (string.Equals(_lastKnownKey, key, StringComparison.Ordinal))
                _lastKnownKey = null;
            if (_lastObservedIdentity is not null &&
                string.Equals(
                    _lastObservedIdentity.ToKey(),
                    key,
                    StringComparison.Ordinal))
                _lastObservedIdentity = null;
            MarkDirty();
            SaveCore(DateTime.UtcNow);
            return true;
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
            _lastObservedIdentity = null;
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
        string exactKey = identity.ToKey();
        LteCellHistoryRecord? record = FindByKey(exactKey);
        if (record is not null)
        {
            LteCellHistoryRecord? completeMatch =
                FindUniqueMoreCompleteRecord(identity, record, record);
            if (completeMatch is not null)
            {
                MergeRecords(completeMatch, record);
                _document.Records.Remove(record);
                ReplaceTrackedKey(record.Key, completeMatch.Key);
                return completeMatch;
            }
            if (string.IsNullOrWhiteSpace(record.PrimaryBand))
                record.PrimaryBand = identity.PrimaryBand;
            return record;
        }

        LteCellHistoryRecord? knownMatch = FindUniqueMoreCompleteRecord(identity);
        if (knownMatch is not null)
            return knownMatch;

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

        if (identity.CellId is null)
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

    private LteCellHistoryRecord? FindUniqueMoreCompleteRecord(
        CellIdentity identity,
        LteCellHistoryRecord? excluded = null,
        LteCellHistoryRecord? periodReference = null)
    {
        int incomingCompleteness =
            (identity.Earfcn == "-" ? 0 : 1) +
            (identity.Pci == "-" ? 0 : 1) +
            (identity.CellId is null ? 0 : 1);
        LteCellHistoryRecord[] candidates = _document.Records
            .Where(target =>
                !ReferenceEquals(target, excluded) &&
                Equal(target.Band, identity.Band) &&
                Equal(NormalizePrimaryBand(target.PrimaryBand, target.Band),
                    identity.PrimaryBand) &&
                Equal(target.Earfcn, identity.Earfcn) &&
                (identity.Pci == "-" || Equal(target.Pci, identity.Pci)) &&
                (identity.CellId is null ||
                 Equal(target.CellId ?? "", identity.CellId)) &&
                IdentityCompleteness(target) > incomingCompleteness)
            .ToArray();
        if (candidates.Length == 1)
            return candidates[0];
        if (periodReference is null)
            return null;

        int[] observedPeriods = periodReference.TimeBuckets
            .Where(HasPeriodEvidence)
            .Select(bucket => bucket.Period)
            .Distinct()
            .ToArray();
        if (observedPeriods.Length == 0)
            return null;
        LteCellHistoryRecord[] periodMatches = candidates
            .Where(candidate => candidate.TimeBuckets.Any(bucket =>
                observedPeriods.Contains(bucket.Period) && HasPeriodEvidence(bucket)))
            .ToArray();
        return periodMatches.Length == 1 ? periodMatches[0] : null;
    }

    private void ReplaceTrackedKey(string oldKey, string newKey)
    {
        if (string.Equals(_activeKey, oldKey, StringComparison.Ordinal))
            _activeKey = newKey;
        if (string.Equals(_lastKnownKey, oldKey, StringComparison.Ordinal))
            _lastKnownKey = newKey;
    }

    private bool TryResolveTelemetryIdentity(
        RouterTelemetry telemetry,
        out CellIdentity? identity)
    {
        string? band = NormalizePart(telemetry.Band);
        string? earfcn = NormalizePart(telemetry.Earfcn);
        string pci = NormalizePart(telemetry.Pci, zeroIsMissing: false) ?? "-";
        if (band is null)
        {
            identity = null;
            return false;
        }

        string primaryBand = NormalizePrimaryBand(telemetry.PrimaryBand, band);
        string? cellId = NormalizePart(telemetry.CellId);
        CellIdentity? previous = _lastObservedIdentity;
        bool aggregated = band.Contains('+', StringComparison.Ordinal);
        bool liveIdentityIncomplete = earfcn is null || pci == "-" || cellId is null;
        if (previous is not null && Equal(previous.PrimaryBand, primaryBand))
        {
            bool profileChanged = !Equal(previous.Band, band);
            if (profileChanged && aggregated)
            {
                // MR600 firmware often omits serving-cell identifiers while carrier
                // aggregation is active. A transition into a new aggregated profile
                // keeps the exact identity of the immediately preceding live PCell.
                earfcn = previous.Earfcn;
                pci = previous.Pci;
                cellId = previous.CellId;
            }
            else if (!profileChanged && liveIdentityIncomplete)
            {
                // Repeated samples of the same state may omit individual fields.
                // Fill only missing values, again from the immediately prior sample.
                earfcn ??= previous.Earfcn;
                if (pci == "-")
                    pci = previous.Pci;
                cellId ??= previous.CellId;
            }
        }

        if (earfcn is null || cellId is null ||
            !IsEarfcnValidForPrimaryBand(primaryBand, earfcn))
        {
            identity = null;
            return false;
        }

        identity = new CellIdentity(
            band,
            primaryBand,
            earfcn,
            pci,
            cellId);
        _lastObservedIdentity = identity;
        return true;
    }

    private LteCellHistoryRecord? ResolveSpeedTestRecord(RouterTelemetry telemetry)
    {
        string? band = NormalizePart(telemetry.Band);
        if (band is null)
            return null;
        string primaryBand = NormalizePrimaryBand(telemetry.PrimaryBand, band);
        if (_activeKey is not null && FindByKey(_activeKey) is { } active &&
            Equal(active.Band, band) &&
            Equal(NormalizePrimaryBand(active.PrimaryBand, active.Band), primaryBand))
            return active;

        return TryResolveTelemetryIdentity(telemetry, out CellIdentity? identity)
            ? ResolveRecord(identity!)
            : null;
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
        LteTimeBucketRecord period,
        RouterTelemetry telemetry)
    {
        if (telemetry.RsrpDbm.HasValue)
        {
            record.RsrpSamples++;
            record.RsrpTotal += telemetry.RsrpDbm.Value;
            period.RsrpSamples++;
            period.RsrpTotal += telemetry.RsrpDbm.Value;
        }
        if (telemetry.RsrqDb.HasValue)
        {
            record.RsrqSamples++;
            record.RsrqTotal += telemetry.RsrqDb.Value;
            period.RsrqSamples++;
            period.RsrqTotal += telemetry.RsrqDb.Value;
        }
        if (telemetry.SnrDb.HasValue)
        {
            record.SnrSamples++;
            record.SnrTotal += telemetry.SnrDb.Value;
            period.SnrSamples++;
            period.SnrTotal += telemetry.SnrDb.Value;
        }
    }

    private static LteCellRecommendation ToRecommendation(
        LteCellHistoryRecord record,
        int periodId,
        long totalPeriodTraffic,
        double totalPeriodSeconds)
    {
        LteTimeBucketRecord period =
            GetExistingPeriodStats(record, periodId) ?? new LteTimeBucketRecord
            {
                Period = periodId
            };
        bool hasRadioEvidence = record.SnrSamples > 0 &&
                                record.RsrqSamples > 0 &&
                                record.RsrpSamples > 0;
        bool eligible = record.CellId is not null &&
                        record.ConnectedSeconds >= MinimumObservationSeconds &&
                        hasRadioEvidence;

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
        double? globalPing = Average(record.PingTotalMs, record.PingSamples);
        double? periodPing = Average(period.PingTotalMs, period.PingSamples);
        double? averagePing = Blend(globalPing, periodPing, evidenceWeight);
        double? loadDownload = periodDownload ?? globalDownload;
        double? estimatedCellLoad = loadDownload.HasValue && record.BestDownloadMbps > 0
            ? Math.Clamp(
                100D * (1D - loadDownload.Value / record.BestDownloadMbps),
                0D,
                100D)
            : null;

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
                : !hasRadioEvidence
                    ? "Needs SINR/RSRQ/RSRP"
                    : "CID required"
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
            Pci = record.Pci,
            CellId = record.CellId,
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
            AveragePingMs = averagePing,
            EstimatedCellLoadPercent = estimatedCellLoad,
            AverageSinrDb = Blend(Average(record.SnrTotal, record.SnrSamples),
                Average(period.SnrTotal, period.SnrSamples), evidenceWeight),
            AverageRsrqDb = Blend(Average(record.RsrqTotal, record.RsrqSamples),
                Average(period.RsrqTotal, period.RsrqSamples), evidenceWeight),
            AverageRsrpDbm = Blend(Average(record.RsrpTotal, record.RsrpSamples),
                Average(period.RsrpTotal, period.RsrpSamples), evidenceWeight),
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
            DiscoveryCandidate = record.DiscoveryCandidate,
            Confidence = record.UserAdded && record.Samples == 0
                ? "Manual entry"
                : record.DiscoveryCandidate && record.Samples == 0
                    ? "Awaiting measurements"
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
        _lastObservedIdentity = null;
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

    private static CellHistoryDocument Load(string path, out bool repaired)
    {
        repaired = false;
        try
        {
            if (!File.Exists(path))
                return new CellHistoryDocument();
            string json = File.ReadAllText(path);
            CellHistoryDocument? loaded =
                JsonSerializer.Deserialize<CellHistoryDocument>(json, JsonOptions);
            if (loaded is null || loaded.Version is < 1 or > HistoryFormatVersion)
                return new CellHistoryDocument();
            bool versionChanged = loaded.Version != HistoryFormatVersion;
            string beforeRepair = JsonSerializer.Serialize(loaded, JsonOptions);
            loaded.Records = loaded.Records
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.Key) &&
                    !string.IsNullOrWhiteSpace(item.Band) &&
                    (item.Earfcn == "-" ||
                     int.TryParse(item.Earfcn, out int earfcn) &&
                     earfcn is >= 1 and <= 65535) &&
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
                record.DiscoveryCandidate |=
                    record.UserAdded || IsLockReadyRecord(record);
            }
            RepairLoadedRecords(loaded);
            // Older builds could persist band-only observations. They cannot be
            // safely compared or locked because the same radio channel may be
            // served by multiple cells. Never invent a CID: discard these
            // obsolete, ambiguous records during migration.
            loaded.Records.RemoveAll(record =>
                string.IsNullOrWhiteSpace(record.CellId));
            loaded.Version = HistoryFormatVersion;
            repaired = versionChanged ||
                       !string.Equals(
                           beforeRepair,
                           JsonSerializer.Serialize(loaded, JsonOptions),
                           StringComparison.Ordinal);
            return loaded;
        }
        catch
        {
            return new CellHistoryDocument();
        }
    }

    private static void RepairLoadedRecords(CellHistoryDocument document)
    {
        foreach (LteCellHistoryRecord record in document.Records)
        {
            if (TryNormalizeBandProfile(record.Band, out string band, out _))
                record.Band = band;
            record.PrimaryBand = NormalizePrimaryBand(record.PrimaryBand, record.Band);
            record.Band = PutPrimaryBandFirst(record.Band, record.PrimaryBand);
            record.Pci = NormalizeLoadedPci(record.Pci);
            record.CellId = LteRadioIdentifier.TryNormalizeCellId(
                record.CellId,
                out string? cellId)
                ? cellId
                : null;
            record.DiscoveryCandidate |=
                record.UserAdded || IsLockReadyRecord(record);
            record.TimeBuckets ??= [];
            record.TimeBuckets = record.TimeBuckets
                .Where(item => item.Period is >= 0 and <= 3)
                .GroupBy(item => item.Period)
                .Select(group => group.Aggregate(MergeTimeBuckets))
                .ToList();
        }

        Dictionary<string, string> singleKnownEarfcnByPrimary = document.Records
            .Where(record => IsEarfcnValidForPrimaryBand(
                record.PrimaryBand,
                record.Earfcn))
            .GroupBy(record => record.PrimaryBand, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Primary = group.Key,
                Values = group.Select(record => record.Earfcn)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
            })
            .Where(item => item.Values.Length == 1)
            .ToDictionary(
                item => item.Primary,
                item => item.Values[0],
                StringComparer.OrdinalIgnoreCase);

        foreach (LteCellHistoryRecord record in document.Records)
        {
            if (!IsEarfcnValidForPrimaryBand(record.PrimaryBand, record.Earfcn))
            {
                record.Earfcn = singleKnownEarfcnByPrimary.TryGetValue(
                    record.PrimaryBand,
                    out string? known)
                    ? known
                    : "-";
            }
            record.Key = CreateRecordKey(record);
        }

        MergeExactDuplicates(document.Records);
        // Never attach incomplete historical evidence to a known CID. The same
        // band/EARFCN/PCI can be served by different cells with different results.
        foreach (LteCellHistoryRecord record in document.Records)
            record.Key = CreateRecordKey(record);
    }

    private static string PutPrimaryBandFirst(string band, string primaryBand)
    {
        string[] bands = band.Split('+', StringSplitOptions.TrimEntries |
                                           StringSplitOptions.RemoveEmptyEntries);
        if (!bands.Any(item => Equal(item, primaryBand)))
            return band;
        return string.Join(" + ", bands
            .OrderByDescending(item => Equal(item, primaryBand)));
    }

    private static string NormalizeLoadedPci(string? value) =>
        int.TryParse(value, out int pci) && pci is >= 0 and <= 512
            ? pci.ToString()
            : "-";

    private static bool IsEarfcnValidForPrimaryBand(string primaryBand, string earfcn)
    {
        if (!int.TryParse(earfcn, out int value) || value is < 1 or > 65535)
            return false;
        if (!int.TryParse(primaryBand.Trim().TrimStart('B', 'b'), out int band))
            return true;
        return GetEarfcnRange(band) is not { } range ||
               value >= range.Minimum && value <= range.Maximum;
    }

    private static (int Minimum, int Maximum)? GetEarfcnRange(int band) => band switch
    {
        1 => (1, 599),
        2 => (600, 1199),
        3 => (1200, 1949),
        4 => (1950, 2399),
        5 => (2400, 2649),
        6 => (2650, 2749),
        7 => (2750, 3449),
        8 => (3450, 3799),
        9 => (3800, 4149),
        10 => (4150, 4749),
        11 => (4750, 4949),
        12 => (5010, 5179),
        13 => (5180, 5279),
        14 => (5280, 5379),
        17 => (5730, 5849),
        18 => (5850, 5999),
        19 => (6000, 6149),
        20 => (6150, 6449),
        21 => (6450, 6599),
        22 => (6600, 7399),
        23 => (7500, 7699),
        24 => (7700, 8039),
        25 => (8040, 8689),
        26 => (8690, 9039),
        27 => (9040, 9209),
        28 => (9210, 9659),
        30 => (9770, 9869),
        31 => (9870, 9919),
        32 => (9920, 10359),
        33 => (36000, 36199),
        34 => (36200, 36349),
        35 => (36350, 36949),
        36 => (36950, 37549),
        37 => (37550, 37749),
        38 => (37750, 38249),
        39 => (38250, 38649),
        40 => (38650, 39649),
        41 => (39650, 41589),
        42 => (41590, 43589),
        43 => (43590, 45589),
        44 => (45590, 46589),
        _ => null
    };

    private static void MergeExactDuplicates(List<LteCellHistoryRecord> records)
    {
        foreach (IGrouping<string, LteCellHistoryRecord> group in records
                     .GroupBy(record => record.Key, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)
                     .ToArray())
        {
            LteCellHistoryRecord target = group.First();
            foreach (LteCellHistoryRecord source in group.Skip(1).ToArray())
            {
                MergeRecords(target, source);
                records.Remove(source);
            }
        }
    }

    private static void MergeIncompleteDuplicates(List<LteCellHistoryRecord> records)
    {
        bool merged;
        do
        {
            merged = false;
            foreach (LteCellHistoryRecord source in records
                         .Where(IsIncompleteIdentity)
                         .OrderBy(IdentityCompleteness)
                         .ToArray())
            {
                LteCellHistoryRecord[] candidates = records
                    .Where(target =>
                        !ReferenceEquals(target, source) &&
                        Equal(target.Band, source.Band) &&
                        Equal(target.PrimaryBand, source.PrimaryBand) &&
                        (source.Earfcn == "-" || Equal(target.Earfcn, source.Earfcn)) &&
                        target.Earfcn != "-" &&
                        (source.Pci == "-" || Equal(target.Pci, source.Pci)) &&
                        (source.CellId is null || Equal(target.CellId ?? "", source.CellId)) &&
                        IdentityCompleteness(target) > IdentityCompleteness(source))
                    .ToArray();
                if (candidates.Length > 1)
                {
                    int[] observedPeriods = source.TimeBuckets
                        .Where(HasPeriodEvidence)
                        .Select(bucket => bucket.Period)
                        .Distinct()
                        .ToArray();
                    if (observedPeriods.Length > 0)
                    {
                        candidates = candidates
                            .Where(candidate => candidate.TimeBuckets.Any(bucket =>
                                observedPeriods.Contains(bucket.Period) &&
                                HasPeriodEvidence(bucket)))
                            .ToArray();
                    }
                }
                if (candidates.Length != 1)
                    continue;
                MergeRecords(candidates[0], source);
                records.Remove(source);
                merged = true;
                break;
            }
        } while (merged);
    }

    private static bool IsIncompleteIdentity(LteCellHistoryRecord record) =>
        record.Earfcn == "-" || record.Pci == "-" || record.CellId is null;

    private static bool IsValidPci(string? value) =>
        int.TryParse(value, out int pci) && pci is >= 0 and <= 512;

    private static bool TryNormalizeOptionalPci(
        string? value,
        out string normalized)
    {
        string candidate = value?.Trim() ?? "";
        if (candidate.Length == 0 || candidate == "-")
        {
            normalized = "-";
            return true;
        }

        if (int.TryParse(candidate, out int pci) && pci is >= 0 and <= 512)
        {
            normalized = pci.ToString();
            return true;
        }

        normalized = "-";
        return false;
    }

    private static bool IsLockReadyIdentity(CellIdentity identity) =>
        IsEarfcnValidForPrimaryBand(identity.PrimaryBand, identity.Earfcn) &&
        IsValidPci(identity.Pci) &&
        identity.CellId is not null;

    private static bool IsLockReadyRecord(LteCellHistoryRecord record) =>
        IsEarfcnValidForPrimaryBand(
            NormalizePrimaryBand(record.PrimaryBand, record.Band),
            record.Earfcn) &&
        IsValidPci(record.Pci) &&
        record.CellId is not null;

    private static int IdentityCompleteness(LteCellHistoryRecord record) =>
        (record.Earfcn == "-" ? 0 : 1) +
        (record.Pci == "-" ? 0 : 1) +
        (record.CellId is null ? 0 : 1);

    private static bool HasPeriodEvidence(LteTimeBucketRecord bucket) =>
        bucket.Samples > 0 || bucket.ConnectedSeconds > 0 ||
        bucket.Sessions > 0 || bucket.TrafficBytes > 0 ||
        bucket.SpeedTests > 0 || bucket.PingSamples > 0 ||
        bucket.Disconnections > 0;

    private static string CreateRecordKey(LteCellHistoryRecord record) =>
        new CellIdentity(
            record.Band,
            record.PrimaryBand,
            record.Earfcn,
            record.Pci,
            record.CellId).ToKey();

    private static void MergeRecords(
        LteCellHistoryRecord target,
        LteCellHistoryRecord source)
    {
        target.FirstSeenUtc = Earlier(target.FirstSeenUtc, source.FirstSeenUtc);
        target.LastSeenUtc = Later(target.LastSeenUtc, source.LastSeenUtc);
        target.ConnectedSeconds += source.ConnectedSeconds;
        target.Sessions += source.Sessions;
        target.Handoffs += source.Handoffs;
        target.Disconnections += source.Disconnections;
        target.Samples += source.Samples;
        target.TrafficBytes += source.TrafficBytes;
        target.SpeedTests += source.SpeedTests;
        target.DownloadSamples += source.DownloadSamples;
        target.DownloadTotalMbps += source.DownloadTotalMbps;
        target.BestDownloadMbps = Math.Max(target.BestDownloadMbps, source.BestDownloadMbps);
        target.UploadSamples += source.UploadSamples;
        target.UploadTotalMbps += source.UploadTotalMbps;
        target.BestUploadMbps = Math.Max(target.BestUploadMbps, source.BestUploadMbps);
        target.PingSamples += source.PingSamples;
        target.PingTotalMs += source.PingTotalMs;
        target.RsrpSamples += source.RsrpSamples;
        target.RsrpTotal += source.RsrpTotal;
        target.RsrqSamples += source.RsrqSamples;
        target.RsrqTotal += source.RsrqTotal;
        target.SnrSamples += source.SnrSamples;
        target.SnrTotal += source.SnrTotal;
        target.UserAdded |= source.UserAdded;
        target.DiscoveryCandidate |= source.DiscoveryCandidate;
        foreach (LteTimeBucketRecord sourceBucket in source.TimeBuckets)
        {
            LteTimeBucketRecord? targetBucket = target.TimeBuckets
                .FirstOrDefault(item => item.Period == sourceBucket.Period);
            if (targetBucket is null)
                target.TimeBuckets.Add(sourceBucket);
            else
                MergeTimeBuckets(targetBucket, sourceBucket);
        }
    }

    private static LteTimeBucketRecord MergeTimeBuckets(
        LteTimeBucketRecord target,
        LteTimeBucketRecord source)
    {
        target.LastSeenUtc = Later(target.LastSeenUtc, source.LastSeenUtc);
        target.ConnectedSeconds += source.ConnectedSeconds;
        target.Sessions += source.Sessions;
        target.Handoffs += source.Handoffs;
        target.Disconnections += source.Disconnections;
        target.Samples += source.Samples;
        target.TrafficBytes += source.TrafficBytes;
        target.SpeedTests += source.SpeedTests;
        target.DownloadSamples += source.DownloadSamples;
        target.DownloadTotalMbps += source.DownloadTotalMbps;
        target.UploadSamples += source.UploadSamples;
        target.UploadTotalMbps += source.UploadTotalMbps;
        target.PingSamples += source.PingSamples;
        target.PingTotalMs += source.PingTotalMs;
        return target;
    }

    private DateTime OfficialNow() => TimeZoneInfo.ConvertTimeFromUtc(
        DateTime.UtcNow,
        _officialTimeZone);

    private DateTime ToOfficialTime(DateTime? timestamp) =>
        timestamp.HasValue
            ? ToOfficialLocal(ToUtc(timestamp.Value))
            : OfficialNow();

    private DateTime ToOfficialLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            _officialTimeZone);

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
        public int Version { get; set; } = HistoryFormatVersion;
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
        public int PingSamples { get; set; }
        public double PingTotalMs { get; set; }
        public int RsrpSamples { get; set; }
        public double RsrpTotal { get; set; }
        public int RsrqSamples { get; set; }
        public double RsrqTotal { get; set; }
        public int SnrSamples { get; set; }
        public double SnrTotal { get; set; }
        public bool UserAdded { get; set; }
        public bool DiscoveryCandidate { get; set; }
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
        public int PingSamples { get; set; }
        public double PingTotalMs { get; set; }
        public int RsrpSamples { get; set; }
        public double RsrpTotal { get; set; }
        public int RsrqSamples { get; set; }
        public double RsrqTotal { get; set; }
        public int SnrSamples { get; set; }
        public double SnrTotal { get; set; }
    }
}
