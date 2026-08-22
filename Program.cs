namespace NetPulseMonitor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (ApplicationUpdater.TryHandleStartup(args))
            return;
        ApplicationConfiguration.Initialize();
        WindowsNotificationIdentity.EnsureRegistered();
        Application.Run(new MainForm());
    }
}
