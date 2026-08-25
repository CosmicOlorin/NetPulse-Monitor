namespace NetPulseMonitor;

/// <summary>
/// Keeps one desktop process per Windows session and lets a second launch ask
/// the running process to restore its main window from the notification area.
/// </summary>
internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = @"Local\CosmicOlorin.NetPulseMonitor.Instance";
    private const string ActivationEventName = @"Local\CosmicOlorin.NetPulseMonitor.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly EventWaitHandle _stopEvent = new(false, EventResetMode.ManualReset);
    private readonly Thread _listener;
    private readonly object _gate = new();
    private Action? _activationRequested;
    private bool _pendingActivation;
    private bool _disposed;

    private SingleInstanceCoordinator(Mutex mutex)
    {
        _mutex = mutex;
        _activationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            ActivationEventName);
        _listener = new Thread(Listen)
        {
            IsBackground = true,
            Name = "NetPulse single-instance activation"
        };
        _listener.Start();
    }

    public event Action ActivationRequested
    {
        add
        {
            bool deliverPending;
            lock (_gate)
            {
                _activationRequested += value;
                deliverPending = _pendingActivation;
                _pendingActivation = false;
            }
            if (deliverPending)
                value();
        }
        remove
        {
            lock (_gate)
                _activationRequested -= value;
        }
    }

    public static bool TryAcquire(out SingleInstanceCoordinator? coordinator)
    {
        var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (createdNew)
        {
            coordinator = new SingleInstanceCoordinator(mutex);
            return true;
        }

        mutex.Dispose();
        coordinator = null;
        // The primary creates the activation event immediately after taking
        // the mutex. Retry briefly so a rapid double-click cannot land in that
        // tiny startup interval and lose the request to show the window.
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using EventWaitHandle activation = EventWaitHandle.OpenExisting(
                    ActivationEventName);
                activation.Set();
                break;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
        }
        return false;
    }

    private void Listen()
    {
        WaitHandle[] handles = [_activationEvent, _stopEvent];
        while (WaitHandle.WaitAny(handles) == 0)
        {
            Action? handler;
            lock (_gate)
            {
                handler = _activationRequested;
                if (handler is null)
                    _pendingActivation = true;
            }
            handler?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _stopEvent.Set();
        if (_listener.IsAlive)
            _listener.Join(TimeSpan.FromSeconds(1));
        _activationEvent.Dispose();
        _stopEvent.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }
        _mutex.Dispose();
    }
}
