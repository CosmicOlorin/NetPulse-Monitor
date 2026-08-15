using System.Security.Cryptography;
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
    int? UnreadSmsCount);

public sealed class CompanionClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly byte[] _key;

    public CompanionClient(PairingProfile profile, HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = profile.BaseAddress;
        _http.Timeout = TimeSpan.FromSeconds(8);
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(profile.Secret));
    }

    public async Task<MobileSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        const string path = "/v1/snapshot";
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string nonce = Base64Url(RandomNumberGenerator.GetBytes(18));
        string signature = Base64Url(HMACSHA256.HashData(
            _key, Encoding.UTF8.GetBytes($"GET\n{path}\n{timestamp}\n{nonce}")));
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-NetPulse-Time", timestamp);
        request.Headers.Add("X-NetPulse-Nonce", nonce);
        request.Headers.Add("X-NetPulse-Signature", signature);
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using JsonDocument envelope = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        byte[] encryptedNonce = FromBase64Url(envelope.RootElement.GetProperty("nonce").GetString()!);
        byte[] ciphertext = FromBase64Url(envelope.RootElement.GetProperty("ciphertext").GetString()!);
        byte[] tag = FromBase64Url(envelope.RootElement.GetProperty("tag").GetString()!);
        byte[] plain = new byte[ciphertext.Length];
        using (var aes = new AesGcm(_key, tag.Length))
            aes.Decrypt(encryptedNonce, ciphertext, tag, plain);
        try
        {
            return JsonSerializer.Deserialize<MobileSnapshot>(plain) ??
                   throw new InvalidDataException("The NetPulse snapshot was empty.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
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
        CryptographicOperations.ZeroMemory(_key);
    }
}
