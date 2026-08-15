using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NetPulseMonitor;

internal sealed record CompanionSnapshot(
    DateTime Timestamp,
    bool InternetOnline,
    bool MonitoringPaused,
    int? PingMs,
    double JitterMs,
    double PacketLossPercent,
    double AvailabilityPercent,
    int Outages,
    string RouterState,
    bool LteRegistered,
    string NetworkType,
    string Band,
    string PrimaryBand,
    string Earfcn,
    string Pci,
    string CellId,
    double? RsrpDbm,
    double? RsrqDb,
    double? SnrDb,
    int? UnreadSmsCount);

internal sealed class CompanionService : IAsyncDisposable
{
    public const int DefaultPort = 45831;
    private const int DiscoveryPort = 45832;
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    private readonly Func<CompanionSnapshot> _snapshotFactory;
    private readonly object _nonceGate = new();
    private readonly Dictionary<string, DateTime> _usedNonces = new(StringComparer.Ordinal);
    private CancellationTokenSource? _cancellation;
    private TcpListener? _listener;
    private UdpClient? _discovery;
    private Task? _acceptTask;
    private Task? _discoveryTask;
    private byte[] _key = [];

    public bool IsRunning => _listener is not null;
    public int Port { get; private set; } = DefaultPort;
    public string PairingUri { get; private set; } = "";

    public CompanionService(Func<CompanionSnapshot> snapshotFactory) =>
        _snapshotFactory = snapshotFactory;

    public void Start(int port, string pairingSecret)
    {
        if (IsRunning)
            return;
        if (port is < 1024 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        ArgumentException.ThrowIfNullOrWhiteSpace(pairingSecret);

        Port = port;
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(pairingSecret));
        PairingUri = BuildPairingUri(port, pairingSecret);
        _cancellation = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start(16);
        _acceptTask = AcceptLoopAsync(_cancellation.Token);

        try
        {
            _discovery = new UdpClient(DiscoveryPort);
            _discoveryTask = DiscoveryLoopAsync(_cancellation.Token);
        }
        catch (SocketException)
        {
            _discovery?.Dispose();
            _discovery = null;
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation = _cancellation;
        _cancellation = null;
        cancellation?.Cancel();
        _listener?.Stop();
        _listener = null;
        _discovery?.Dispose();
        _discovery = null;
        Task[] tasks = [_acceptTask ?? Task.CompletedTask, _discoveryTask ?? Task.CompletedTask];
        try { await Task.WhenAll(tasks); } catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
        _acceptTask = null;
        _discoveryTask = null;
        cancellation?.Dispose();
        CryptographicOperations.ZeroMemory(_key);
        _key = [];
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(cancellationToken); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) when (cancellationToken.IsCancellationRequested) { break; }
            _ = HandleClientAsync(client, cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;
            NetworkStream stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
            string? requestLine = await reader.ReadLineAsync(cancellationToken);
            if (requestLine is null)
                return;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 } line)
            {
                int separator = line.IndexOf(':');
                if (separator > 0)
                    headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }

            string[] request = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (request.Length < 2 || request[0] != "GET")
            {
                await WriteResponseAsync(stream, 405, "application/json", "{\"error\":\"method_not_allowed\"}", cancellationToken);
                return;
            }
            string path = request[1].Split('?', 2)[0];
            if (path == "/v1/info")
            {
                string info = JsonSerializer.Serialize(new { name = "NetPulse Monitor", protocol = 1, port = Port });
                await WriteResponseAsync(stream, 200, "application/json", info, cancellationToken);
                return;
            }
            if (path != "/v1/snapshot" || !Authorize("GET", path, headers))
            {
                await WriteResponseAsync(stream, 401, "application/json", "{\"error\":\"unauthorized\"}", cancellationToken);
                return;
            }

            byte[] plain = JsonSerializer.SerializeToUtf8Bytes(_snapshotFactory());
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] cipher = new byte[plain.Length];
            byte[] tag = new byte[16];
            using (var aes = new AesGcm(_key, tag.Length))
                aes.Encrypt(nonce, plain, cipher, tag);
            CryptographicOperations.ZeroMemory(plain);
            string envelope = JsonSerializer.Serialize(new
            {
                nonce = Base64Url(nonce),
                ciphertext = Base64Url(cipher),
                tag = Base64Url(tag)
            });
            await WriteResponseAsync(stream, 200, "application/netpulse+json", envelope, cancellationToken);
        }
    }

    private bool Authorize(string method, string path, Dictionary<string, string> headers)
    {
        if (!headers.TryGetValue("X-NetPulse-Time", out string? timeText) ||
            !headers.TryGetValue("X-NetPulse-Nonce", out string? nonce) ||
            !headers.TryGetValue("X-NetPulse-Signature", out string? supplied) ||
            !long.TryParse(timeText, out long unixTime) || nonce.Length is < 16 or > 128)
            return false;
        DateTime timestamp;
        try { timestamp = DateTimeOffset.FromUnixTimeSeconds(unixTime).UtcDateTime; }
        catch (ArgumentOutOfRangeException) { return false; }
        DateTime now = DateTime.UtcNow;
        if ((now - timestamp).Duration() > AllowedClockSkew || !RememberNonce(nonce, now))
            return false;
        byte[] expected = HMACSHA256.HashData(
            _key, Encoding.UTF8.GetBytes($"{method}\n{path}\n{timeText}\n{nonce}"));
        byte[] actual;
        try { actual = FromBase64Url(supplied); } catch (FormatException) { return false; }
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private bool RememberNonce(string nonce, DateTime now)
    {
        lock (_nonceGate)
        {
            foreach (string expired in _usedNonces.Where(item => now - item.Value > AllowedClockSkew).Select(item => item.Key).ToArray())
                _usedNonces.Remove(expired);
            return _usedNonces.TryAdd(nonce, now);
        }
    }

    private async Task DiscoveryLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _discovery is not null)
        {
            UdpReceiveResult request;
            try { request = await _discovery.ReceiveAsync(cancellationToken); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            if (Encoding.ASCII.GetString(request.Buffer) != "NETPULSE_DISCOVER_V1")
                continue;
            byte[] response = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                name = Environment.MachineName,
                protocol = 1,
                port = Port
            }));
            await _discovery.SendAsync(response, request.RemoteEndPoint, cancellationToken);
        }
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string contentType, string body, CancellationToken token)
    {
        byte[] payload = Encoding.UTF8.GetBytes(body);
        string reason = status switch { 200 => "OK", 401 => "Unauthorized", 405 => "Method Not Allowed", _ => "Error" };
        byte[] header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(payload, token);
    }

    internal static string CreatePairingSecret() => Base64Url(RandomNumberGenerator.GetBytes(32));

    internal static string BuildPairingUri(int port, string secret) =>
        $"netpulse://pair?host={Uri.EscapeDataString(PreferredLanAddress())}&port={port}&key={Uri.EscapeDataString(secret)}&v=1";

    private static string PreferredLanAddress()
    {
        foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces().Where(item => item.OperationalStatus == OperationalStatus.Up))
        foreach (UnicastIPAddressInformation address in adapter.GetIPProperties().UnicastAddresses)
            if (address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
                return address.Address.ToString();
        return "127.0.0.1";
    }

    internal static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    internal static byte[] FromBase64Url(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
