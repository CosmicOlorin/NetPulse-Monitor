using System.Diagnostics;

namespace NetPulseMonitor;

internal sealed class RouterMonitor : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CsvInterval = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private readonly SemaphoreSlim _providerGate = new(1, 1);
    private readonly CsvLogger _logger;
    private readonly Func<IRouterTelemetryProvider> _providerFactory;
    private CancellationTokenSource? _cancellation;
    private Task? _loopTask;
    private IRouterTelemetryProvider? _provider;
    private AppSettings _settings;
    private string _password;
    private RouterTelemetry _latest = new();
    private bool _disposed;

    public event Action<RouterTelemetry>? TelemetryUpdated;
    public event Action<MonitorEvent>? EventOccurred;

    public RouterMonitor(AppSettings settings, CsvLogger logger, string password)
        : this(settings, logger, password, () => new TpLinkMr600Provider())
    {
    }

    internal RouterMonitor(
        AppSettings settings,
        CsvLogger logger,
        string password,
        Func<IRouterTelemetryProvider> providerFactory)
    {
        _settings = settings;
        _logger = logger;
        _password = password;
        _providerFactory = providerFactory;
    }

    public RouterTelemetry GetSnapshot()
    {
        lock (_gate)
            return _latest;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_loopTask is not null)
            return;

        if (!_settings.TpLinkRouterEnabled)
        {
            Publish(new RouterTelemetry { Status = "Disabled" });
            return;
        }

        if (string.IsNullOrWhiteSpace(_password))
        {
            Publish(new RouterTelemetry
            {
                Status = "Password required",
                Error = "Open TP-Link setup to enter the router password."
            });
            return;
        }

        _cancellation = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunAsync(_cancellation.Token));
    }

    public async Task RestartAsync(
        AppSettings settings,
        string password,
        CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken);
        _settings = settings;
        _password = password;
        Start();
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cancellation = _cancellation;
        Task? loopTask = _loopTask;
        cancellation?.Cancel();

        if (loopTask is not null)
            await loopTask.WaitAsync(cancellationToken);

        if (ReferenceEquals(_loopTask, loopTask))
        {
            _loopTask = null;
            _cancellation = null;
            cancellation?.Dispose();
        }
    }

    public async Task<RouterLockState> ReadLockStateAsync(
        CancellationToken cancellationToken = default)
    {
        return await WithCellLockProviderAsync(
            provider => provider.ReadLockStateAsync(cancellationToken),
            cancellationToken);
    }

    public async Task ApplyCellAndBandLockAsync(
        RouterCellLockTarget target,
        CancellationToken cancellationToken = default)
    {
        await WithCellLockProviderAsync(
            async provider =>
            {
                await provider.ApplyCellAndBandLockAsync(target, cancellationToken);
                return true;
            },
            cancellationToken);
    }

    public async Task RestoreLockStateAsync(
        RouterLockState state,
        CancellationToken cancellationToken = default)
    {
        await WithCellLockProviderAsync(
            async provider =>
            {
                await provider.RestoreLockStateAsync(state, cancellationToken);
                return true;
            },
            cancellationToken);
    }

    public async Task RestoreAutomaticSelectionAsync(
        CancellationToken cancellationToken = default)
    {
        await WithCellLockProviderAsync(
            async provider =>
            {
                await provider.RestoreAutomaticSelectionAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<RouterSmsMessage>> ReadSmsTimelineAsync(
        CancellationToken cancellationToken = default) =>
        WithSmsProviderAsync(
            provider => provider.ReadSmsTimelineAsync(cancellationToken),
            cancellationToken);

    public async Task MarkSmsReadAsync(
        string stack,
        CancellationToken cancellationToken = default)
    {
        await WithSmsProviderAsync(
            async provider =>
            {
                await provider.MarkSmsReadAsync(stack, cancellationToken);
                return true;
            },
            cancellationToken);
    }

    public async Task SendSmsAsync(
        string phoneNumber,
        string content,
        CancellationToken cancellationToken = default)
    {
        await WithSmsProviderAsync(
            async provider =>
            {
                await provider.SendSmsAsync(phoneNumber, content, cancellationToken);
                return true;
            },
            cancellationToken);
    }

    public async Task SaveSmsDraftAsync(
        string phoneNumber,
        string content,
        CancellationToken cancellationToken = default)
    {
        await WithSmsProviderAsync(
            async provider =>
            {
                await provider.SaveSmsDraftAsync(
                    phoneNumber,
                    content,
                    cancellationToken);
                return true;
            },
            cancellationToken);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        int consecutiveFailures = 0;
        string previousState = "";
        DateTime lastCsv = DateTime.MinValue;
        long nextTick = Stopwatch.GetTimestamp();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                RouterCapabilities? connectedCapabilities = null;
                RouterTelemetry telemetry;
                await _providerGate.WaitAsync(cancellationToken);
                try
                {
                    _provider ??= _providerFactory();
                    if (!_provider.IsConnected)
                    {
                        Publish(new RouterTelemetry { Status = "Connecting…" });
                        Uri uri = new(_settings.TpLinkRouterAddress, UriKind.Absolute);
                        connectedCapabilities = await _provider.ConnectAsync(
                            new RouterConnectionOptions
                            {
                                RouterUri = uri,
                                Password = _password,
                                AllowSessionTakeover = true
                            },
                            cancellationToken);
                    }

                    telemetry = await _provider.ReadAsync(cancellationToken);
                }
                finally
                {
                    _providerGate.Release();
                }

                if (connectedCapabilities is not null)
                {
                    RaiseStateEvent(ref previousState, "connected", new MonitorEvent
                    {
                        Kind = "ROUTER",
                        Message = $"TP-Link router connected ({connectedCapabilities.HardwareVersion})"
                    });
                }

                Publish(telemetry);
                consecutiveFailures = 0;
                if (previousState == "offline")
                {
                    EventOccurred?.Invoke(new MonitorEvent
                    {
                        Kind = "ROUTER",
                        Message = "TP-Link router telemetry recovered"
                    });
                }
                previousState = "connected";

                if (DateTime.UtcNow - lastCsv >= CsvInterval)
                {
                    _logger.LogRouterTelemetry(telemetry);
                    lastCsv = DateTime.UtcNow;
                }

                nextTick += (long)(RefreshInterval.TotalSeconds * Stopwatch.Frequency);
                long remainingTicks = nextTick - Stopwatch.GetTimestamp();
                if (remainingTicks > 0)
                {
                    TimeSpan delay = TimeSpan.FromSeconds(
                        (double)remainingTicks / Stopwatch.Frequency);
                    await Task.Delay(delay, cancellationToken);
                }
                else
                {
                    // A slow response consumes its tick. Requests never overlap.
                    nextTick = Stopwatch.GetTimestamp();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (RouterAuthenticationException ex)
            {
                PublishFailure("Authentication required", ex.Message);
                RaiseStateEvent(ref previousState, "authentication", new MonitorEvent
                {
                    Kind = "ROUTER",
                    Message = "TP-Link authentication stopped; update the saved password"
                });
                break;
            }
            catch (RouterBusyException ex)
            {
                PublishFailure("Web session active", ex.Message);
                RaiseStateEvent(ref previousState, "busy", new MonitorEvent
                {
                    Kind = "ROUTER",
                    Message = "TP-Link management interface is busy; automatic takeover will retry"
                });
                await ResetProviderAsync();
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                nextTick = Stopwatch.GetTimestamp();
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                PublishFailure("Router unavailable", FriendlyError(ex));
                RaiseStateEvent(ref previousState, "offline", new MonitorEvent
                {
                    Kind = "ROUTER",
                    Message = "TP-Link telemetry unavailable: " + FriendlyError(ex)
                });
                await ResetProviderAsync();
                int seconds = Math.Min(30, 1 << Math.Min(consecutiveFailures, 4));
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
                nextTick = Stopwatch.GetTimestamp();
            }
        }

        await ResetProviderAsync();
    }

    private void PublishFailure(string status, string error)
    {
        RouterTelemetry previous = GetSnapshot();
        Publish(new RouterTelemetry
        {
            Timestamp = DateTime.Now,
            Status = status,
            Error = error,
            HardwareVersion = previous.HardwareVersion,
            FirmwareVersion = previous.FirmwareVersion
        });
    }

    private void Publish(RouterTelemetry telemetry)
    {
        lock (_gate)
            _latest = telemetry;
        TelemetryUpdated?.Invoke(telemetry);
    }

    private void RaiseStateEvent(
        ref string previousState,
        string currentState,
        MonitorEvent monitorEvent)
    {
        if (previousState == currentState)
            return;
        previousState = currentState;
        EventOccurred?.Invoke(monitorEvent);
    }

    private async Task ResetProviderAsync()
    {
        await _providerGate.WaitAsync();
        try
        {
            IRouterTelemetryProvider? provider = _provider;
            _provider = null;
            if (provider is null)
                return;
            try
            {
                await provider.DisposeAsync();
            }
            catch
            {
            }
        }
        finally
        {
            _providerGate.Release();
        }
    }

    private async Task<T> WithCellLockProviderAsync<T>(
        Func<IRouterCellLockProvider, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _providerGate.WaitAsync(cancellationToken);
        try
        {
            if (_provider is not { IsConnected: true })
                throw new RouterConnectionException(
                    "Connect TP-Link monitoring before changing Cell Lock settings.");
            if (_provider is not IRouterCellLockProvider cellLockProvider)
                throw new RouterConnectionException(
                    "This router provider does not support Cell Lock changes.");
            return await operation(cellLockProvider);
        }
        finally
        {
            _providerGate.Release();
        }
    }

    private async Task<T> WithSmsProviderAsync<T>(
        Func<IRouterSmsProvider, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _providerGate.WaitAsync(cancellationToken);
        try
        {
            if (_provider is not { IsConnected: true })
                throw new RouterConnectionException(
                    "Connect TP-Link monitoring before using SMS.");
            if (_provider is not IRouterSmsProvider smsProvider)
                throw new RouterConnectionException(
                    "This router provider does not support SMS.");
            return await operation(smsProvider);
        }
        finally
        {
            _providerGate.Release();
        }
    }

    private static string FriendlyError(Exception exception) => exception switch
    {
        TimeoutException => "The router did not respond before the timeout.",
        OperationCanceledException => "The router did not respond before the timeout.",
        HttpRequestException => "The router could not be reached on the local network.",
        RouterConnectionException => exception.Message,
        _ => "The router status request failed."
    };

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cancellation?.Cancel();
        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
        }
        _cancellation?.Dispose();
        _cancellation = null;
        _loopTask = null;
        _password = "";
    }
}
