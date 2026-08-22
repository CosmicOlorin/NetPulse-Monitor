using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace NetPulseMonitor;

internal static class ApplicationUpdater
{
    private const string ApplyArgument = "--apply-update";
    private const string CleanupArgument = "--cleanup-updater";

    public static async Task StageAndLaunchAsync(
        UpdateCheckResult update,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        if (!update.UpdateAvailable)
            throw new InvalidOperationException("No newer NetPulse version is available.");
        if (string.IsNullOrWhiteSpace(update.AssetUrl) &&
            string.IsNullOrWhiteSpace(update.LocalPath))
            throw new InvalidOperationException(
                "The release does not contain a Windows executable update asset.");

        string version = NormalizeVersion(update.LatestVersion);
        string stagedPath = Path.Combine(Path.GetTempPath(),
            $"NetPulse-Monitor-{version}-{Guid.NewGuid():N}.exe");
        try
        {
            if (!string.IsNullOrWhiteSpace(update.LocalPath))
            {
                File.Copy(update.LocalPath, stagedPath, overwrite: true);
                progress?.Report(100);
            }
            else
            {
                await DownloadAsync(update.AssetUrl!, stagedPath, progress,
                    cancellationToken);
            }
            ValidateExecutable(stagedPath, update.LatestVersion);
            if (!string.IsNullOrWhiteSpace(update.ChecksumUrl))
                await ValidateChecksumAsync(stagedPath, update.ChecksumUrl,
                    cancellationToken);
            else if (!string.IsNullOrWhiteSpace(update.LocalPath) &&
                     File.Exists(update.LocalPath + ".sha256"))
                await ValidateLocalChecksumAsync(
                    stagedPath, update.LocalPath + ".sha256", cancellationToken);

            using Process current = Process.GetCurrentProcess();
            var start = new ProcessStartInfo(stagedPath)
            {
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            start.ArgumentList.Add(ApplyArgument);
            start.ArgumentList.Add(Application.ExecutablePath);
            start.ArgumentList.Add(current.Id.ToString());
            _ = Process.Start(start) ?? throw new InvalidOperationException(
                "Windows could not start the NetPulse updater.");
        }
        catch
        {
            TryDelete(stagedPath);
            throw;
        }
    }

    public static bool TryHandleStartup(string[] args)
    {
        if (args.Length >= 3 && args[0].Equals(
                ApplyArgument, StringComparison.OrdinalIgnoreCase))
        {
            ApplyUpdate(args[1], args[2]);
            return true;
        }
        if (args.Length >= 2 && args[0].Equals(
                CleanupArgument, StringComparison.OrdinalIgnoreCase))
            DeleteLater(args[1]);
        return false;
    }

    private static void ApplyUpdate(string targetPath, string processIdText)
    {
        try
        {
            string self = Environment.ProcessPath ?? Application.ExecutablePath;
            string target = Path.GetFullPath(targetPath);
            if (int.TryParse(processIdText, out int processId))
            {
                try
                {
                    using Process process = Process.GetProcessById(processId);
                    process.WaitForExit(60000);
                }
                catch (ArgumentException)
                {
                }
            }

            Exception? lastError = null;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                try
                {
                    File.Copy(self, target, overwrite: true);
                    lastError = null;
                    break;
                }
                catch (IOException ex)
                {
                    lastError = ex;
                    Thread.Sleep(500);
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastError = ex;
                    Thread.Sleep(500);
                }
            }
            if (lastError is not null)
                throw lastError;

            var restart = new ProcessStartInfo(target)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(target) ?? AppContext.BaseDirectory
            };
            restart.ArgumentList.Add(CleanupArgument);
            restart.ArgumentList.Add(self);
            Process.Start(restart);
        }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.SettingsFolder);
                File.WriteAllText(Path.Combine(AppSettings.SettingsFolder,
                    "update-error.log"), ex.ToString());
            }
            catch
            {
            }
        }
    }

    private static void DeleteLater(string path) => _ = Task.Run(async () =>
    {
        await Task.Delay(1500);
        for (int attempt = 0; attempt < 10 && File.Exists(path); attempt++)
        {
            TryDelete(path);
            if (File.Exists(path))
                await Task.Delay(500);
        }
    });

    private static async Task DownloadAsync(
        string url,
        string destination,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("NetPulseMonitor", "updater"));
        using HttpResponseMessage response = await client.GetAsync(url,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? 0;
        await using Stream input = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        await using var output = new FileStream(destination, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 81920, useAsync: true);
        byte[] buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            written += read;
            if (total > 0)
                progress?.Report((int)Math.Clamp(written * 100 / total, 0, 100));
        }
        progress?.Report(100);
    }

    private static void ValidateExecutable(string path, string expectedVersion)
    {
        var info = new FileInfo(path);
        if (info.Length < 1024 * 1024)
            throw new InvalidDataException("The downloaded update is incomplete.");
        using (FileStream stream = File.OpenRead(path))
        {
            if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
                throw new InvalidDataException("The downloaded update is not a Windows executable.");
        }
        string actual = FileVersionInfo.GetVersionInfo(path).FileVersion ?? "";
        if (NormalizeVersion(actual) != NormalizeVersion(expectedVersion))
            throw new InvalidDataException(
                $"The update version is {actual}, but {expectedVersion} was expected.");
    }

    private static async Task ValidateChecksumAsync(
        string path,
        string checksumUrl,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("NetPulseMonitor", "updater"));
        string checksumText = await client.GetStringAsync(checksumUrl,
            cancellationToken);
        Match expectedMatch = Regex.Match(checksumText, "[A-Fa-f0-9]{64}");
        if (!expectedMatch.Success)
            throw new InvalidDataException("The release checksum file is invalid.");
        await using FileStream stream = File.OpenRead(path);
        string actual = Convert.ToHexString(await SHA256.HashDataAsync(
            stream, cancellationToken));
        if (!actual.Equals(expectedMatch.Value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The downloaded update failed SHA-256 verification.");
    }

    private static async Task ValidateLocalChecksumAsync(
        string path,
        string checksumPath,
        CancellationToken cancellationToken)
    {
        string checksumText = await File.ReadAllTextAsync(
            checksumPath, cancellationToken);
        Match expectedMatch = Regex.Match(checksumText, "[A-Fa-f0-9]{64}");
        if (!expectedMatch.Success)
            throw new InvalidDataException("The local update checksum file is invalid.");
        await using FileStream stream = File.OpenRead(path);
        string actual = Convert.ToHexString(await SHA256.HashDataAsync(
            stream, cancellationToken));
        if (!actual.Equals(expectedMatch.Value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The local update failed SHA-256 verification.");
    }

    private static string NormalizeVersion(string value)
    {
        string candidate = value.Trim().TrimStart('v', 'V');
        return Version.TryParse(candidate, out Version? version)
            ? version.ToString(3)
            : candidate;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
