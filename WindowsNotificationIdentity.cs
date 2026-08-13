using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NetPulseMonitor;

/// <summary>
/// Gives unpackaged Windows notifications a stable, human-readable sender.
/// Without an explicit registered AppUserModelID, Windows assigns NotifyIcon
/// a generated identifier and exposes that internal value in notification UI.
/// </summary>
internal static class WindowsNotificationIdentity
{
    private const string AppUserModelId = "CosmicOlorin.NetPulseMonitor";
    private const string RegistrationPath =
        @"Software\Classes\AppUserModelId\" + AppUserModelId;

    public static void EnsureRegistered()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(
                RegistrationPath,
                writable: true);
            key?.SetValue("DisplayName", "NetPulse Monitor", RegistryValueKind.String);
            key?.SetValue("IconUri", Application.ExecutablePath, RegistryValueKind.String);
            key?.SetValue("ShowInSettings", 1, RegistryValueKind.DWord);
            SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        }
        catch (UnauthorizedAccessException)
        {
            // Notifications still work if policy prevents per-user registration.
            SetProcessIdentityBestEffort();
        }
        catch (System.Security.SecurityException)
        {
            SetProcessIdentityBestEffort();
        }
    }

    private static void SetProcessIdentityBestEffort()
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        string appId);
}
