namespace NetPulse.Companion;

public sealed record PairingProfile(string Host, int Port, string Secret, int ProtocolVersion)
{
    public Uri BaseAddress => new($"http://{Host}:{Port}/");

    public static PairingProfile Parse(string pairingUri)
    {
        if (!Uri.TryCreate(pairingUri, UriKind.Absolute, out Uri? uri) ||
            !uri.Scheme.Equals("netpulse", StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("pair", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("This is not a NetPulse pairing code.");

        var query = ParseQuery(uri.Query);
        if (!query.TryGetValue("host", out string? host) ||
            !query.TryGetValue("key", out string? secret) ||
            !query.TryGetValue("port", out string? portText) ||
            !int.TryParse(portText, out int port) || port is < 1024 or > 65535 ||
            string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(secret))
            throw new FormatException("The NetPulse pairing code is incomplete.");
        int version = query.TryGetValue("v", out string? versionText) &&
                      int.TryParse(versionText, out int parsed) ? parsed : 1;
        if (version != 1)
            throw new NotSupportedException($"NetPulse companion protocol {version} is not supported.");
        return new PairingProfile(host, port, secret, version);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            if (parts.Length == 2)
                values[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
        }
        return values;
    }
}
