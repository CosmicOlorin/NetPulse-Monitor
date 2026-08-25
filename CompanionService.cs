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
    long? UploadBytesPerSecond,
    long? DownloadBytesPerSecond,
    int? UnreadSmsCount);

internal sealed record CompanionSmsAction(string Stack, string Index, int PageNumber, string Folder, bool Unread);
internal sealed record CompanionSmsSend(string PhoneNumber, string Content);
internal sealed record CompanionLockRequest(int[] Bands, string Earfcn, string Pci, string? CellId);

internal sealed class CompanionService : IAsyncDisposable
{
    public const int DefaultPort = 45831;
    private const int DiscoveryPort = 45832;
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    private readonly Func<CompanionSnapshot> _snapshotFactory;
    private readonly RouterMonitor? _routerMonitor;
    private readonly LteCellHistoryStore? _cellHistory;
    private readonly Func<IReadOnlyDictionary<string, string>> _contactsFactory;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _nonceGate = new();
    private readonly Dictionary<string, DateTime> _usedNonces = new(StringComparer.Ordinal);
    private CancellationTokenSource? _cancellation;
    private TcpListener? _listener;
    private UdpClient? _discovery;
    private Task? _acceptTask;
    private Task? _discoveryTask;
    private byte[] _key = [];
    private byte[] _downloadToken = [];

    public bool IsRunning => _listener is not null;
    public int Port { get; private set; } = DefaultPort;
    public string PairingUri { get; private set; } = "";

    public CompanionService(Func<CompanionSnapshot> snapshotFactory, RouterMonitor routerMonitor, LteCellHistoryStore cellHistory, Func<IReadOnlyDictionary<string, string>> contactsFactory)
    {
        _snapshotFactory = snapshotFactory;
        _routerMonitor = routerMonitor;
        _cellHistory = cellHistory;
        _contactsFactory = contactsFactory;
    }

    internal CompanionService(Func<CompanionSnapshot> snapshotFactory)
    {
        _snapshotFactory = snapshotFactory;
        _contactsFactory = () => new Dictionary<string, string>();
    }

    public void Start(int port, string pairingSecret)
    {
        if (IsRunning)
            return;
        if (port is < 1024 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        ArgumentException.ThrowIfNullOrWhiteSpace(pairingSecret);

        Port = port;
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(pairingSecret));
        _downloadToken = HMACSHA256.HashData(
            _key,
            Encoding.UTF8.GetBytes("netpulse-android-download-v1"));
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
        CryptographicOperations.ZeroMemory(_downloadToken);
        _downloadToken = [];
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
            if (request.Length < 2 || request[0] is not ("GET" or "POST"))
            {
                await WriteResponseAsync(stream, 405, "application/json", "{\"error\":\"method_not_allowed\"}", cancellationToken);
                return;
            }
            string method = request[0];
            string requestTarget = request[1];
            string path = requestTarget.Split('?', 2)[0];
            string body = "";
            if (method == "POST")
            {
                if (!headers.TryGetValue("Content-Length", out string? lengthText) ||
                    !int.TryParse(lengthText, out int length) || length < 0 || length > 64 * 1024)
                {
                    await WriteResponseAsync(stream, 400, "application/json", "{\"error\":\"invalid_body\"}", cancellationToken);
                    return;
                }
                char[] content = new char[length];
                int read = 0;
                while (read < length)
                {
                    int count = await reader.ReadAsync(content.AsMemory(read, length - read), cancellationToken);
                    if (count == 0) break;
                    read += count;
                }
                body = new string(content, 0, read);
            }
            bool authorized = Authorize(method, path, headers, body);
            bool downloadAuthorized = AuthorizeAndroidDownload(method, requestTarget);
            if (!authorized && !downloadAuthorized)
            {
                await WriteResponseAsync(stream, 401, "application/json", "{\"error\":\"unauthorized\"}", cancellationToken);
                return;
            }

            if (path == "/v1/info")
            {
                string info = JsonSerializer.Serialize(new { name = "NetPulse Monitor", protocol = 1, port = Port });
                await WriteResponseAsync(stream, 200, "application/json", info, cancellationToken);
                return;
            }
            if (path == "/download/android")
            {
                string apkPath = Path.Combine(AppContext.BaseDirectory, "NetPulse-Monitor-Companion-Android.apk");
                if (!File.Exists(apkPath))
                {
                    await WriteResponseAsync(stream, 404, "application/json", "{\"error\":\"android_app_not_installed\"}", cancellationToken);
                    return;
                }
                await WriteFileResponseAsync(stream, apkPath, cancellationToken);
                return;
            }
            if (path == "/v1/app/android")
            {
                string apkPath = Path.Combine(AppContext.BaseDirectory, "NetPulse-Monitor-Companion-Android.apk");
                if (!File.Exists(apkPath))
                {
                    await WriteResponseAsync(stream, 404, "application/json", "{\"error\":\"android_app_not_installed\"}", cancellationToken);
                    return;
                }
                var apk = new FileInfo(apkPath);
                string sha256;
                await using (FileStream source = File.OpenRead(apkPath))
                    sha256 = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken));
                string info = JsonSerializer.Serialize(new
                {
                    displayVersion = "1.0.19",
                    versionCode = 11,
                    size = apk.Length,
                    sha256,
                    downloadPath = "/download/android"
                });
                await WriteResponseAsync(stream, 200, "application/json", info, cancellationToken);
                return;
            }
            try
            {
                object result = path switch
                {
                    "/v1/snapshot" when method == "GET" => _snapshotFactory(),
                    "/v1/sms" when method == "GET" => await ReadSmsAsync(cancellationToken),
                    "/v1/contacts" when method == "GET" => _contactsFactory(),
                    "/v1/devices" when method == "GET" => await ReadConnectedDevicesAsync(cancellationToken),
                    "/v1/sms/unread" when method == "POST" => await SetSmsUnreadAsync(body, cancellationToken),
                    "/v1/sms/delete" when method == "POST" => await DeleteSmsAsync(body, cancellationToken),
                    "/v1/sms/send" when method == "POST" => await SendSmsAsync(body, cancellationToken),
                    "/v1/lte/history" when method == "GET" => RequireHistory().GetHistoryRecommendations(),
                    "/v1/lte/lock" when method == "POST" => await ApplyLockAsync(body, cancellationToken),
                    "/v1/lte/automatic" when method == "POST" => await RestoreAutomaticAsync(cancellationToken),
                    "/v1/router/reboot" when method == "POST" => await RebootRouterAsync(cancellationToken),
                    _ => throw new FileNotFoundException()
                };
                await WriteEncryptedResponseAsync(stream, result, cancellationToken);
            }
            catch (FileNotFoundException)
            {
                await WriteResponseAsync(stream, 404, "application/json", "{\"error\":\"not_found\"}", cancellationToken);
            }
            catch (Exception ex)
            {
                await WriteResponseAsync(stream, 409, "application/json", JsonSerializer.Serialize(new { error = "operation_failed", message = ex.Message }), cancellationToken);
            }
        }
    }

    private async Task WriteEncryptedResponseAsync(NetworkStream stream, object value, CancellationToken cancellationToken)
    {
            byte[] plain = JsonSerializer.SerializeToUtf8Bytes(value);
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

    private async Task<object> ReadSmsAsync(CancellationToken token) =>
        (await RequireRouter().ReadSmsTimelineAsync(token)).Select(message => new
        {
            message.Stack, message.Index, message.PageNumber, message.Address, message.Content,
            message.TimeText, message.Timestamp, Folder = message.Folder.ToString(), message.IsUnread, message.Identity
        }).ToArray();

    private async Task<object> ReadConnectedDevicesAsync(CancellationToken token) =>
        (await RequireRouter().ReadConnectedDevicesAsync(token)).Select(device => new
        {
            device.Name,
            device.IpAddress,
            device.MacAddress,
            device.ConnectionType,
            device.IsActive
        }).ToArray();

    private async Task<object> SetSmsUnreadAsync(string body, CancellationToken token)
    {
        CompanionSmsAction action = JsonSerializer.Deserialize<CompanionSmsAction>(body) ?? throw new InvalidDataException("Invalid SMS action.");
        await SerializedAsync(() => RequireRouter().SetSmsUnreadAsync(action.Stack, action.Index, action.PageNumber, action.Unread, token), token);
        return new { ok = true };
    }

    private async Task<object> DeleteSmsAsync(string body, CancellationToken token)
    {
        CompanionSmsAction action = JsonSerializer.Deserialize<CompanionSmsAction>(body) ?? throw new InvalidDataException("Invalid SMS action.");
        if (!Enum.TryParse(action.Folder, true, out RouterSmsFolder folder)) throw new InvalidDataException("Invalid SMS folder.");
        await SerializedAsync(() => RequireRouter().DeleteSmsAsync(folder, action.Stack, action.Index, action.PageNumber, token), token);
        return new { ok = true };
    }

    private async Task<object> SendSmsAsync(string body, CancellationToken token)
    {
        CompanionSmsSend request = JsonSerializer.Deserialize<CompanionSmsSend>(body) ?? throw new InvalidDataException("Invalid SMS request.");
        if (string.IsNullOrWhiteSpace(request.PhoneNumber) || string.IsNullOrWhiteSpace(request.Content)) throw new InvalidDataException("Phone number and message are required.");
        await SerializedAsync(() => RequireRouter().SendSmsAsync(request.PhoneNumber.Trim(), request.Content, token), token);
        return new { ok = true };
    }

    private async Task<object> ApplyLockAsync(string body, CancellationToken token)
    {
        CompanionLockRequest request = JsonSerializer.Deserialize<CompanionLockRequest>(body) ?? throw new InvalidDataException("Invalid LTE lock request.");
        int[] bands = request.Bands.Distinct().ToArray();
        if (bands.Length != 1 || bands[0] is < 1 or > 261) throw new InvalidDataException("Exactly one valid PCell band is required; the router selects SCells automatically.");
        var target = new RouterCellLockTarget { Bands = bands, Earfcn = request.Earfcn.Trim(), Pci = request.Pci.Trim(), CellId = request.CellId?.Trim() };
        await SerializedAsync(() => RequireRouter().ApplyCellAndBandLockAsync(target, token), token);
        return new { ok = true };
    }

    private async Task<object> RestoreAutomaticAsync(CancellationToken token)
    {
        await SerializedAsync(() => RequireRouter().RestoreAutomaticSelectionAsync(token), token);
        return new { ok = true };
    }

    private async Task<object> RebootRouterAsync(CancellationToken token)
    {
        await SerializedAsync(() => RequireRouter().RebootRouterAsync(token), token);
        return new { ok = true };
    }

    private async Task SerializedAsync(Func<Task> action, CancellationToken token)
    {
        await _operationGate.WaitAsync(token);
        try { await action(); } finally { _operationGate.Release(); }
    }

    private RouterMonitor RequireRouter() => _routerMonitor ?? throw new NotSupportedException("Router controls are not available.");
    private LteCellHistoryStore RequireHistory() => _cellHistory ?? throw new NotSupportedException("LTE history is not available.");

    private bool Authorize(string method, string path, Dictionary<string, string> headers, string body)
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
        string payload = $"{method}\n{path}\n{timeText}\n{nonce}";
        if (body.Length > 0)
            payload += "\n" + Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        byte[] expected = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));
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

    private bool AuthorizeAndroidDownload(string method, string requestTarget)
    {
        if (method != "GET" || _downloadToken.Length == 0)
            return false;
        string[] target = requestTarget.Split('?', 2);
        if (target[0] != "/download/android" || target.Length != 2)
            return false;
        string? supplied = target[1]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(part => part.Length == 2 && part[0] == "token")
            .Select(part => Uri.UnescapeDataString(part[1]))
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(supplied))
            return false;
        byte[] actual;
        try { actual = FromBase64Url(supplied); }
        catch (FormatException) { return false; }
        return actual.Length == _downloadToken.Length &&
               CryptographicOperations.FixedTimeEquals(actual, _downloadToken);
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
        string reason = status switch { 200 => "OK", 401 => "Unauthorized", 404 => "Not Found", 405 => "Method Not Allowed", _ => "Error" };
        byte[] header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(payload, token);
    }

    private static async Task WriteFileResponseAsync(NetworkStream stream, string path, CancellationToken token)
    {
        var file = new FileInfo(path);
        byte[] header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: application/vnd.android.package-archive\r\nContent-Disposition: attachment; filename=\"NetPulse-Monitor-Companion-Android.apk\"\r\nContent-Length: {file.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
        await stream.WriteAsync(header, token);
        await using var source = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(stream, 64 * 1024, token);
    }

    internal static string CreatePairingSecret() => Base64Url(RandomNumberGenerator.GetBytes(32));

    internal static string BuildPairingUri(int port, string secret) =>
        $"netpulse://pair?host={Uri.EscapeDataString(PreferredLanAddress())}&port={port}&key={Uri.EscapeDataString(secret)}&v=1";

    internal static string BuildAndroidDownloadUri(int port, string pairingSecret)
    {
        byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes(pairingSecret));
        try
        {
            byte[] token = HMACSHA256.HashData(
                key,
                Encoding.UTF8.GetBytes("netpulse-android-download-v1"));
            return $"http://{PreferredLanAddress()}:{port}/download/android?token={Uri.EscapeDataString(Base64Url(token))}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static string PreferredLanAddress()
    {
        NetworkInterface[] candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                              adapter.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet &&
                              adapter.GetIPProperties().GatewayAddresses.Any(gateway =>
                                  gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                                  !gateway.Address.Equals(IPAddress.Any)))
            .OrderBy(adapter => adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1)
            .ThenByDescending(adapter => adapter.Speed)
            .ToArray();
        foreach (NetworkInterface adapter in candidates)
        foreach (UnicastIPAddressInformation address in adapter.GetIPProperties().UnicastAddresses)
            if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(address.Address) &&
                !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
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
