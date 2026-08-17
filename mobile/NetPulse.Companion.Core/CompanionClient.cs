using System.Security.Cryptography;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace NetPulse.Companion;

public sealed record MobileSnapshot(
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

public sealed record AndroidAppRelease(
    string DisplayVersion,
    int VersionCode,
    long Size,
    string Sha256,
    string DownloadPath);

public sealed record MobileSmsMessage(string Stack, string Index, int PageNumber, string Address, string Content, string TimeText, DateTime? Timestamp, string Folder, bool IsUnread, string Identity)
{
    public string? ContactName { get; init; }
    public string DisplayAddress => string.IsNullOrWhiteSpace(ContactName) ? Address : $"{ContactName} · {Address}";
}
public sealed record MobileLteProfile(
    string Key, string Band, string PrimaryBand, string Earfcn, string Pci,
    string? CellId, string TimePeriod, double? AverageDownloadMbps,
    double? AverageUploadMbps, double? AveragePingMs,
    double? EstimatedCellLoadPercent, double DisconnectionsPerHour,
    string Confidence, TimeSpan PeriodConnectedTime, int PeriodDisconnections)
{
    public string IdentitySummary =>
        $"EARFCN {Display(Earfcn)}  ·  PCI {Display(Pci)}  ·  CID {Display(CellId)}";

    private static string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) || value == "-" ? "—" : value;
}

public sealed class CompanionClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly byte[] _key;
    private readonly SemaphoreSlim _routerOperationGate = new(1, 1);

    public CompanionClient(PairingProfile profile, HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = profile.BaseAddress;
        // TP-Link SMS and lock operations can legitimately take tens of seconds.
        // Individual live snapshots still use their own short deadline.
        _http.Timeout = TimeSpan.FromMinutes(2);
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(profile.Secret));
    }

    public async Task<MobileSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        return await SendEncryptedAsync<MobileSnapshot>(HttpMethod.Get, "/v1/snapshot", null, deadline.Token);
    }

    public async Task<List<MobileSmsMessage>> ReadSmsAsync(CancellationToken token = default)
    {
        List<MobileSmsMessage> messages = await SendEncryptedAsync<List<MobileSmsMessage>>(HttpMethod.Get, "/v1/sms", null, token);
        Dictionary<string, string> contacts = await SendEncryptedAsync<Dictionary<string, string>>(HttpMethod.Get, "/v1/contacts", null, token);
        var normalized = contacts
            .GroupBy(item => PhoneIdentity(item.Key), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
        return messages.Select(message => normalized.TryGetValue(PhoneIdentity(message.Address), out string? name)
            ? message with { ContactName = name }
            : message).ToList();
    }

    private static string PhoneIdentity(string value)
    {
        string digits = new(value.Where(char.IsDigit).ToArray());
        return digits.Length > 10 ? digits[^10..] : digits;
    }
    public Task SetSmsUnreadAsync(MobileSmsMessage sms, bool unread, CancellationToken token = default) => SendActionAsync("/v1/sms/unread", new { sms.Stack, sms.Index, sms.PageNumber, sms.Folder, Unread = unread }, token);
    public Task DeleteSmsAsync(MobileSmsMessage sms, CancellationToken token = default) => SendActionAsync("/v1/sms/delete", new { sms.Stack, sms.Index, sms.PageNumber, sms.Folder, Unread = false }, token);
    public Task SendSmsAsync(string phoneNumber, string content, CancellationToken token = default) => SendActionAsync("/v1/sms/send", new { PhoneNumber = phoneNumber, Content = content }, token);
    public async Task<List<MobileLteProfile>> ReadLteHistoryAsync(CancellationToken token = default)
    {
        List<MobileLteProfile> profiles = await SendEncryptedAsync<List<MobileLteProfile>>(HttpMethod.Get, "/v1/lte/history", null, token);
        return profiles
            .GroupBy(ProfileIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(MergePeriods)
            .OrderByDescending(profile => profile.PeriodConnectedTime)
            .ThenBy(profile => profile.Band, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ProfileIdentity(MobileLteProfile profile) => string.Join('|',
        NormalizeBand(profile.Band), NormalizeIdentity(profile.Earfcn),
        NormalizeIdentity(profile.Pci), NormalizeIdentity(profile.CellId));

    private static string NormalizeBand(string? value) => string.Join(" + ",
        (value ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.ToUpperInvariant()));

    private static string NormalizeIdentity(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim() == "-" ? "" : value.Trim().ToUpperInvariant();

    private static MobileLteProfile MergePeriods(IGrouping<string, MobileLteProfile> group)
    {
        MobileLteProfile first = group.First();
        TimeSpan connected = TimeSpan.FromTicks(group.Sum(item => item.PeriodConnectedTime.Ticks));
        int drops = group.Sum(item => item.PeriodDisconnections);
        return first with
        {
            Band = NormalizeBand(first.Band),
            TimePeriod = "All periods",
            PeriodConnectedTime = connected,
            PeriodDisconnections = drops,
            DisconnectionsPerHour = connected.TotalHours > 0 ? drops / connected.TotalHours : 0,
            AverageDownloadMbps = Average(group.Select(item => item.AverageDownloadMbps)),
            AverageUploadMbps = Average(group.Select(item => item.AverageUploadMbps)),
            AveragePingMs = WeightedAverage(group, item => item.AveragePingMs),
            EstimatedCellLoadPercent = WeightedAverage(group, item => item.EstimatedCellLoadPercent),
            Confidence = group.Select(item => item.Confidence).OrderByDescending(ConfidenceRank).FirstOrDefault() ?? "Gathering data"
        };
    }

    private static double? Average(IEnumerable<double?> values)
    {
        double[] measured = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return measured.Length == 0 ? null : measured.Average();
    }

    private static double? WeightedAverage(IEnumerable<MobileLteProfile> profiles, Func<MobileLteProfile, double?> selector)
    {
        var measured = profiles.Select(profile => new
        {
            Value = selector(profile),
            Weight = Math.Max(1d, profile.PeriodConnectedTime.TotalSeconds)
        }).Where(item => item.Value.HasValue).ToArray();
        return measured.Length == 0 ? null : measured.Sum(item => item.Value!.Value * item.Weight) / measured.Sum(item => item.Weight);
    }

    private static int ConfidenceRank(string? confidence) => confidence?.ToLowerInvariant() switch
    {
        "high" => 5,
        "medium" => 4,
        "basic" => 3,
        "needs speed test" => 2,
        "gathering data" => 1,
        _ => 0
    };
    public Task ApplyLteLockAsync(int[] bands, string earfcn, string pci, string? cellId, CancellationToken token = default) => SendActionAsync("/v1/lte/lock", new { Bands = bands, Earfcn = earfcn, Pci = pci, CellId = cellId }, token);
    public Task RestoreAutomaticAsync(CancellationToken token = default) => SendActionAsync("/v1/lte/automatic", new { Confirm = true }, token);
    public Task RebootRouterAsync(CancellationToken token = default) => SendActionAsync("/v1/router/reboot", new { Confirm = true }, token);

    private async Task SendActionAsync(string path, object body, CancellationToken token) => await SendEncryptedAsync<JsonElement>(HttpMethod.Post, path, body, token);

    private async Task<T> SendEncryptedAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        bool routerOperation = path != "/v1/snapshot";
        if (routerOperation) await _routerOperationGate.WaitAsync(cancellationToken);
        try
        {
        string bodyText = body is null ? "" : JsonSerializer.Serialize(body);
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string nonce = Base64Url(RandomNumberGenerator.GetBytes(18));
        string payload = $"{method.Method}\n{path}\n{timestamp}\n{nonce}";
        if (bodyText.Length > 0) payload += "\n" + Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(bodyText)));
        string signature = Base64Url(HMACSHA256.HashData(
            _key, Encoding.UTF8.GetBytes(payload)));
        using var request = new HttpRequestMessage(method, path);
        if (bodyText.Length > 0) request.Content = new StringContent(bodyText, Encoding.UTF8, "application/json");
        request.Headers.Add("X-NetPulse-Time", timestamp);
        request.Headers.Add("X-NetPulse-Nonce", nonce);
        request.Headers.Add("X-NetPulse-Signature", signature);
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string detail = response.ReasonPhrase ?? "Router operation failed";
            try
            {
                using JsonDocument error = JsonDocument.Parse(responseText);
                if (error.RootElement.TryGetProperty("message", out JsonElement message)) detail = message.GetString() ?? detail;
                else if (error.RootElement.TryGetProperty("error", out JsonElement code)) detail = code.GetString() ?? detail;
            }
            catch (JsonException) { }
            throw new HttpRequestException(detail, null, response.StatusCode);
        }
        using JsonDocument envelope = JsonDocument.Parse(responseText);
        byte[] encryptedNonce = FromBase64Url(envelope.RootElement.GetProperty("nonce").GetString()!);
        byte[] ciphertext = FromBase64Url(envelope.RootElement.GetProperty("ciphertext").GetString()!);
        byte[] tag = FromBase64Url(envelope.RootElement.GetProperty("tag").GetString()!);
        byte[] plain = new byte[ciphertext.Length];
        using (var aes = new AesGcm(_key, tag.Length))
            aes.Decrypt(encryptedNonce, ciphertext, tag, plain);
        try
        {
            return JsonSerializer.Deserialize<T>(plain) ?? throw new InvalidDataException("The NetPulse response was empty.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
        }
        finally
        {
            if (routerOperation) _routerOperationGate.Release();
        }
    }

    public async Task<AndroidAppRelease> ReadAndroidAppReleaseAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http.GetAsync("/v1/app/android", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AndroidAppRelease>(cancellationToken: cancellationToken) ??
               throw new InvalidDataException("The desktop did not return Android update information.");
    }

    public Uri GetAndroidDownloadUri(string downloadPath) => new(_http.BaseAddress!, downloadPath);

    public async Task DownloadAndroidUpdateAsync(AndroidAppRelease release, string destinationPath, IProgress<double>? progress = null, CancellationToken token = default)
    {
        using HttpResponseMessage response = await _http.GetAsync(release.DownloadPath, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? release.Size;
        await using Stream source = await response.Content.ReadAsStreamAsync(token);
        await using FileStream destination = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        byte[] buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, token)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), token);
            received += read;
            if (total > 0) progress?.Report(Math.Clamp(received / (double)total, 0, 1));
        }
        await destination.FlushAsync(token);
        string hash;
        await using (FileStream downloaded = File.OpenRead(destinationPath))
            hash = Convert.ToHexString(await SHA256.HashDataAsync(downloaded, token));
        if (!hash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(destinationPath);
            throw new InvalidDataException("The downloaded update failed its SHA-256 integrity check.");
        }
        progress?.Report(1);
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromBase64Url(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }

    public void Dispose()
    {
        _http.Dispose();
        _routerOperationGate.Dispose();
        CryptographicOperations.ZeroMemory(_key);
    }
}
