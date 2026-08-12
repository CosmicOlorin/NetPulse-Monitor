using System.Net.NetworkInformation;

namespace NetPulseMonitor;

internal sealed class MonitorEngine : IDisposable
{
    private readonly object _gate = new();
    private readonly CsvLogger _logger;
    private readonly Queue<long?> _recent = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private AppSettings _settings;

    private DateTime _startedAt;
    private DateTime? _outageStartedAt;
    private TimeSpan _completedDowntime;
    private long? _currentPing;
    private long _successful;
    private long _failed;
    private int _consecutiveFailures;
    private int _outages;
    private bool _isOnline = true;
    private bool _paused;

    public event Action<long?>? SampleRecorded;
    public event Action<MonitorEvent>? EventOccurred;

    public MonitorEngine(AppSettings settings, CsvLogger logger)
    {
        _settings = settings;
        _logger = logger;
        ResetSession();
    }

    public void Start()
    {
        if (_loopTask is { IsCompleted: false })
            return;

        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => LoopAsync(_cts.Token));
        Emit("SYSTEM", "Monitoring started");
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    public void SetPaused(bool value)
    {
        lock (_gate)
        {
            _paused = value;
            if (value)
                _currentPing = null;
        }

        Emit("SYSTEM", value ? "Monitoring paused" : "Monitoring resumed");
    }

    public bool IsPaused
    {
        get { lock (_gate) return _paused; }
    }

    public void UpdateSettings(AppSettings settings)
    {
        lock (_gate)
            _settings = settings;
    }

    public void ResetSession()
    {
        lock (_gate)
        {
            _startedAt = DateTime.Now;
            _outageStartedAt = null;
            _completedDowntime = TimeSpan.Zero;
            _currentPing = null;
            _successful = 0;
            _failed = 0;
            _consecutiveFailures = 0;
            _outages = 0;
            _isOnline = true;
            _recent.Clear();
        }

        Emit("SYSTEM", "Session counters reset");
    }

    public MonitorSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            DateTime now = DateTime.Now;
            TimeSpan runTime = now - _startedAt;
            TimeSpan downtime = _completedDowntime;

            if (_outageStartedAt.HasValue)
                downtime += now - _outageStartedAt.Value;

            double availability = runTime.TotalSeconds <= 0
                ? 100
                : Math.Clamp(
                    ((runTime.TotalSeconds - downtime.TotalSeconds) / runTime.TotalSeconds) * 100,
                    0, 100);

            var recent = _recent.ToArray();
            int totalRecent = recent.Length;
            int failedRecent = recent.Count(x => !x.HasValue);
            double loss = totalRecent == 0 ? 0 : failedRecent * 100.0 / totalRecent;

            var successfulValues = recent.Where(x => x.HasValue)
                .Select(x => (double)x!.Value).ToArray();

            double jitter = 0;
            if (successfulValues.Length > 1)
            {
                double sum = 0;
                for (int i = 1; i < successfulValues.Length; i++)
                    sum += Math.Abs(successfulValues[i] - successfulValues[i - 1]);
                jitter = sum / (successfulValues.Length - 1);
            }

            return new MonitorSnapshot
            {
                StartedAt = _startedAt,
                IsOnline = _isOnline,
                IsPaused = _paused,
                CurrentPingMs = _currentPing,
                JitterMs = jitter,
                PacketLossPercent = loss,
                SuccessfulPings = _successful,
                FailedPings = _failed,
                Outages = _outages,
                RunTime = runTime,
                TotalDowntime = downtime,
                AvailabilityPercent = availability
            };
        }
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            AppSettings settings;
            bool paused;

            lock (_gate)
            {
                settings = _settings;
                paused = _paused;
            }

            if (!paused)
                await ProbeOnceAsync(settings, token);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.PingIntervalSeconds), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProbeOnceAsync(AppSettings settings, CancellationToken token)
    {
        long? latency = null;

        try
        {
            using var ping = new Ping();
            PingReply reply = await ping.SendPingAsync(
                settings.PingTarget,
                settings.PingTimeoutMilliseconds,
                new byte[32],
                new PingOptions(64, true));

            if (reply.Status == IPStatus.Success)
                latency = reply.RoundtripTime;
        }
        catch when (!token.IsCancellationRequested)
        {
            latency = null;
        }

        if (token.IsCancellationRequested)
            return;

        if (latency.HasValue)
            RegisterSuccess(latency.Value);
        else
            RegisterFailure(settings);

        SampleRecorded?.Invoke(latency);
    }

    private void RegisterSuccess(long latency)
    {
        MonitorEvent? recoveryEvent = null;
        double? duration = null;

        lock (_gate)
        {
            _successful++;
            _currentPing = latency;
            _consecutiveFailures = 0;
            AddRecent(latency);

            if (_outageStartedAt.HasValue)
            {
                TimeSpan outage = DateTime.Now - _outageStartedAt.Value;
                _completedDowntime += outage;
                duration = outage.TotalSeconds;
                _outageStartedAt = null;
                _isOnline = true;

                recoveryEvent = new MonitorEvent
                {
                    Kind = "ONLINE",
                    Message = "Connection restored after " + FormatDuration(outage)
                };
            }
            else
            {
                _isOnline = true;
            }
        }

        if (recoveryEvent is not null)
        {
            _logger.LogEvent(recoveryEvent, duration);
            EventOccurred?.Invoke(recoveryEvent);
        }
    }

    private void RegisterFailure(AppSettings settings)
    {
        MonitorEvent? outageEvent = null;

        lock (_gate)
        {
            _failed++;
            _currentPing = null;
            _consecutiveFailures++;
            AddRecent(null);

            if (!_outageStartedAt.HasValue &&
                _consecutiveFailures >= settings.FailuresForOutage)
            {
                _outageStartedAt = DateTime.Now.AddSeconds(
                    -settings.PingIntervalSeconds * (settings.FailuresForOutage - 1));
                _outages++;
                _isOnline = false;

                outageEvent = new MonitorEvent
                {
                    Timestamp = _outageStartedAt.Value,
                    Kind = "OFFLINE",
                    Message = "Connection outage detected"
                };
            }
        }

        if (outageEvent is not null)
        {
            _logger.LogEvent(outageEvent);
            EventOccurred?.Invoke(outageEvent);
        }
    }

    private void AddRecent(long? value)
    {
        _recent.Enqueue(value);
        while (_recent.Count > 120)
            _recent.Dequeue();
    }

    private void Emit(string kind, string message)
    {
        var evt = new MonitorEvent { Kind = kind, Message = message };
        _logger.LogEvent(evt);
        EventOccurred?.Invoke(evt);
    }

    private static string FormatDuration(TimeSpan duration) =>
        $"{(int)duration.TotalDays:00}d {duration.Hours:00}h " +
        $"{duration.Minutes:00}m {duration.Seconds:00}s";

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
