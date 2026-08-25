namespace NetPulseMonitor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (ApplicationUpdater.TryHandleStartup(args))
            return;

        if (!SingleInstanceCoordinator.TryAcquire(out SingleInstanceCoordinator? singleInstance))
            return;

        using SingleInstanceCoordinator coordinator = singleInstance!;
        ApplicationConfiguration.Initialize();
        WindowsNotificationIdentity.EnsureRegistered();
        using var mainForm = new MainForm();
        coordinator.ActivationRequested += mainForm.RestoreFromExternalLaunch;
        Application.Run(mainForm);
    }
}
