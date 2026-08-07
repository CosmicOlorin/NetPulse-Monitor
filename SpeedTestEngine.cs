using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;

namespace NetPulseMonitor;

internal static class SpeedTestEngine
{
    private sealed record Provider(
        string Name,
        Func<long, string> DownloadUrl,
        string? UploadUrl);

    // Providers are attempted in order and each direction falls back independently.
    // Static-file providers are read only up to the requested sample size.
    private static readonly Provider[] Providers =
    {
        new("Cloudflare", bytes =>
            $"https://speed.cloudflare.com/__down?bytes={bytes}&cache={Guid.NewGuid():N}",
            "https://speed.cloudflare.com/__up"),
        new("OVH", _ =>
            $"https://proof.ovh.net/files/100Mb.dat?cache={Guid.NewGuid():N}", null),
        new("Hetzner", _ =>
            $"https://speed.hetzner.de/100MB.bin?cache={Guid.NewGuid():N}", null)
    };

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 4
        };

        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NetPulseMonitor/1.0");
        return client;
    }

    public static async Task<SpeedTestResult> RunAsync(
        int downloadMegabytes, int uploadMegabytes, CancellationToken token)
    {
        var quality = await MeasureQualityAsync(token);
        long downloadBytes = checked(downloadMegabytes * 1_000_000L);
        int uploadBytes = checked(uploadMegabytes * 1_000_000);
        var warnings = new List<string>();

        (double Value, string Provider)? download = null;
        foreach (Provider provider in Providers)
        {
            try
            {
                download = (await MeasureDownloadAsync(provider, downloadBytes, token), provider.Name);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"{provider.Name} download: {FriendlyMessage(ex)}");
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                warnings.Add($"{provider.Name} download: timed out");
            }
        }

        (double Value, string Provider)? upload = null;
        foreach (Provider provider in Providers.Where(x => x.UploadUrl is not null))
        {
            try
            {
                upload = (await MeasureUploadAsync(provider, uploadBytes, token), provider.Name);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"{provider.Name} upload: {FriendlyMessage(ex)}");
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                warnings.Add($"{provider.Name} upload: timed out");
            }
        }

        if (download is null && upload is null)
            throw new InvalidOperationException("All speed-test providers failed. " + string.Join(" | ", warnings));

        string providerName = download?.Provider == upload?.Provider
            ? download?.Provider ?? upload!.Value.Provider
            : $"{download?.Provider ?? "N/A"} down / {upload?.Provider ?? "N/A"} up";

        return new SpeedTestResult
        {
            Provider = providerName,
            LatencyMs = quality.Latency,
            JitterMs = quality.Jitter,
            PacketLossPercent = quality.Loss,
            DownloadMbps = download?.Value,
            UploadMbps = upload?.Value,
            Warning = warnings.Count == 0 ? null : string.Join(" | ", warnings)
        };
    }

    private static async Task<(double Latency, double Jitter, double Loss)> MeasureQualityAsync(
        CancellationToken token)
    {
        const int attempts = 8;
        var samples = new List<double>();
        for (int i = 0; i < attempts; i++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var ping = new Ping();
                PingReply reply = await ping.SendPingAsync("1.1.1.1", 2_000);
                token.ThrowIfCancellationRequested();
                if (reply.Status == IPStatus.Success)
                    samples.Add(reply.RoundtripTime);
            }
            catch (OperationCanceledException) { throw; }
            catch { }

            if (i < attempts - 1)
                await Task.Delay(100, token);
        }

        if (samples.Count == 0)
            return (0, 0, 100);

        double jitter = samples.Count < 2 ? 0 : samples.Zip(samples.Skip(1),
            (a, b) => Math.Abs(a - b)).Average();
        return (samples.Average(), jitter, (attempts - samples.Count) * 100.0 / attempts);
    }

    private static async Task<double> MeasureDownloadAsync(
        Provider provider, long requestedBytes, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        // A 20 MB sample needs about 55 seconds on a 3 Mbps LTE link.
        timeout.CancelAfter(TimeSpan.FromSeconds(75));
        using var request = new HttpRequestMessage(HttpMethod.Get, provider.DownloadUrl(requestedBytes));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        var stopwatch = Stopwatch.StartNew();
        using HttpResponseMessage response = await Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        byte[] buffer = new byte[128 * 1024];
        long total = 0;
        while (total < requestedBytes)
        {
            int count = (int)Math.Min(buffer.Length, requestedBytes - total);
            int read = await stream.ReadAsync(buffer.AsMemory(0, count), timeout.Token);
            if (read == 0) break;
            total += read;
        }
        stopwatch.Stop();
        if (total < Math.Min(requestedBytes, 100_000))
            throw new InvalidOperationException("endpoint returned too little data");
        return total * 8.0 / 1_000_000 / stopwatch.Elapsed.TotalSeconds;
    }

    private static async Task<double> MeasureUploadAsync(
        Provider provider, int bytes, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        byte[] payload = GC.AllocateUninitializedArray<byte>(bytes);
        Random.Shared.NextBytes(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, provider.UploadUrl);
        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var stopwatch = Stopwatch.StartNew();
        using HttpResponseMessage response = await Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        stopwatch.Stop();
        return payload.Length * 8.0 / 1_000_000 / stopwatch.Elapsed.TotalSeconds;
    }

    private static string FriendlyMessage(Exception ex) => ex switch
    {
        TaskCanceledException => "timed out",
        HttpRequestException http when http.StatusCode.HasValue => $"HTTP {(int)http.StatusCode.Value}",
        _ => ex.Message
    };
}
