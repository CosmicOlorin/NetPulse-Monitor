namespace NetPulseMonitor;

internal sealed record AutomaticSpeedTestRequest(string Reason);

internal sealed class AutomaticSpeedTestCoordinator
{
    private static readonly TimeSpan StabilizationDelay = TimeSpan.FromSeconds(12);

    private readonly object _gate = new();
    private readonly List<string> _reasons = new();
    private DateTime? _dueUtc;
    private bool _outagePending;
    private RouterTelemetry? _lastConnectedRouter;
    private string? _lastPublicIp;

    public DateTime? DueUtc
    {
        get { lock (_gate) return _dueUtc; }
    }

    public void ObserveOutage()
    {
        lock (_gate)
            _outagePending = true;
    }

    public void ObserveRecovery(DateTime nowUtc)
    {
        lock (_gate)
        {
            if (!_outagePending)
                return;
            _outagePending = false;
            QueueLocked("connection restored after a confirmed outage", nowUtc);
        }
    }

    public void ObserveRouterTelemetry(RouterTelemetry telemetry, DateTime nowUtc)
    {
        if (!telemetry.IsConnected)
            return;

        lock (_gate)
        {
            RouterTelemetry? previous = _lastConnectedRouter;
            _lastConnectedRouter = telemetry;
            if (previous is null)
                return;

            if (Changed(previous.Band, telemetry.Band))
                QueueLocked("LTE band changed", nowUtc);

            if (CellChanged(previous, telemetry))
                QueueLocked("LTE cell changed", nowUtc);
        }
    }

    public void ObservePublicIp(string address, DateTime nowUtc)
    {
        string normalized = address.Trim();
        if (normalized.Length == 0)
            return;

        lock (_gate)
        {
            string? previous = _lastPublicIp;
            _lastPublicIp = normalized;
            if (previous is not null &&
                !string.Equals(previous, normalized, StringComparison.OrdinalIgnoreCase))
            {
                QueueLocked("public IP changed", nowUtc);
            }
        }
    }

    public bool TryTakeDue(DateTime nowUtc, out AutomaticSpeedTestRequest? request)
    {
        lock (_gate)
        {
            if (!_dueUtc.HasValue || nowUtc < _dueUtc.Value || _reasons.Count == 0)
            {
                request = null;
                return false;
            }

            request = new AutomaticSpeedTestRequest(string.Join(", ", _reasons));
            _reasons.Clear();
            _dueUtc = null;
            return true;
        }
    }

    private void QueueLocked(string reason, DateTime nowUtc)
    {
        if (!_reasons.Contains(reason, StringComparer.OrdinalIgnoreCase))
            _reasons.Add(reason);
        // A fresh transition restarts the short stability window. This prevents
        // assigning a large transfer to a cell that disappeared seconds later.
        _dueUtc = nowUtc + StabilizationDelay;
    }

    private static bool CellChanged(RouterTelemetry previous, RouterTelemetry current)
    {
        if (Changed(previous.Earfcn, current.Earfcn) ||
            Changed(previous.Pci, current.Pci, zeroIsValue: true))
            return true;

        return IsValue(previous.CellId) && IsValue(current.CellId) &&
               !string.Equals(
                   previous.CellId.Trim(),
                   current.CellId.Trim(),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool Changed(
        string previous,
        string current,
        bool zeroIsValue = false) =>
        IsValue(previous, zeroIsValue) && IsValue(current, zeroIsValue) &&
        !string.Equals(
            previous.Trim(), current.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsValue(string? value, bool zeroIsValue = false) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim() != "-" &&
        (zeroIsValue || value.Trim() != "0");
}
