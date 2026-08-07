using System.Net;

namespace NetPulseMonitor;

internal static class PublicIpProbe
{
    private static readonly Uri[] Providers =
    {
        new("https://api4.ipify.org/"),
        new("https://checkip.amazonaws.com/"),
        new("https://ipv4.icanhazip.com/")
    };

    private static readonly HttpClient Client = CreateClient();
    private static int _nextProvider;

    public static async Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        int start = Math.Abs(Interlocked.Increment(ref _nextProvider)) % Providers.Length;
        for (int offset = 0; offset < Providers.Length; offset++)
        {
            Uri provider = Providers[(start + offset) % Providers.Length];
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var request = new HttpRequestMessage(HttpMethod.Get, provider);
                using HttpResponseMessage response = await Client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseContentRead,
                    timeout.Token);
                response.EnsureSuccessStatusCode();
                string value = (await response.Content.ReadAsStringAsync(timeout.Token)).Trim();
                if (IPAddress.TryParse(value, out IPAddress? address) &&
                    address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return address.ToString();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Try the next independent provider. Public IP probe failures do
                // not interrupt ping, router telemetry, or scheduled speed tests.
            }
        }

        return null;
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(4),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30)
        };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NetPulseMonitor/1.0");
        return client;
    }
}
