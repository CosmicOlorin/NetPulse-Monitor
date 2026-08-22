using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Reflection;

namespace NetPulseMonitor;

internal sealed class TpLinkMr600Provider :
    IRouterTelemetryProvider,
    IRouterCellLockProvider,
    IRouterMobileNetworkModeProvider,
    IRouterSmsProvider,
    IRouterConnectedDevicesProvider,
    IRouterRebootProvider
{
    private const string DefaultTokenId = "abcd";
    private const int InvalidCredentialsCode = 71233;
    private static readonly string ZeroStack = "0,0,0,0,0,0";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(6);

    private CookieContainer _cookies = new();
    private HttpClient? _client;
    private TpLinkCrypto? _crypto;
    private Uri? _routerUri;
    private string _password = "";
    private bool _allowSessionTakeover;
    private string _tokenId = DefaultTokenId;
    private string _lteInterfaceStack = "1,0,0,0,0,0";
    private string _lteLinkStack = "1,1,0,0,0,0";
    private RouterCapabilities _capabilities = new();
    private string _mobileNetworkModeOid = "";
    private string _mobileNetworkModeStack = "";
    private string _mobileNetworkModeField = "";

    private static readonly RouterMobileNetworkModeOption[] LegacyMobileNetworkModes =
    [
        new("3", "4G preferred (4G / 3G)"),
        new("2", "4G only"),
        new("1", "3G only")
    ];

    public bool IsConnected => _client is not null && _crypto is not null;

    public async Task<RouterCapabilities> ConnectAsync(
        RouterConnectionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        await DisconnectAsync(cancellationToken);

        _routerUri = NormalizeRouterUri(options.RouterUri);
        await ValidateLocalAddressAsync(_routerUri, cancellationToken);
        _password = options.Password;
        _allowSessionTakeover = options.AllowSessionTakeover;
        if (string.IsNullOrWhiteSpace(_password))
            throw new RouterAuthenticationException("Enter the TP-Link router password.");

        _cookies = new CookieContainer();
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            CookieContainer = _cookies,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.GZip |
                                     DecompressionMethods.Deflate
        };

        _client = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = _routerUri,
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = 1024 * 1024
        };
        string appVersion = Assembly.GetExecutingAssembly().GetName().Version?
            .ToString(3) ?? "1.0";
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"NetPulseMonitor/{appVersion} (Windows; TP-Link local telemetry)");
        _client.DefaultRequestHeaders.Referrer = _routerUri;
        // The MR600 login page sets this cookie in JavaScript. Non-browser
        // clients must set it explicitly or some firmware builds omit the
        // post-login TokenID value and reject the first cgi_gdpr request.
        _cookies.Add(_routerUri, new Cookie("loginErrorShow", "1", "/"));

        try
        {
            await LoginAsync(options.AllowSessionTakeover, cancellationToken);
            await DiscoverLteStacksAsync(cancellationToken);
            RouterTelemetry telemetry = await ReadTelemetryCoreAsync(cancellationToken);
            _capabilities = new RouterCapabilities
            {
                Model = telemetry.Model,
                HardwareVersion = telemetry.HardwareVersion,
                FirmwareVersion = telemetry.FirmwareVersion,
                SupportsLteTelemetry = telemetry.IsConnected
            };
            return _capabilities;
        }
        catch
        {
            await DisposeClientAsync();
            throw;
        }
    }

    public async Task<RouterTelemetry> ReadAsync(CancellationToken cancellationToken)
    {
        EnsureConnected();
        try
        {
            return await ReadTelemetryCoreAsync(cancellationToken);
        }
        catch (RouterAuthenticationException)
        {
            _crypto = null;
            // Live monitoring can be configured to own the MR600's single local
            // management slot. Setup tests remain opt-in, while the monitor
            // reconnects with its original takeover policy.
            await LoginAsync(_allowSessionTakeover, cancellationToken);
            await DiscoverLteStacksAsync(cancellationToken);
            return await ReadTelemetryCoreAsync(cancellationToken);
        }
    }

    public async Task<RouterLockState> ReadLockStateAsync(
        CancellationToken cancellationToken)
    {
        EnsureConnected();
        RouterAction[] actions =
        [
            new(1, "LTE_WAN_CFG", _lteLinkStack, ZeroStack,
                ["bandSelectSwitch", "bandSelectedMaskL", "bandSelectedMaskH"]),
            new(1, "LTE_CELL_LOCK", ZeroStack, ZeroStack,
                ["rfInfoCellIDLock", "rfInfoCellID", "rfInfoEARFCN", "rfInfoPCI"])
        ];
        CgiResponse response = await SendActionsAsync(actions, cancellationToken);
        CgiObject band = response.GetFirst(0);
        CgiObject cell = response.GetFirst(1);
        return new RouterLockState
        {
            BandSelectionEnabled = ParseInt(band.Get("bandSelectSwitch")) == 1,
            BandMaskLow = ParseMask(band.Get("bandSelectedMaskL")),
            BandMaskHigh = ParseMask(band.Get("bandSelectedMaskH")),
            CellLockEnabled = ParseInt(cell.Get("rfInfoCellIDLock")) == 1,
            CellId = NormalizeOptionalCellId(cell.Get("rfInfoCellID")),
            Earfcn = cell.Get("rfInfoEARFCN")?.Trim() ?? "",
            Pci = cell.Get("rfInfoPCI")?.Trim() ?? ""
        };
    }

    public async Task ApplyCellAndBandLockAsync(
        RouterCellLockTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        EnsureConnected();
        ValidateLockTarget(target);
        (int low, int high) = BuildBandMasks(target.Bands);
        var actions = new List<RouterAction>
        {
            new(2, "LTE_WAN_CFG", _lteLinkStack, ZeroStack,
                [
                    "bandSelectSwitch=1",
                    "bandSelectedMaskL=" + low.ToString(CultureInfo.InvariantCulture),
                    "bandSelectedMaskH=" + high.ToString(CultureInfo.InvariantCulture)
                ])
        };
        if (target.HasCellTarget)
        {
            string cid = target.CellId?.Trim() ?? "";
            actions.Add(
            new(2, "LTE_CELL_LOCK", ZeroStack, ZeroStack,
                [
                    "rfInfoCellIDLock=1",
                    "rfInfoCellID=" + cid,
                    "rfInfoEARFCN=" + target.Earfcn.Trim(),
                    "rfInfoPCI=" + target.Pci.Trim()
                ]));
        }
        else
        {
            // Some MR600 firmware exposes the live PCell channel but not PCI in
            // automatic mode. In that case optimize the measured band profile
            // and leave cell selection automatic instead of inventing a PCI.
            actions.Add(new RouterAction(
                2, "LTE_CELL_LOCK", ZeroStack, ZeroStack,
                ["rfInfoCellIDLock=0"]));
        }
        await SendActionsAsync(actions, cancellationToken);
    }

    public async Task RestoreLockStateAsync(
        RouterLockState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        EnsureConnected();
        RouterAction[] actions =
        [
            new(2, "LTE_WAN_CFG", _lteLinkStack, ZeroStack,
                [
                    "bandSelectSwitch=" + (state.BandSelectionEnabled ? "1" : "0"),
                    "bandSelectedMaskL=" + state.BandMaskLow.ToString(CultureInfo.InvariantCulture),
                    "bandSelectedMaskH=" + state.BandMaskHigh.ToString(CultureInfo.InvariantCulture)
                ]),
            BuildCellLockAction(state)
        ];
        await SendActionsAsync(actions, cancellationToken);
    }

    public async Task RestoreAutomaticSelectionAsync(
        CancellationToken cancellationToken)
    {
        EnsureConnected();
        RouterAction[] actions =
        [
            new(2, "LTE_WAN_CFG", _lteLinkStack, ZeroStack,
                ["bandSelectSwitch=0", "bandSelectedMaskL=0", "bandSelectedMaskH=0"]),
            new(2, "LTE_CELL_LOCK", ZeroStack, ZeroStack,
                ["rfInfoCellIDLock=0"])
        ];
        await SendActionsAsync(actions, cancellationToken);
    }

    public async Task RebootRouterAsync(CancellationToken cancellationToken)
    {
        EnsureConnected();
        await SendActionsAsync(
            [new RouterAction(8, "/cgi/reboot", ZeroStack, ZeroStack, [])],
            cancellationToken);
        await DisposeClientAsync();
    }

    public async Task<RouterMobileNetworkModeState> ReadMobileNetworkModeAsync(
        CancellationToken cancellationToken)
    {
        EnsureConnected();

        RouterMobileNetworkModeState? state;
        if (LooksLikeFiveGModel(_capabilities.Model))
        {
            state = await TryReadModernMobileNetworkModeAsync(cancellationToken)
                    ?? await TryReadLegacyMobileNetworkModeAsync(cancellationToken);
        }
        else
        {
            state = await TryReadLegacyMobileNetworkModeAsync(cancellationToken)
                    ?? await TryReadModernMobileNetworkModeAsync(cancellationToken);
        }

        return state ?? throw new RouterConnectionException(
            "This TP-Link firmware did not expose a supported mobile network mode setting.");
    }

    public async Task SetMobileNetworkModeAsync(
        string value,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new RouterConnectionException("Select a mobile network mode first.");

        RouterMobileNetworkModeState state = await ReadMobileNetworkModeAsync(
            cancellationToken);
        RouterMobileNetworkModeOption? selected = state.SupportedModes.FirstOrDefault(
            mode => mode.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
            throw new RouterConnectionException(
                "The selected network mode is not offered by this router firmware.");

        await SendActionsAsync(
            [new RouterAction(
                2,
                _mobileNetworkModeOid,
                _mobileNetworkModeStack,
                ZeroStack,
                [_mobileNetworkModeField + "=" + selected.Value])],
            cancellationToken);

        RouterMobileNetworkModeState confirmed = await ReadMobileNetworkModeAsync(
            cancellationToken);
        if (!confirmed.CurrentValue.Equals(
                selected.Value, StringComparison.OrdinalIgnoreCase))
        {
            throw new RouterConnectionException(
                "The router did not confirm the requested mobile network mode.");
        }
    }

    private async Task<RouterMobileNetworkModeState?>
        TryReadLegacyMobileNetworkModeAsync(CancellationToken cancellationToken)
    {
        try
        {
            CgiResponse response = await SendActionsAsync(
                [new RouterAction(
                    1,
                    "LTE_WAN_CFG",
                    _lteLinkStack,
                    ZeroStack,
                    ["networkPreferredMode"])],
                cancellationToken);
            CgiObject value = response.GetFirst(0);
            string current = value.Get("networkPreferredMode")?.Trim() ?? "";
            if (!LegacyMobileNetworkModes.Any(mode => mode.Value == current))
                return null;

            _mobileNetworkModeOid = "LTE_WAN_CFG";
            _mobileNetworkModeStack = value.Stack;
            _mobileNetworkModeField = "networkPreferredMode";
            return new RouterMobileNetworkModeState
            {
                Model = _capabilities.Model,
                CurrentValue = current,
                SupportedModes = LegacyMobileNetworkModes
            };
        }
        catch (RouterAuthenticationException)
        {
            throw;
        }
        catch (RouterConnectionException)
        {
            return null;
        }
    }

    private async Task<RouterMobileNetworkModeState?>
        TryReadModernMobileNetworkModeAsync(CancellationToken cancellationToken)
    {
        foreach (string stack in new[] { _lteInterfaceStack, _lteLinkStack }
                     .Distinct(StringComparer.Ordinal))
        {
            try
            {
                CgiResponse response = await SendActionsAsync(
                    [new RouterAction(
                        1,
                        "DEV2_LTE_WAN_CFG",
                        stack,
                        ZeroStack,
                        ["networkPreferredModeOptionList",
                         "networkPreferredModeSelected"])],
                    cancellationToken);
                CgiObject value = response.GetFirst(0);
                string current = value.Get("networkPreferredModeSelected")?.Trim() ?? "";
                RouterMobileNetworkModeOption[] modes = ParseModernMobileNetworkModes(
                    value.Get("networkPreferredModeOptionList"));
                if (modes.Length == 0 || !modes.Any(mode =>
                        mode.Value.Equals(current, StringComparison.OrdinalIgnoreCase)))
                    continue;

                _mobileNetworkModeOid = "DEV2_LTE_WAN_CFG";
                _mobileNetworkModeStack = value.Stack;
                _mobileNetworkModeField = "networkPreferredModeSelected";
                return new RouterMobileNetworkModeState
                {
                    Model = _capabilities.Model,
                    CurrentValue = current,
                    SupportedModes = modes
                };
            }
            catch (RouterAuthenticationException)
            {
                throw;
            }
            catch (RouterConnectionException)
            {
            }
        }

        return null;
    }

    internal static RouterMobileNetworkModeOption[] ParseModernMobileNetworkModes(
        string? optionList)
    {
        if (string.IsNullOrWhiteSpace(optionList))
            return [];

        return optionList.Split(',', StringSplitOptions.TrimEntries |
                                      StringSplitOptions.RemoveEmptyEntries)
            .Where(IsKnownModernMobileNetworkMode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new RouterMobileNetworkModeOption(
                value,
                FormatModernMobileNetworkMode(value)))
            .ToArray();
    }

    private static bool IsKnownModernMobileNetworkMode(string value) =>
        value is "5G/4G" or "5G Preferred" or "5G Only" or
            "4G Preferred" or "4G Only" or "3G Only";

    private static string FormatModernMobileNetworkMode(string value) => value switch
    {
        "5G/4G" => "5G / 4G / 3G (automatic)",
        "5G Preferred" => "5G preferred",
        "5G Only" => "5G only",
        "4G Preferred" => "4G preferred (4G / 3G)",
        "4G Only" => "4G only",
        "3G Only" => "3G only",
        _ => value
    };

    private static bool LooksLikeFiveGModel(string model) =>
        model.Contains("NX", StringComparison.OrdinalIgnoreCase) ||
        model.Contains("5G", StringComparison.OrdinalIgnoreCase) ||
        model.Contains("M8550", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<RouterSmsMessage>> ReadSmsTimelineAsync(
        CancellationToken cancellationToken)
    {
        EnsureConnected();
        var messages = new List<RouterSmsMessage>();
        messages.AddRange(await ReadSmsFolderAsync(
            "LTE_SMS_RECVMSGBOX",
            "LTE_SMS_RECVMSGENTRY",
            RouterSmsFolder.Inbox,
            "from",
            "receivedTime",
            cancellationToken));
        await Task.Delay(150, cancellationToken);
        messages.AddRange(await ReadSmsFolderAsync(
            "LTE_SMS_SENDMSGBOX",
            "LTE_SMS_SENDMSGENTRY",
            RouterSmsFolder.Sent,
            "to",
            "sendTime",
            cancellationToken));
        await Task.Delay(150, cancellationToken);
        messages.AddRange(await ReadSmsFolderAsync(
            "LTE_SMS_DRAFTMSGBOX",
            "LTE_SMS_DRAFTMSGENTRY",
            RouterSmsFolder.Draft,
            "to",
            null,
            cancellationToken));

        return messages
            .OrderByDescending(message => message.Timestamp.HasValue)
            .ThenByDescending(message => message.Timestamp)
            .ThenBy(message => message.Folder)
            .ToArray();
    }

    public async Task<IReadOnlyList<RouterConnectedDevice>> ReadConnectedDevicesAsync(
        CancellationToken cancellationToken)
    {
        EnsureConnected();
        CgiResponse response = await SendActionsAsync(
            [new RouterAction(
                5,
                "LAN_HOST_ENTRY",
                ZeroStack,
                ZeroStack,
                ["IPAddress", "MACAddress", "hostName", "X_TP_ConnType", "active"])],
            cancellationToken);

        return response.GetObjects(0)
            .Select(item => new RouterConnectedDevice
            {
                Name = FirstNonEmpty(item.Get("hostName"), "Unknown device"),
                IpAddress = FirstNonEmpty(item.Get("IPAddress"), "-"),
                MacAddress = NormalizeMacAddress(item.Get("MACAddress")),
                ConnectionType = FormatConnectionType(item.Get("X_TP_ConnType")),
                IsActive = ParseInt(item.Get("active")) is not 0
            })
            .Where(device => device.IsActive)
            .Where(device => device.IpAddress != "-" || device.MacAddress != "-")
            .GroupBy(device => device.MacAddress != "-"
                    ? device.MacAddress
                    : device.IpAddress,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(device => device.Name.Equals(
                "Unknown device", StringComparison.OrdinalIgnoreCase))
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.IpAddress, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeMacAddress(string? value)
    {
        string candidate = value?.Trim() ?? "";
        if (candidate.Length == 0)
            return "-";
        string hex = new(candidate.Where(char.IsAsciiHexDigit).ToArray());
        return hex.Length == 12
            ? string.Join(":", Enumerable.Range(0, 6)
                .Select(index => hex.Substring(index * 2, 2).ToUpperInvariant()))
            : candidate.ToUpperInvariant();
    }

    private static string FormatConnectionType(string? value)
    {
        string candidate = value?.Trim() ?? "";
        if (candidate.Length == 0)
            return "Unknown";
        if (candidate.Contains("wireless", StringComparison.OrdinalIgnoreCase) ||
            candidate.Contains("wifi", StringComparison.OrdinalIgnoreCase) ||
            candidate.Contains("wlan", StringComparison.OrdinalIgnoreCase))
            return "Wi-Fi";
        if (candidate.Contains("ethernet", StringComparison.OrdinalIgnoreCase) ||
            candidate.Contains("wired", StringComparison.OrdinalIgnoreCase) ||
            candidate.Equals("LAN", StringComparison.OrdinalIgnoreCase))
            return "Ethernet";
        return candidate;
    }

    private async Task<IReadOnlyList<RouterSmsMessage>> ReadSmsFolderAsync(
        string boxObject,
        string entryObject,
        RouterSmsFolder folder,
        string addressField,
        string? timeField,
        CancellationToken cancellationToken)
    {
        await SendActionsAsync(
            [new RouterAction(
                2,
                boxObject,
                ZeroStack,
                ZeroStack,
                ["PageNumber=0"])],
            cancellationToken);

        CgiResponse boxResponse = await SendActionsAsync(
            [new RouterAction(
                1,
                boxObject,
                ZeroStack,
                ZeroStack,
                ["totalNumber", "amountPerPage"])],
            cancellationToken);
        CgiObject box = boxResponse.GetFirst(0);
        int total = Math.Clamp(ParseInt(box.Get("totalNumber")) ?? 0, 0, 100);
        if (total == 0)
            return [];

        int pageSize = Math.Clamp(ParseInt(box.Get("amountPerPage")) ?? 8, 1, 50);
        int pages = (int)Math.Ceiling(total / (double)pageSize);
        var messages = new List<RouterSmsMessage>(total);
        for (int page = 1; page <= pages && messages.Count < total; page++)
        {
            if (page > 1)
                await Task.Delay(75, cancellationToken);
            await SendActionsAsync(
                [new RouterAction(
                    2,
                    boxObject,
                    ZeroStack,
                    ZeroStack,
                    ["PageNumber=" + page.ToString(CultureInfo.InvariantCulture)])],
                cancellationToken);
            string[] fields = folder switch
            {
                RouterSmsFolder.Inbox =>
                    ["index", addressField, "content", timeField!, "unread"],
                RouterSmsFolder.Sent =>
                    ["index", addressField, "content", timeField!],
                _ => ["index", addressField, "content"]
            };
            CgiResponse pageResponse = await SendActionsAsync(
                [new RouterAction(
                    5,
                    entryObject,
                    ZeroStack,
                    ZeroStack,
                    fields)],
                cancellationToken);

            foreach (CgiObject item in pageResponse.GetObjects(0))
            {
                if (!IsStack(item.Stack))
                    continue;
                string timeText = timeField is null
                    ? ""
                    : item.Get(timeField)?.Trim() ?? "";
                messages.Add(new RouterSmsMessage
                {
                    Stack = item.Stack,
                    Index = item.Get("index")?.Trim() ?? "",
                    PageNumber = page,
                    Address = item.Get(addressField)?.Trim() ?? "",
                    Content = DecodeSmsContent(item.Get("content") ?? ""),
                    TimeText = timeText,
                    Timestamp = ParseSmsTimestamp(timeText),
                    Folder = folder,
                    IsUnread = folder == RouterSmsFolder.Inbox &&
                               ParseInt(item.Get("unread")) == 1
                });
                if (messages.Count >= total)
                    break;
            }
        }
        return messages;
    }

    public async Task SetSmsUnreadAsync(
        string stack,
        string index,
        int pageNumber,
        bool unread,
        CancellationToken cancellationToken)
    {
        EnsureConnected();
        if (!IsStack(stack))
            throw new RouterConnectionException("The selected SMS identifier is invalid.");
        await SelectSmsFolderPageAsync(
            "LTE_SMS_RECVMSGBOX", pageNumber, cancellationToken);
        await SendActionsAsync(
            [new RouterAction(
                2,
                "LTE_SMS_RECVMSGENTRY",
                stack,
                ZeroStack,
                ["unread=" + (unread ? "1" : "0")])],
            cancellationToken);
        await VerifySmsUnreadAsync(stack, index, unread, cancellationToken);
    }

    public async Task DeleteSmsAsync(
        RouterSmsFolder folder,
        string stack,
        string index,
        int pageNumber,
        CancellationToken cancellationToken)
    {
        EnsureConnected();
        if (!IsStack(stack))
            throw new RouterConnectionException("The selected SMS identifier is invalid.");
        string entryObject = folder switch
        {
            RouterSmsFolder.Inbox => "LTE_SMS_RECVMSGENTRY",
            RouterSmsFolder.Sent => "LTE_SMS_SENDMSGENTRY",
            RouterSmsFolder.Draft => "LTE_SMS_DRAFTMSGENTRY",
            _ => throw new RouterConnectionException("The selected SMS folder is invalid.")
        };
        string boxObject = folder switch
        {
            RouterSmsFolder.Inbox => "LTE_SMS_RECVMSGBOX",
            RouterSmsFolder.Sent => "LTE_SMS_SENDMSGBOX",
            RouterSmsFolder.Draft => "LTE_SMS_DRAFTMSGBOX",
            _ => throw new RouterConnectionException("The selected SMS folder is invalid.")
        };
        await SelectSmsFolderPageAsync(boxObject, pageNumber, cancellationToken);
        await SendActionsAsync(
            [new RouterAction(4, entryObject, stack, ZeroStack, [])],
            cancellationToken);
        await VerifySmsDeletedAsync(folder, stack, index, cancellationToken);
    }

    private Task<CgiResponse> SelectSmsFolderPageAsync(
        string boxObject,
        int pageNumber,
        CancellationToken cancellationToken) =>
        SendActionsAsync(
            [new RouterAction(
                2,
                boxObject,
                ZeroStack,
                ZeroStack,
                ["PageNumber=" + Math.Max(1, pageNumber).ToString(
                    CultureInfo.InvariantCulture)])],
            cancellationToken);

    private async Task VerifySmsUnreadAsync(
        string stack,
        string index,
        bool expectedUnread,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(300, cancellationToken);
            IReadOnlyList<RouterSmsMessage> inbox =
                await ReadSmsFolderForVerificationAsync(
                    RouterSmsFolder.Inbox,
                    cancellationToken);
            RouterSmsMessage? message = inbox.FirstOrDefault(item =>
                IsSelectedSms(item, stack, index));
            if (message is not null && message.IsUnread == expectedUnread)
                return;
        }
        throw new RouterConnectionException(
            "The MR600 did not confirm the requested SMS read-state change.");
    }

    private async Task VerifySmsDeletedAsync(
        RouterSmsFolder folder,
        string stack,
        string index,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(300, cancellationToken);
            IReadOnlyList<RouterSmsMessage> messages =
                await ReadSmsFolderForVerificationAsync(folder, cancellationToken);
            if (!messages.Any(item => IsSelectedSms(item, stack, index)))
                return;
        }
        throw new RouterConnectionException(
            "The MR600 did not confirm deletion of the selected SMS.");
    }

    private Task<IReadOnlyList<RouterSmsMessage>> ReadSmsFolderForVerificationAsync(
        RouterSmsFolder folder,
        CancellationToken cancellationToken) => folder switch
        {
            RouterSmsFolder.Inbox => ReadSmsFolderAsync(
                "LTE_SMS_RECVMSGBOX", "LTE_SMS_RECVMSGENTRY", folder,
                "from", "receivedTime", cancellationToken),
            RouterSmsFolder.Sent => ReadSmsFolderAsync(
                "LTE_SMS_SENDMSGBOX", "LTE_SMS_SENDMSGENTRY", folder,
                "to", "sendTime", cancellationToken),
            RouterSmsFolder.Draft => ReadSmsFolderAsync(
                "LTE_SMS_DRAFTMSGBOX", "LTE_SMS_DRAFTMSGENTRY", folder,
                "to", null, cancellationToken),
            _ => throw new RouterConnectionException(
                "The selected SMS folder is invalid.")
        };

    private static bool IsSelectedSms(
        RouterSmsMessage message,
        string stack,
        string index) =>
        !string.IsNullOrWhiteSpace(index) && index != "0"
            ? string.Equals(message.Index, index, StringComparison.Ordinal)
            : string.Equals(message.Stack, stack, StringComparison.Ordinal);

    public async Task SendSmsAsync(
        string phoneNumber,
        string content,
        CancellationToken cancellationToken)
    {
        EnsureConnected();
        string recipient = phoneNumber.Trim();
        ValidateSms(recipient, content);
        string encodedContent = EncodeSmsContent(content);
        await SendActionsAsync(
            [new RouterAction(
                2,
                "LTE_SMS_SENDNEWMSG",
                ZeroStack,
                ZeroStack,
                ["index=1", "to=" + recipient, "textContent=" + encodedContent])],
            cancellationToken);

        while (true)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            CgiResponse resultResponse;
            try
            {
                resultResponse = await SendActionsAsync(
                    [new RouterAction(
                        1,
                        "LTE_SMS_SENDNEWMSG",
                        ZeroStack,
                        ZeroStack,
                        ["sendResult"])],
                    cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The send was already accepted. A slow status request is not
                // evidence of failure, so keep polling rather than inviting a
                // duplicate send.
                continue;
            }

            int result = ParseInt(
                resultResponse.GetFirst(0).Get("sendResult")) ?? 0;
            if (result == 1)
                return;

            // Both values are transient after this MR600 accepts an SMS.
            // There is deliberately no overall confirmation deadline; only an
            // explicit application shutdown cancels this wait.
            if (result is 2 or 3)
                continue;

            throw new RouterConnectionException(
                "The MR600 accepted the SMS but returned an unknown delivery " +
                "status. Check Sent before trying again to avoid a duplicate.");
        }
    }

    public async Task SaveSmsDraftAsync(
        string phoneNumber,
        string content,
        CancellationToken cancellationToken)
    {
        EnsureConnected();
        string recipient = phoneNumber.Trim();
        ValidateSms(recipient, content);
        await SendActionsAsync(
            [new RouterAction(
                2,
                "LTE_SMS_SENDNEWMSG",
                ZeroStack,
                ZeroStack,
                [
                    "index=2",
                    "to=" + recipient,
                    "textContent=" + EncodeSmsContent(content)
                ])],
            cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        if (_client is not null && _crypto is not null)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                await SendActionsAsync(
                    [new RouterAction(8, "/cgi/clearBusy", ZeroStack, ZeroStack, [])],
                    timeout.Token);
                await SendActionsAsync(
                    [new RouterAction(8, "/cgi/logout", ZeroStack, ZeroStack, [])],
                    timeout.Token);
            }
            catch
            {
                // The session may already be gone. Local cleanup still proceeds.
            }
        }

        await DisposeClientAsync();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await DisconnectAsync(timeout.Token);
        }
        catch
        {
            await DisposeClientAsync();
        }
    }

    public static Uri NormalizeRouterUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme is not ("http" or "https"))
            throw new RouterConnectionException(
                "The router address must start with http:// or https://.");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new RouterConnectionException(
                "Do not put credentials in the router address.");

        return new UriBuilder(uri)
        {
            Path = "/",
            Query = "",
            Fragment = ""
        }.Uri;
    }

    private async Task LoginAsync(
        bool allowSessionTakeover,
        CancellationToken cancellationToken)
    {
        HttpClient client = GetClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(ConnectTimeout);

        using (var primeRequest = new HttpRequestMessage(HttpMethod.Get, ""))
        using (HttpResponseMessage primeResponse = await client.SendAsync(
                   primeRequest,
                   HttpCompletionOption.ResponseHeadersRead,
                   timeout.Token))
        {
            if (!primeResponse.IsSuccessStatusCode)
                throw new RouterConnectionException(
                    "The TP-Link login page could not be opened.");
        }

        LoginParameters parameters = await GetLoginParametersAsync(timeout.Token);
        BusyState busy = await GetBusyStateAsync(timeout.Token);
        if (busy.IsBusy && allowSessionTakeover)
        {
            for (int retry = 0; retry < 3 && busy.IsBusy; retry++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token);
                busy = await GetBusyStateAsync(timeout.Token);
            }
        }

        // The MR600 web UI polls a busy management session three times and,
        // after explicit takeover confirmation, submits the login even when
        // isBusy has not cleared. Mirror that behavior: otherwise a browser
        // performing background requests can keep NetPulse locked out even
        // though the user allowed session takeover.
        if (busy.IsBusy && !allowSessionTakeover)
            throw new RouterBusyException(
                "The MR600 is processing another management request. " +
                "Wait a few seconds, then try again.");
        if (busy.IsLoggedIn && !allowSessionTakeover)
            throw new RouterBusyException(
                "Another MR600 web or app session is signed in. NetPulse is " +
                "waiting and will not force that session off.");

        _crypto = new TpLinkCrypto(
            "admin",
            _password,
            parameters.Modulus,
            parameters.Exponent,
            parameters.Sequence);
        EncryptedPayload payload = _crypto.EncryptLogin("admin", _password);
        string path = "cgi/login?data=" + Uri.EscapeDataString(payload.Data) +
                      "&sign=" + Uri.EscapeDataString(payload.Signature) +
                      "&Action=1&LoginStatus=0";

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new ByteArrayContent([])
        };
        using HttpResponseMessage response = await client.SendAsync(
            loginRequest, HttpCompletionOption.ResponseContentRead, timeout.Token);
        string responseText = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new RouterConnectionException(
                $"The router rejected the login request ({(int)response.StatusCode}).");

        int result = ReadLoginResult(responseText);
        if (result == InvalidCredentialsCode)
            throw new RouterAuthenticationException(
                "The TP-Link password was not accepted. To protect the router, " +
                "NetPulse will not retry it automatically.");
        if (result != 0)
            throw new RouterConnectionException(
                "The router returned an unsupported login response.");

        _tokenId = GetHeaderValue(response, "TokenID") ??
                   GetHeaderValue(response, "tokenid") ??
                   DefaultTokenId;

        using var homeRequest = new HttpRequestMessage(HttpMethod.Get, "");
        using HttpResponseMessage homeResponse = await client.SendAsync(
            homeRequest, HttpCompletionOption.ResponseContentRead, timeout.Token);
        string homeText = await homeResponse.Content.ReadAsStringAsync(timeout.Token);
        if (LooksLikeLoginPage(homeText))
            throw new RouterAuthenticationException(
                "The TP-Link session did not open after login.");

        _tokenId = ExtractTokenId(homeText) ?? _tokenId;
        if (_tokenId == DefaultTokenId)
            throw new RouterAuthenticationException(
                "The MR600 did not provide a session token after login.");
    }

    private async Task<LoginParameters> GetLoginParametersAsync(
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "cgi/getParm")
        {
            Content = new ByteArrayContent([])
        };
        using HttpResponseMessage response = await GetClient().SendAsync(
            request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        string text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new RouterConnectionException(
                "The TP-Link login parameters are unavailable.");

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;
            string modulus = ReadJsonString(root, "nn");
            string exponent = ReadJsonString(root, "ee");
            long sequence = ReadJsonLong(root, "seq");
            if (string.IsNullOrWhiteSpace(modulus) ||
                string.IsNullOrWhiteSpace(exponent))
                throw new JsonException();
            return new LoginParameters(modulus, exponent, sequence);
        }
        catch (JsonException ex)
        {
            Match modulus = Regex.Match(text,
                "(?:nn|modulus)\\s*[:=]\\s*['\\\"](?<value>[0-9a-f]+)['\\\"]",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            Match exponent = Regex.Match(text,
                "(?:ee|exponent)\\s*[:=]\\s*['\\\"](?<value>[0-9a-f]+)['\\\"]",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            Match sequence = Regex.Match(text,
                "seq\\s*[:=]\\s*['\\\"]?(?<value>[0-9]+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (modulus.Success && exponent.Success && sequence.Success &&
                long.TryParse(sequence.Groups["value"].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long sequenceValue))
            {
                return new LoginParameters(
                    modulus.Groups["value"].Value,
                    exponent.Groups["value"].Value,
                    sequenceValue);
            }

            throw new RouterConnectionException(
                "The router uses an unsupported login-parameter format.", ex);
        }
    }

    private async Task<BusyState> GetBusyStateAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "cgi/getBusy")
        {
            Content = new ByteArrayContent([])
        };
        using HttpResponseMessage response = await GetClient().SendAsync(
            request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new BusyState(false, false);
        return new BusyState(
            ReadJsonInt(json, "isLogined", 0) != 0,
            ReadJsonInt(json, "isBusy", 0) != 0);
    }

    private async Task DiscoverLteStacksAsync(CancellationToken cancellationToken)
    {
        var action = new RouterAction(
            5,
            "WAN_COMMON_INTF_CFG",
            ZeroStack,
            ZeroStack,
            ["WANAccessType"]);
        try
        {
            CgiResponse response = await SendActionsAsync([action], cancellationToken);
            CgiObject? lte = response.GetObjects(0).FirstOrDefault(item =>
                string.Equals(item.Get("WANAccessType"), "LTE",
                    StringComparison.OrdinalIgnoreCase));
            if (lte is not null && IsStack(lte.Stack))
            {
                _lteInterfaceStack = lte.Stack;
                _lteLinkStack = AddChildStack(lte.Stack);
            }
        }
        catch (RouterAuthenticationException)
        {
            throw;
        }
        catch
        {
            // The validated MR600 v5 uses these standard first-WAN stacks.
            _lteInterfaceStack = "1,0,0,0,0,0";
            _lteLinkStack = "1,1,0,0,0,0";
        }
    }

    private async Task<RouterTelemetry> ReadTelemetryCoreAsync(
        CancellationToken cancellationToken)
    {
        RouterAction[] actions =
        [
            new(1, "WAN_LTE_LINK_CFG", _lteLinkStack, ZeroStack,
                ["simStatus", "roamingStatus", "signalStrength", "networkType", "connectStatus"]),
            new(1, "WAN_LTE_INTF_CFG", _lteInterfaceStack, ZeroStack,
                ["totalStatistics", "curRxSpeed", "curTxSpeed"]),
            new(1, "LTE_NET_STATUS", _lteLinkStack, ZeroStack,
                []),
            new(1, "LTE_PROF_STAT", _lteLinkStack, ZeroStack,
                ["ispName", "spn"]),
            new(1, "LTE_CELL_LOCK", ZeroStack, ZeroStack,
                ["rfInfoCellID", "rfInfoEARFCN", "rfInfoPCI"]),
            new(1, "IGD_DEV_INFO", ZeroStack, ZeroStack,
                ["modelName", "hardwareVersion", "softwareVersion"])
        ];

        CgiResponse response = await SendActionsAsync(actions, cancellationToken);
        CgiObject link = response.GetFirst(0);
        CgiObject data = response.GetFirst(1);
        CgiObject network = response.GetFirst(2);
        CgiObject profile = response.GetFirst(3);
        CgiObject device = response.GetFirstOrEmpty(5);

        int? signalBars = ParseInt(link.Get("signalStrength"));
        int? networkType = ParseInt(link.Get("networkType"));
        int? registrationStatus = ParseInt(network.Get("regStat"));
        bool connected = registrationStatus == 1;
        string isp = FirstNonEmpty(profile.Get("ispName"), profile.Get("spn"), "-");
        string band = FormatBand(ParseLong(network.Get("rfInfoBand")));
        string primaryBand = FormatPrimaryBand(
            network.Get("rfInfoPCellBand"),
            band);

        return new RouterTelemetry
        {
            Timestamp = DateTime.Now,
            IsConnected = connected,
            Status = connected ? "Connected" : "Not registered",
            Isp = isp,
            NetworkType = FormatNetworkType(networkType),
            Band = band,
            PrimaryBand = primaryBand,
            SimStatus = FormatSimStatus(ParseInt(link.Get("simStatus"))),
            SignalPercent = signalBars.HasValue
                ? Math.Clamp(signalBars.Value * 25, 0, 100)
                : null,
            RsrpDbm = ParseDouble(network.Get("rfInfoRsrp")),
            RsrqDb = ParseDouble(network.Get("rfInfoRsrq")),
            SnrDb = ParseDouble(network.Get("rfInfoSnr")) is double rawSnr
                ? rawSnr / 10D
                : null,
            RssiDbm = ParseDouble(network.Get("rfInfoRssi")),
            // LTE_CELL_LOCK contains the configured lock target, not necessarily
            // the currently serving PCell. Never use it as live telemetry.
            Pci = FirstRadioValue(network.Get("rfInfoPCI")),
            CellId = FirstRadioValue(network.Get("rfInfoCellID")),
            Earfcn = FirstRadioValue(
                network.Get("rfInfoPCellChannel"),
                network.Get("rfInfoChannel"),
                network.Get("rfInfoEARFCN")),
            UnreadSmsCount = ParseInt(network.Get("smsUnreadCount")) is int unread
                ? Math.Clamp(unread, 0, 9999)
                : null,
            TotalBytes = ParseLong(data.Get("totalStatistics")),
            UploadBytesPerSecond = ParseLong(data.Get("curTxSpeed")),
            DownloadBytesPerSecond = ParseLong(data.Get("curRxSpeed")),
            Model = FirstNonEmpty(
                device.Get("modelName"), _capabilities.Model, "TP-Link router"),
            HardwareVersion = FirstNonEmpty(
                device.Get("hardwareVersion"), _capabilities.HardwareVersion, "Unknown"),
            FirmwareVersion = FirstNonEmpty(
                device.Get("softwareVersion"), _capabilities.FirmwareVersion, "Unknown")
        };
    }

    private async Task<CgiResponse> SendActionsAsync(
        IReadOnlyList<RouterAction> actions,
        CancellationToken cancellationToken)
    {
        EnsureConnected();
        string plainText = BuildActionBody(actions);
        bool smsRequest = actions.Any(action =>
            action.Oid.StartsWith("LTE_SMS_", StringComparison.Ordinal));
        int attempts = smsRequest ? 4 : 2;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            EncryptedPayload encrypted = _crypto!.EncryptRequest(plainText);
            string body = $"sign={encrypted.Signature}\r\ndata={encrypted.Data}\r\n";
            using var request = new HttpRequestMessage(HttpMethod.Post, "cgi_gdpr?")
            {
                Content = new StringContent(body, Encoding.UTF8)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            request.Headers.TryAddWithoutValidation("TokenID", _tokenId);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(smsRequest ? TimeSpan.FromSeconds(25) : RequestTimeout);
            try
            {
                using HttpResponseMessage response = await GetClient().SendAsync(
                    request, HttpCompletionOption.ResponseContentRead, timeout.Token);
                string encryptedResponse = await response.Content.ReadAsStringAsync(timeout.Token);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new RouterAuthenticationException("The TP-Link session expired.");
                bool transient = response.StatusCode is HttpStatusCode.InternalServerError or
                    HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests;
                if (!response.IsSuccessStatusCode)
                {
                    if (transient && attempt < attempts)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt * attempt), cancellationToken);
                        continue;
                    }
                    throw new RouterConnectionException(
                        $"The router request failed ({(int)response.StatusCode}) after {attempt} attempt(s).");
                }
                if (LooksLikeLoginPage(encryptedResponse))
                    throw new RouterAuthenticationException("The TP-Link session expired.");

                try
                {
                    return ParseCgiResponse(_crypto.DecryptResponse(encryptedResponse));
                }
                catch (FormatException)
                {
                    throw new RouterAuthenticationException("The TP-Link session response was not encrypted as expected.");
                }
                catch (CryptographicException)
                {
                    throw new RouterAuthenticationException("The TP-Link session could not be decrypted.");
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt * attempt), cancellationToken);
            }
        }
        throw new RouterConnectionException("The router did not complete the request after multiple attempts.");
    }

    private static string BuildActionBody(IReadOnlyList<RouterAction> actions)
    {
        var builder = new StringBuilder();
        builder.AppendJoin('&', actions.Select(action => action.Type));
        builder.Append("\r\n");
        for (int index = 0; index < actions.Count; index++)
        {
            RouterAction action = actions[index];
            builder.Append('[').Append(action.Oid).Append('#')
                .Append(action.Stack).Append('#').Append(action.ParentStack)
                .Append(']').Append(index).Append(',')
                .Append(action.Attributes.Count).Append("\r\n");
            foreach (string attribute in action.Attributes)
                builder.Append(attribute).Append("\r\n");
        }
        return builder.ToString();
    }

    private static CgiResponse ParseCgiResponse(string responseText)
    {
        var result = new CgiResponse();
        CgiObject? current = null;
        foreach (string rawLine in responseText.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
                continue;
            if (line[0] == '[')
            {
                int close = line.IndexOf(']');
                if (close < 2)
                    continue;
                string stack = line[1..close];
                if (!int.TryParse(line[(close + 1)..], out int section))
                    continue;
                if (stack.Equals("error", StringComparison.OrdinalIgnoreCase))
                {
                    if (section != 0)
                        throw new RouterConnectionException(
                            $"The TP-Link local API returned error {section}.");
                    current = null;
                    continue;
                }

                current = new CgiObject(stack);
                result.Add(section, current);
                continue;
            }

            if (current is null)
                continue;
            int equals = line.IndexOf('=');
            if (equals > 0)
                current.Values[line[..equals]] = line[(equals + 1)..];
        }
        return result;
    }

    private static async Task ValidateLocalAddressAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.DnsSafeHost, out IPAddress? address))
            addresses = [address];
        else
            addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost,
                cancellationToken);

        if (addresses.Length == 0 || addresses.Any(item => !IsPrivateAddress(item)))
        {
            throw new RouterConnectionException(
                "For safety, TP-Link monitoring only connects to a private LAN address.");
        }
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal)
            return true;
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return (address.GetAddressBytes()[0] & 0xFE) == 0xFC;

        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
               bytes[0] == 127 ||
               bytes[0] == 192 && bytes[1] == 168 ||
               bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
               bytes[0] == 169 && bytes[1] == 254;
    }

    private static bool LooksLikeLoginPage(string value) =>
        value.Contains("pc-login-password", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("cgi/getParm", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractTokenId(string html)
    {
        Match match = Regex.Match(html,
            "(?:tokenid|token)\\s*[:=]\\s*['\\\"](?<token>[^'\\\"]{1,256})['\\\"]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["token"].Value : null;
    }

    private static int ReadLoginResult(string value)
    {
        int jsonResult = ReadJsonInt(value, "ret", int.MinValue);
        if (jsonResult != int.MinValue)
            return jsonResult;

        Match match = Regex.Match(value,
            "(?:\\$\\.)?ret\\s*=\\s*(?<value>-?[0-9]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(
            match.Groups["value"].Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int result)
            ? result
            : int.MinValue;
    }

    private static string? GetHeaderValue(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;
    }

    private static int ReadJsonInt(string json, string name, int fallback)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (property.Value.ValueKind == JsonValueKind.Number &&
                    property.Value.TryGetInt32(out int number))
                    return number;
                if (int.TryParse(property.Value.ToString(), out number))
                    return number;
            }
        }
        catch (JsonException)
        {
        }
        return fallback;
    }

    private static string ReadJsonString(JsonElement root, string name)
    {
        foreach (JsonProperty property in root.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value.ToString();
        return "";
    }

    private static long ReadJsonLong(JsonElement root, string name)
    {
        string value = ReadJsonString(root, name);
        return long.TryParse(value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out long result) ? result : 0;
    }

    private static string AddChildStack(string stack)
    {
        int[] values = stack.Split(',')
            .Select(item => int.TryParse(item, out int value) ? value : 0)
            .Concat(Enumerable.Repeat(0, 6))
            .Take(6)
            .ToArray();
        int next = Array.FindIndex(values, value => value == 0);
        if (next >= 0)
            values[next] = 1;
        return string.Join(',', values);
    }

    private static RouterAction BuildCellLockAction(RouterLockState state)
    {
        if (!state.CellLockEnabled)
        {
            return new RouterAction(
                2,
                "LTE_CELL_LOCK",
                ZeroStack,
                ZeroStack,
                ["rfInfoCellIDLock=0"]);
        }

        var target = new RouterCellLockTarget
        {
            Bands = [1],
            Earfcn = state.Earfcn,
            Pci = state.Pci,
            CellId = state.CellId
        };
        ValidateLockTarget(target);
        return new RouterAction(
            2,
            "LTE_CELL_LOCK",
            ZeroStack,
            ZeroStack,
            [
                "rfInfoCellIDLock=1",
                "rfInfoCellID=" + (state.CellId?.Trim() ?? ""),
                "rfInfoEARFCN=" + state.Earfcn.Trim(),
                "rfInfoPCI=" + state.Pci.Trim()
            ]);
    }

    private static void ValidateLockTarget(RouterCellLockTarget target)
    {
        if (target.Bands.Count == 0 || target.Bands.Any(band => band is < 1 or > 64))
            throw new RouterConnectionException(
                "The MR600 band lock requires one or more LTE bands from 1 to 64.");
        if (!target.HasCellTarget)
        {
            if ((!string.IsNullOrWhiteSpace(target.Earfcn) && target.Earfcn != "-") ||
                (!string.IsNullOrWhiteSpace(target.Pci) && target.Pci != "-"))
                throw new RouterConnectionException(
                    "EARFCN and PCI are both required for a cell-specific lock.");
            return;
        }
        if (!int.TryParse(target.Earfcn, NumberStyles.None,
                CultureInfo.InvariantCulture, out int earfcn) ||
            earfcn is < 1 or > 65535)
            throw new RouterConnectionException(
                "EARFCN must be a number from 1 to 65535.");
        if (!int.TryParse(target.Pci, NumberStyles.None,
                CultureInfo.InvariantCulture, out int pci) ||
            pci is < 0 or > 512)
            throw new RouterConnectionException(
                "PCI must be a number from 0 to 512.");
        if (!LteRadioIdentifier.TryNormalizeCellId(target.CellId, out _))
            throw new RouterConnectionException(
                "CID is optional; use a decimal or hexadecimal value " +
                "(for example ABCDE).");
    }

    private static (int Low, int High) BuildBandMasks(IReadOnlyList<int> bands)
    {
        int low = 0;
        int high = 0;
        foreach (int band in bands.Distinct())
        {
            if (band <= 32)
                low |= unchecked(1 << (band - 1));
            else
                high |= unchecked(1 << (band - 33));
        }
        return (low, high);
    }

    private static string? NormalizeOptionalCellId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return LteRadioIdentifier.TryNormalizeCellId(value, out string? normalized)
            ? normalized
            : value.Trim();
    }

    private static bool IsStack(string value) =>
        Regex.IsMatch(value, @"^\d+(?:,\d+){5}$",
            RegexOptions.CultureInvariant);

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
            out int result) ? result : null;

    private static int ParseMask(string? value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int signed))
            return signed;
        return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
            out uint unsigned)
            ? unchecked((int)unsigned)
            : 0;
    }

    private static long? ParseLong(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
            out double result) && result is >= 0 and <= long.MaxValue
            ? (long)result
            : null;

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
            out double result) ? result : null;

    private static DateTime? ParseSmsTimestamp(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (DateTime.TryParse(
                value,
                CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTime local) ||
            DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out local))
            return local;
        return null;
    }

    private static string FirstRadioValue(params string?[] values) =>
        values.Select(value => value?.Trim())
            .FirstOrDefault(value =>
                !string.IsNullOrWhiteSpace(value) && value is not "-" and not "0")
        ?? "-";

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "-";

    private static string FormatNetworkType(int? value) => value switch
    {
        1 => "2G",
        2 => "3G",
        3 => "4G LTE",
        4 or 7 => "4G+ LTE-A",
        5 => "5G",
        null => "-",
        _ => "Mobile network " + value.Value
    };

    private static string FormatSimStatus(int? value) => value switch
    {
        1 => "No SIM",
        2 => "PIN required",
        3 => "Ready",
        4 => "PUK required",
        5 => "Ready",
        null => "-",
        _ => "SIM status " + value.Value
    };

    private static string FormatBand(long? rawValue)
    {
        if (!rawValue.HasValue || rawValue.Value < 0)
            return "-";
        int primary = (int)(rawValue.Value & 0xFF);
        int secondary = (int)((rawValue.Value >> 8) & 0xFF);
        if (primary == 0)
            return "-";
        string primaryLabel = FormatBandCode(primary);
        string secondaryLabel = FormatBandCode(secondary);
        return secondary == 0 ? primaryLabel : $"{primaryLabel} + {secondaryLabel}";
    }

    private static string FormatPrimaryBand(string? rawValue, string band)
    {
        if (long.TryParse(rawValue, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out long raw) && raw > 0)
        {
            string formatted = FormatBand(raw);
            if (formatted != "-")
                return formatted.Split('+', 2)[0].Trim();
        }
        return band == "-" ? "-" : band.Split('+', 2)[0].Trim();
    }

    private static void ValidateSms(string phoneNumber, string content)
    {
        if (!Regex.IsMatch(phoneNumber, @"^\+?\d{1,20}$",
                RegexOptions.CultureInvariant) || phoneNumber.Length > 20)
            throw new RouterConnectionException(
                "Phone number must contain 1 to 20 digits, with an optional leading +.");
        if (string.IsNullOrWhiteSpace(content))
            throw new RouterConnectionException("Enter an SMS message.");

        (int length, int maximum) = MeasureSms(content);
        if (length > maximum)
            throw new RouterConnectionException(
                $"This message exceeds the MR600 {maximum}-character limit for its encoding.");
    }

    internal static (int Used, int Maximum) MeasureSms(string content)
    {
        bool gsm7 = content.All(IsBasicGsmCharacter);
        return gsm7
            ? (content.Sum(GsmCharacterLength), 765)
            : (content.Length, 335);
    }

    private static bool IsBasicGsmCharacter(char value) =>
        value is '\r' or '\n' ||
        ("@£$¥èéùìòÇØøÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?" +
         "¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà" +
         "^{}\\[~]|€").IndexOf(value) >= 0;

    private static int GsmCharacterLength(char value) =>
        "^{}\\[~]|€".IndexOf(value) >= 0 ? 2 : 1;

    private static string EncodeSmsContent(string content) =>
        content.Replace('\r', '\u0011').Replace('\n', '\u0012');

    private static string DecodeSmsContent(string content) =>
        content.Replace('\u0012', '\n').Replace('\u0011', '\r');

    private static string FormatBandCode(int code)
    {
        string label = code switch
        {
            40 => "450 MHz",
            41 => "480 MHz",
            42 => "750 MHz",
            43 => "850 MHz",
            44 or 45 or 46 => "900 MHz",
            47 => "1800 MHz",
            48 => "1900 MHz",
            >= 80 and <= 88 => (code - 79).ToString(CultureInfo.InvariantCulture),
            90 => "11",
            >= 120 and <= 133 => (code - 119).ToString(CultureInfo.InvariantCulture),
            134 => "17",
            135 => "33",
            136 => "34",
            137 => "35",
            138 => "36",
            139 => "37",
            140 => "38",
            141 => "39",
            142 => "40",
            143 => "18",
            144 => "19",
            145 => "20",
            146 => "21",
            147 => "24",
            148 => "25",
            149 => "41",
            150 => "42",
            151 => "43",
            152 => "23",
            153 => "26",
            154 => "32",
            155 => "125",
            156 => "126",
            157 => "127",
            158 => "28",
            159 => "29",
            160 => "30",
            200 => "A",
            201 => "B",
            202 => "C",
            203 => "D",
            204 => "E",
            205 => "F",
            _ => code.ToString(CultureInfo.InvariantCulture)
        };
        return label.EndsWith("MHz", StringComparison.Ordinal) ? label : "B" + label;
    }

    private HttpClient GetClient() => _client ??
        throw new RouterConnectionException("The TP-Link provider is not initialized.");

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new RouterConnectionException("The TP-Link router is not connected.");
    }

    private Task DisposeClientAsync()
    {
        _client?.Dispose();
        _client = null;
        _crypto = null;
        _password = "";
        _allowSessionTakeover = false;
        _tokenId = DefaultTokenId;
        return Task.CompletedTask;
    }

    private sealed record LoginParameters(
        string Modulus, string Exponent, long Sequence);
    private sealed record BusyState(bool IsLoggedIn, bool IsBusy);
    private sealed record RouterAction(
        int Type,
        string Oid,
        string Stack,
        string ParentStack,
        IReadOnlyList<string> Attributes);

    private sealed class CgiResponse
    {
        private readonly Dictionary<int, List<CgiObject>> _sections = new();

        public void Add(int section, CgiObject value)
        {
            if (!_sections.TryGetValue(section, out List<CgiObject>? values))
                _sections[section] = values = [];
            values.Add(value);
        }

        public IReadOnlyList<CgiObject> GetObjects(int section) =>
            _sections.TryGetValue(section, out List<CgiObject>? values)
                ? values
                : [];

        public CgiObject GetFirst(int section)
        {
            CgiObject? value = GetObjects(section).FirstOrDefault();
            return value ?? throw new RouterConnectionException(
                "The router did not return all required LTE status fields.");
        }

        public CgiObject GetFirstOrEmpty(int section) =>
            GetObjects(section).FirstOrDefault() ?? new CgiObject(ZeroStack);
    }

    private sealed class CgiObject(string stack)
    {
        public string Stack { get; } = stack;
        public Dictionary<string, string> Values { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public string? Get(string name) =>
            Values.TryGetValue(name, out string? value) ? value : null;
    }
}
