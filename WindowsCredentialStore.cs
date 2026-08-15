using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace NetPulseMonitor;

internal static class WindowsCredentialStore
{
    private const string TargetName = "NetPulseMonitor:TpLinkMr600";
    private const string CompanionTargetName = "NetPulseMonitor:CompanionPairing";
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public static void SavePassword(string password) =>
        SaveSecret(TargetName, "router", password, "TP-Link password");

    public static string? ReadPassword() => ReadSecret(TargetName, "TP-Link password");

    public static void DeletePassword() => DeleteSecret(TargetName, "TP-Link password");

    public static void SaveCompanionSecret(string secret) =>
        SaveSecret(CompanionTargetName, "companion", secret, "companion pairing key");

    public static string? ReadCompanionSecret() =>
        ReadSecret(CompanionTargetName, "companion pairing key");

    public static void DeleteCompanionSecret() =>
        DeleteSecret(CompanionTargetName, "companion pairing key");

    private static void SaveSecret(string target, string userName, string password, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        byte[] secret = Encoding.Unicode.GetBytes(password);
        if (secret.Length > 512)
            throw new ArgumentOutOfRangeException(nameof(password),
                "The router password is too long for Windows Credential Manager.");

        IntPtr blob = Marshal.AllocCoTaskMem(secret.Length);
        try
        {
            Marshal.Copy(secret, 0, blob, secret.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)secret.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = userName
            };

            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"Windows could not protect the {description}.");
        }
        finally
        {
            Array.Clear(secret);
            if (blob != IntPtr.Zero)
            {
                byte[] zeroes = new byte[Math.Max(1, secret.Length)];
                Marshal.Copy(zeroes, 0, blob, secret.Length);
                Marshal.FreeCoTaskMem(blob);
            }
        }
    }

    private static string? ReadSecret(string target, string description)
    {
        if (!CredRead(target, CredTypeGeneric, 0, out IntPtr pointer))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
                return null;
            throw new Win32Exception(error,
                $"Windows could not read the protected {description}.");
        }

        try
        {
            Credential credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero ||
                credential.CredentialBlobSize == 0)
                return null;

            byte[] secret = new byte[credential.CredentialBlobSize];
            try
            {
                Marshal.Copy(credential.CredentialBlob, secret, 0, secret.Length);
                return Encoding.Unicode.GetString(secret);
            }
            finally
            {
                Array.Clear(secret);
            }
        }
        finally
        {
            CredFree(pointer);
        }
    }

    private static void DeleteSecret(string target, string description)
    {
        if (CredDelete(target, CredTypeGeneric, 0))
            return;

        int error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound)
            throw new Win32Exception(error,
                $"Windows could not remove the protected {description}.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        uint type,
        uint flags,
        out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
