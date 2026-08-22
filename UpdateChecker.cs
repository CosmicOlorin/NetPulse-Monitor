using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Diagnostics;

namespace NetPulseMonitor;

internal sealed record UpdateCheckResult(
    bool UpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string Message,
    string? AssetUrl = null,
    string? ChecksumUrl = null,
    string? LocalPath = null,
    string Source = "GitHub");

internal static class UpdateChecker
{
    private const string LatestReleaseEndpoint =
        "https://api.github.com/repos/CosmicOlorin/NetPulse-Monitor/releases/latest";

    public static async Task<UpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        string current = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
                         ?? "0.0.0";
        UpdateCheckResult? local = FindLocalUpdate(current);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("NetPulseMonitor", current));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        JsonDocument document;
        try
        {
            using HttpResponseMessage response = await client.GetAsync(
                LatestReleaseEndpoint,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);
        }
        catch when (local is not null)
        {
            return local;
        }
        using (document)
        {
        string latest = document.RootElement.GetProperty("tag_name").GetString() ?? "";
        string url = document.RootElement.GetProperty("html_url").GetString() ??
                     "https://github.com/CosmicOlorin/NetPulse-Monitor/releases";
        JsonElement.ArrayEnumerator assets = document.RootElement
            .GetProperty("assets").EnumerateArray();
        var releaseAssets = assets.Select(asset => new
        {
            Name = asset.GetProperty("name").GetString() ?? "",
            Url = asset.GetProperty("browser_download_url").GetString() ?? ""
        }).ToArray();
        string? assetUrl = releaseAssets.FirstOrDefault(asset =>
            asset.Name.EndsWith("-win-x64.exe", StringComparison.OrdinalIgnoreCase))?.Url;
        string? checksumUrl = releaseAssets.FirstOrDefault(asset =>
            asset.Name.EndsWith(".exe.sha256", StringComparison.OrdinalIgnoreCase) ||
            asset.Name.EndsWith("-win-x64.sha256", StringComparison.OrdinalIgnoreCase))?.Url;
        bool available = ReleaseVersionComparer.IsNewer(latest, current);
        var github = new UpdateCheckResult(
            available,
            current,
            latest,
            url,
            available
                ? $"NetPulse {latest} is available."
                : $"NetPulse {current} is up to date.",
            AssetUrl: assetUrl,
            ChecksumUrl: checksumUrl);
        if (local is not null && !ReleaseVersionComparer.IsNewer(
                github.LatestVersion, local.LatestVersion))
            return local;
        return github;
        }
    }

    private static UpdateCheckResult? FindLocalUpdate(string current)
    {
        string baseDirectory = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDirectory, "NetPulse Monitor.update.exe"),
            Path.Combine(baseDirectory, "Updates", "NetPulse Monitor.exe")
        ];
        foreach (string path in candidates.Where(File.Exists))
        {
            try
            {
                string version = FileVersionInfo.GetVersionInfo(path).FileVersion ?? "";
                if (!ReleaseVersionComparer.IsNewer(version, current))
                    continue;
                return new UpdateCheckResult(
                    true, current, version, path,
                    $"NetPulse {version} is ready in the local production folder.",
                    LocalPath: path,
                    Source: "Local production");
            }
            catch
            {
            }
        }
        return null;
    }
}
