using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace NetPulseMonitor;

internal sealed record UpdateCheckResult(
    bool UpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string Message);

internal static class UpdateChecker
{
    private const string LatestReleaseEndpoint =
        "https://api.github.com/repos/CosmicOlorin/NetPulse-Monitor/releases/latest";

    public static async Task<UpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        string current = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
                         ?? "0.0.0";
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("NetPulseMonitor", current));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using HttpResponseMessage response = await client.GetAsync(
            LatestReleaseEndpoint,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
        string latest = document.RootElement.GetProperty("tag_name").GetString() ?? "";
        string url = document.RootElement.GetProperty("html_url").GetString() ??
                     "https://github.com/CosmicOlorin/NetPulse-Monitor/releases";
        bool available = ReleaseVersionComparer.IsNewer(latest, current);
        return new UpdateCheckResult(
            available,
            current,
            latest,
            url,
            available
                ? $"NetPulse {latest} is available."
                : $"NetPulse {current} is up to date.");
    }
}
