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
    public bool RegionalSetupCompleted { get; set; }
    public string CountryCode { get; set; } = "";
    public string CountryCultureName { get; set; } = "";
    public string OfficialTimeZoneId { get; set; } = "";
    public bool RouterSetupCompleted { get; set; }
    public bool TpLinkRouterEnabled { get; set; }
    public string TpLinkRouterAddress { get; set; } = "http://192.168.1.1/";
    public bool RememberTpLinkPassword { get; set; } = true;
    public string ConnectionDetailsView { get; set; } = "Lte";
    public bool AutomaticCellLockEnabled { get; set; }
    public int AutomaticCellLockMinimumDwellMinutes { get; set; } = 30;
    public int AutomaticCellLockMaxChangesPerDay { get; set; } = 6;
    public DateTime? AutomaticCellLockCounterDate { get; set; }
    public int AutomaticCellLockChangesToday { get; set; }
    public int CellLockValidationSeconds { get; set; } = 90;
    public DateTime? LastAutomaticCellLockUtc { get; set; }
    public string LastAutomaticCellLockKey { get; set; } = "";
    public RouterLockState? PendingCellLockRollback { get; set; }
    public string PendingCellLockTargetKey { get; set; } = "";
    public DateTime? PendingCellLockAppliedUtc { get; set; }
    public Dictionary<string, string> SmsContacts { get; set; } = [];
    public string Theme { get; set; } = "System";
    public string DashboardLayout { get; set; } = "LTE Advanced";
    public bool ShowHealthSummary { get; set; } = true;
    public bool ShowSmartRecommendation { get; set; } = true;
    public bool CheckForUpdates { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }
    public int CellExperimentMinutesPerProfile { get; set; } = 5;
    public bool DiscoveryHistoryImported { get; set; }
    public bool CompanionEnabled { get; set; }
    public int CompanionPort { get; set; } = 45831;

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
        // NetPulse results use one consistent sample size so history entries are
        // comparable across scheduled and connection-change tests.
        DownloadSampleMegabytes = 20;
        UploadSampleMegabytes = 5;
        RegionalCountryOption country = RegionalSettingsCatalog.ResolveCountry(
            CountryCode);
        CountryCode = country.Code;
        CountryCultureName = RegionalSettingsCatalog.ResolveCultureName(
            CountryCode,
            CountryCultureName);
        OfficialTimeZoneId = RegionalSettingsCatalog.ResolveTimeZone(
            OfficialTimeZoneId,
            CountryCode).Id;
        TpLinkRouterAddress = NormalizeRouterAddress(TpLinkRouterAddress);
        ConnectionDetailsView = ConnectionDetailsView switch
        {
            "Adsl" or "Vdsl" or "Dsl" => "Dsl",
            "Ftth" or "Fttb" or "Fiber" => "Fiber",
            _ => "Lte"
        };
        AutomaticCellLockMinimumDwellMinutes = Math.Clamp(
            AutomaticCellLockMinimumDwellMinutes, 15, 360);
        AutomaticCellLockMaxChangesPerDay = Math.Clamp(
            AutomaticCellLockMaxChangesPerDay, 1, 12);
        AutomaticCellLockChangesToday = Math.Clamp(
            AutomaticCellLockChangesToday, 0, AutomaticCellLockMaxChangesPerDay);
        CellLockValidationSeconds = Math.Clamp(CellLockValidationSeconds, 30, 300);
        Theme = Theme is "Light" or "Dark" ? Theme : "System";
        DashboardLayout = DashboardLayout switch
        {
            "Simple" => "LTE Simple",
            "LTE Simple" or "LTE Advanced" or "DSL / Fiber" or
                "ISP troubleshooting" => DashboardLayout,
            _ => "LTE Advanced"
        };
        CellExperimentMinutesPerProfile = Math.Clamp(
            CellExperimentMinutesPerProfile, 2, 30);
        CompanionPort = Math.Clamp(CompanionPort, 1024, 65535);
        SmsContacts = (SmsContacts ?? [])
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Key) &&
                !string.IsNullOrWhiteSpace(item.Value))
            .Take(250)
            .GroupBy(
                item => SmsConversationBuilder.NormalizeAddress(item.Key, CountryCode),
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    string name = group.Last().Value.Trim();
                    return name[..Math.Min(80, name.Length)];
                },
                StringComparer.Ordinal);
    }

    private static string NormalizeRouterAddress(string? value)
    {
        string candidate = string.IsNullOrWhiteSpace(value)
            ? "http://192.168.1.1/"
            : value.Trim();

        if (!candidate.Contains("://", StringComparison.Ordinal))
            candidate = "http://" + candidate;

        return Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri)
            ? new UriBuilder(uri) { Path = "/", Query = "", Fragment = "" }.Uri.ToString()
            : "http://192.168.1.1/";
    }
}
