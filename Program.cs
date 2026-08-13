namespace NetPulseMonitor;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        WindowsNotificationIdentity.EnsureRegistered();
        Application.Run(new MainForm());
    }
}
