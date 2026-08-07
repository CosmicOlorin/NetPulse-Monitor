using System.Text.Json;

namespace NetPulseMonitor;

internal sealed class AppSettings
{
    public string PingTarget { get; set; } = "8.8.8.8";
    public int PingIntervalSeconds { get; set; } = 5;
    public int PingTimeoutMilliseconds { get; set; } = 3500;
    public int FailuresForOutage { get; set; } = 3;
    public int SpeedTestIntervalMinutes { get; set; } = 60;
    public int DownloadSampleMegabytes { get; set; } = 20;
    public int UploadSampleMegabytes { get; set; } = 5;
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;

    public static string SettingsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetPulseMonitor");

    public static string SettingsPath => Path.Combine(SettingsFolder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsFolder);
        string json = JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    public void Normalize()
    {
        PingTarget = string.IsNullOrWhiteSpace(PingTarget) ? "8.8.8.8" : PingTarget.Trim();
        PingIntervalSeconds = Math.Clamp(PingIntervalSeconds, 1, 300);
        PingTimeoutMilliseconds = Math.Clamp(PingTimeoutMilliseconds, 500, 30000);
        FailuresForOutage = Math.Clamp(FailuresForOutage, 1, 20);
        SpeedTestIntervalMinutes = Math.Clamp(SpeedTestIntervalMinutes, 0, 1440);
        DownloadSampleMegabytes = Math.Clamp(DownloadSampleMegabytes, 1, 100);
        UploadSampleMegabytes = Math.Clamp(UploadSampleMegabytes, 1, 50);
    }
}
