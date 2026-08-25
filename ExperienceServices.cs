namespace NetPulseMonitor;

internal sealed record ConnectionHealthAssessment(
    int Score,
    string Rating,
    string Summary,
    IReadOnlyList<string> Factors);

internal static class ConnectionHealthEvaluator
{
    public static ConnectionHealthAssessment Evaluate(
        MonitorSnapshot monitor,
        RouterTelemetry router,
        DiagnosticResult? diagnostics,
        bool includeLteRadio = true)
    {
        if (!monitor.IsOnline)
        {
            return new ConnectionHealthAssessment(
                0,
                "Offline",
                "Internet connectivity is currently unavailable.",
                ["Confirmed monitoring state: offline"]);
        }

        int score = 100;
        var factors = new List<string>();
        if (monitor.PacketLossPercent >= 10)
        {
            score -= 35;
            factors.Add($"High packet loss ({monitor.PacketLossPercent:0.#}%)");
        }
        else if (monitor.PacketLossPercent >= 2)
        {
            score -= 18;
            factors.Add($"Packet loss is elevated ({monitor.PacketLossPercent:0.#}%)");
        }
        else if (monitor.PacketLossPercent > 0)
        {
            score -= 6;
            factors.Add($"Minor packet loss ({monitor.PacketLossPercent:0.#}%)");
        }

        if (monitor.CurrentPingMs is >= 250)
        {
            score -= 24;
            factors.Add($"Very high latency ({monitor.CurrentPingMs} ms)");
        }
        else if (monitor.CurrentPingMs is >= 120)
        {
            score -= 14;
            factors.Add($"High latency ({monitor.CurrentPingMs} ms)");
        }
        else if (monitor.CurrentPingMs is >= 70)
        {
            score -= 6;
            factors.Add($"Moderate latency ({monitor.CurrentPingMs} ms)");
        }

        if (monitor.JitterMs >= 80)
        {
            score -= 20;
            factors.Add($"Very unstable latency ({monitor.JitterMs:0.#} ms jitter)");
        }
        else if (monitor.JitterMs >= 30)
        {
            score -= 10;
            factors.Add($"Noticeable jitter ({monitor.JitterMs:0.#} ms)");
        }

        if (monitor.AvailabilityPercent < 95)
        {
            score -= 20;
            factors.Add($"Low session availability ({monitor.AvailabilityPercent:0.###}%)");
        }
        else if (monitor.AvailabilityPercent < 99)
        {
            score -= 8;
            factors.Add($"Availability is below 99% ({monitor.AvailabilityPercent:0.###}%)");
        }

        if (includeLteRadio && router.IsConnected && router.RsrpDbm is <= -110)
        {
            score -= 10;
            factors.Add($"Weak LTE reference signal ({router.RsrpDbm:0.#} dBm RSRP)");
        }
        if (includeLteRadio && router.IsConnected && router.RsrqDb is <= -15)
        {
            score -= 8;
            factors.Add($"Poor LTE signal quality ({router.RsrqDb:0.#} dB RSRQ)");
        }
        if (includeLteRadio && router.IsConnected && router.SnrDb is < 3)
        {
            score -= 8;
            factors.Add($"Low LTE signal-to-noise ratio ({router.SnrDb:0.#} dB)");
        }

        if (diagnostics is not null)
        {
            if (!diagnostics.GatewayPing.EndsWith(" ms", StringComparison.OrdinalIgnoreCase))
            {
                score -= 10;
                factors.Add("The local gateway did not answer normally");
            }
            if (!diagnostics.DnsLookup.EndsWith(" ms", StringComparison.OrdinalIgnoreCase))
            {
                score -= 8;
                factors.Add("DNS resolution is unavailable or failing");
            }
        }

        score = Math.Clamp(score, 0, 100);
        string rating = score switch
        {
            >= 90 => "Excellent",
            >= 75 => "Good",
            >= 55 => "Fair",
            >= 35 => "Poor",
            _ => "Critical"
        };
        string summary = factors.Count == 0
            ? "Stable connection with no significant issue detected."
            : factors[0] + (factors.Count > 1 ? $" and {factors.Count - 1} more factor(s)." : ".");
        return new ConnectionHealthAssessment(score, rating, summary, factors);
    }
}

internal sealed record TroubleshootingAssessment(
    string Severity,
    string Headline,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> Actions);

internal static class TroubleshootingAdvisor
{
    public static TroubleshootingAssessment Analyze(
        MonitorSnapshot monitor,
        RouterTelemetry router,
        DiagnosticResult? diagnostics,
        SpeedTestResult? speed)
    {
        var findings = new List<string>();
        var actions = new List<string>();
        string severity = "Healthy";

        if (!monitor.IsOnline)
        {
            severity = "Critical";
            findings.Add("The configured internet target is offline.");
            actions.Add("Check the router registration and local gateway before changing LTE cells.");
        }
        if (diagnostics is not null &&
            !diagnostics.GatewayPing.EndsWith(" ms", StringComparison.OrdinalIgnoreCase))
        {
            severity = "Critical";
            findings.Add("The local gateway is not responding normally.");
            actions.Add("Check the local connection and restart the router if needed.");
        }
        if (monitor.PacketLossPercent >= 2)
        {
            severity = severity == "Critical" ? severity : "Warning";
            findings.Add($"Packet loss is {monitor.PacketLossPercent:0.#}%.");
            actions.Add("Compare loss before and after a cell/band change; signal bars alone are not enough.");
        }
        if (router.IsConnected && router.RsrqDb is <= -15)
        {
            severity = severity == "Critical" ? severity : "Warning";
            findings.Add("LTE quality indicates interference or cell load.");
            actions.Add("Test another measured PCell profile during the same time period.");
        }
        if (speed?.DownloadMbps is < 5 && router.IsConnected)
        {
            severity = severity == "Critical" ? severity : "Warning";
            findings.Add($"The latest measured download is only {speed.DownloadMbps:0.##} Mbps.");
            actions.Add("Run a controlled profile experiment rather than locking by signal strength.");
        }
        if (diagnostics is not null &&
            !diagnostics.DnsLookup.EndsWith(" ms", StringComparison.OrdinalIgnoreCase))
        {
            severity = severity == "Critical" ? severity : "Warning";
            findings.Add("DNS resolution is failing independently of ping monitoring.");
            actions.Add("Try a known resolver and include the diagnostic result in ISP evidence.");
        }
        if (findings.Count == 0)
        {
            findings.Add("No immediate local-path, internet, or LTE-quality fault was detected.");
            actions.Add("Keep monitoring; use the timeline if the issue is intermittent.");
        }

        string headline = severity switch
        {
            "Critical" => "Connection failure needs attention",
            "Warning" => "A measurable connection issue was found",
            _ => "No significant fault detected"
        };
        return new TroubleshootingAssessment(severity, headline, findings, actions);
    }
}

internal sealed record SmsConversation(
    string Address,
    string DisplayName,
    DateTime? LastTimestamp,
    int UnreadCount,
    IReadOnlyList<RouterSmsMessage> Messages);

internal static class SmsConversationBuilder
{
    private static readonly IReadOnlyDictionary<string, string> CountryCallingCodes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GR"] = "30", ["CY"] = "357", ["US"] = "1", ["CA"] = "1",
            ["GB"] = "44", ["IE"] = "353", ["PT"] = "351", ["DE"] = "49",
            ["AT"] = "43", ["CH"] = "41", ["IT"] = "39", ["NL"] = "31",
            ["FR"] = "33", ["ES"] = "34", ["PL"] = "48", ["RO"] = "40",
            ["BG"] = "359", ["FI"] = "358", ["EE"] = "372", ["LV"] = "371",
            ["LT"] = "370", ["TR"] = "90", ["JP"] = "81", ["CN"] = "86",
            ["IN"] = "91", ["BR"] = "55", ["ZA"] = "27", ["AU"] = "61",
            ["NZ"] = "64", ["SE"] = "46", ["NO"] = "47", ["DK"] = "45",
            ["BE"] = "32", ["LU"] = "352", ["CZ"] = "420", ["SK"] = "421",
            ["HU"] = "36", ["SI"] = "386", ["HR"] = "385", ["RS"] = "381",
            ["AL"] = "355", ["MK"] = "389", ["MT"] = "356", ["IS"] = "354"
        };

    public static IReadOnlyList<SmsConversation> Build(
        IEnumerable<RouterSmsMessage> messages,
        IReadOnlyDictionary<string, string> contacts,
        string? search = null,
        string? countryCode = null)
    {
        return messages
            .Where(IsConversationMessage)
            .Where(message => MatchesSearch(
                message, contacts, search, countryCode))
            .GroupBy(
                message => NormalizeAddress(message.Address, countryCode),
                StringComparer.Ordinal)
            .Select(group =>
            {
                RouterSmsMessage[] ordered = group
                    .OrderBy(message => message.Timestamp ?? DateTime.MinValue)
                    .ToArray();
                string displayName = contacts.TryGetValue(group.Key, out string? name)
                    ? name
                    : ordered[^1].Address;
                return new SmsConversation(
                    group.Key,
                    displayName,
                    group.Max(message => message.Timestamp),
                    group.Count(message =>
                        message.Folder == RouterSmsFolder.Inbox && message.IsUnread),
                    ordered);
            })
            .OrderByDescending(conversation => conversation.LastTimestamp ?? DateTime.MinValue)
            .ThenBy(conversation => conversation.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<RouterSmsMessage> MessagesForAddress(
        IEnumerable<RouterSmsMessage> messages,
        string address,
        string? countryCode = null)
    {
        string normalized = NormalizeAddress(address, countryCode);
        return messages
            .Where(IsConversationMessage)
            .Where(message => string.Equals(
                NormalizeAddress(message.Address, countryCode),
                normalized,
                StringComparison.Ordinal))
            .OrderBy(message => message.Timestamp ?? DateTime.MinValue)
            .ThenBy(message => message.Identity, StringComparer.Ordinal)
            .ToArray();
    }

    public static bool MatchesSearch(
        RouterSmsMessage message,
        IReadOnlyDictionary<string, string> contacts,
        string? search,
        string? countryCode = null)
    {
        string term = search?.Trim() ?? "";
        if (term.Length == 0)
            return true;

        string normalizedAddress = NormalizeAddress(message.Address, countryCode);
        string normalizedTerm = NormalizeAddress(term, countryCode);
        return message.Address.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               (normalizedTerm.Length > 0 &&
                normalizedAddress.Contains(normalizedTerm, StringComparison.Ordinal)) ||
               message.Content.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               (contacts.TryGetValue(normalizedAddress, out string? name) &&
                name.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsConversationMessage(RouterSmsMessage message) =>
        message.Folder is RouterSmsFolder.Inbox or RouterSmsFolder.Sent;

    public static string NormalizeAddress(string address, string? countryCode = null)
    {
        string trimmed = address.Trim();
        string digits = new(trimmed.Where(char.IsDigit).ToArray());
        bool international = trimmed.StartsWith('+') ||
                             digits.StartsWith("00", StringComparison.Ordinal);
        if (digits.StartsWith("00", StringComparison.Ordinal) && digits.Length > 2)
            digits = digits[2..];
        if (digits.Length == 0)
            return trimmed.ToUpperInvariant();
        if (international || string.IsNullOrWhiteSpace(countryCode) ||
            !CountryCallingCodes.TryGetValue(countryCode.Trim(), out string? callingCode))
            return digits;
        if (digits.StartsWith(callingCode, StringComparison.Ordinal) &&
            digits.Length >= callingCode.Length + 7)
            return digits;

        string national = digits;
        if (national.StartsWith('0') && national.Length > 7)
            national = national[1..];
        return callingCode + national;
    }
}

internal sealed record CellExperimentResult(
    LteCellRecommendation Recommendation,
    double Score,
    string Explanation);

internal static class CellExperimentEvaluator
{
    public static IReadOnlyList<CellExperimentResult> Rank(
        IEnumerable<LteCellRecommendation> candidates)
    {
        LteCellRecommendation[] values = candidates.ToArray();
        if (values.Length == 0)
            return [];
        LteRecommendationScoring.AssignScores(values);
        return values
            .OrderByDescending(item => item.WeightedScore)
            .Select(item => new CellExperimentResult(
                item,
                item.WeightedScore,
                $"Rank is 50% controlled reliability, 25% download and " +
                $"25% upload; missing evidence contributes zero. RF is separate: " +
                $"50% SINR, 35% RSRQ and 15% RSRP; " +
                $"SINR {item.AverageSinrDb?.ToString("0.#") ?? "--"} dB, " +
                $"RSRQ {item.AverageRsrqDb?.ToString("0.#") ?? "--"} dB, " +
                $"RSRP {item.AverageRsrpDbm?.ToString("0.#") ?? "--"} dBm"))
            .ToArray();
    }
}

internal static class ReleaseVersionComparer
{
    public static bool IsNewer(string candidate, string current)
    {
        string cleanCandidate = candidate.Trim().TrimStart('v', 'V').Split('-', 2)[0];
        string cleanCurrent = current.Trim().TrimStart('v', 'V').Split('-', 2)[0];
        return Version.TryParse(cleanCandidate, out Version? candidateVersion) &&
               Version.TryParse(cleanCurrent, out Version? currentVersion) &&
               candidateVersion > currentVersion;
    }
}

internal sealed class ConnectionTimelineTracker
{
    private readonly object _sync = new();
    private RouterTelemetry? _previous;

    public IReadOnlyList<MonitorEvent> Observe(RouterTelemetry current)
    {
        lock (_sync)
        {
            RouterTelemetry? previous = _previous;
            _previous = current;
            if (previous is null)
                return [];

            var events = new List<MonitorEvent>();
            if (previous.IsConnected != current.IsConnected)
            {
                events.Add(new MonitorEvent
                {
                    Timestamp = current.Timestamp,
                    Kind = "LTE",
                    Message = current.IsConnected
                        ? "Router LTE service connected"
                        : "Router LTE service disconnected"
                });
            }

            string previousIdentity = Identity(previous);
            string currentIdentity = Identity(current);
            if (!string.Equals(previousIdentity, currentIdentity, StringComparison.Ordinal) &&
                current.IsConnected)
            {
                events.Add(new MonitorEvent
                {
                    Timestamp = current.Timestamp,
                    Kind = "LTE CHANGE",
                    Message = $"Serving profile changed: {currentIdentity}"
                });
            }
            return events;
        }
    }

    private static string Identity(RouterTelemetry telemetry) =>
        $"{telemetry.Band}; PCell {telemetry.PrimaryBand}; " +
        $"EARFCN {telemetry.Earfcn}; PCI {telemetry.Pci}; CID {telemetry.CellId}";
}
