using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetPulse.Companion;
using NetPulseMonitor;

const string modulus =
    "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff" +
    "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
const string exponent = "01";
string? aesKey = null;
string? aesIv = null;
int statusRequests = 0;
bool managementSessionActive = false;
bool managementSessionBusy = false;
bool rejectNextStatusAsExpired = false;
var writeRequests = new List<string>();
var logoutRequests = new List<string>();
var smsRequests = new List<string>();
int sendResultPolls = 0;
bool firstInboxUnread = true;
string mockModel = "Archer MR600";
string legacyMobileNetworkMode = "3";
string modernMobileNetworkMode = "5G/4G";
var deletedInboxStacks = new HashSet<string>(StringComparer.Ordinal);
var deletedSentStacks = new HashSet<string>(StringComparer.Ordinal);
var deletedDraftStacks = new HashSet<string>(StringComparer.Ordinal);

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
WebApplication app = builder.Build();

app.MapPost("/cgi/getBusy", () => Results.Json(new
{
    isLogined = managementSessionActive ? 1 : 0,
    isBusy = managementSessionBusy ? 1 : 0
}));
app.MapPost("/cgi/getParm", () => Results.Text(
    $"nn=\"{modulus}\"; ee=\"{exponent}\"; seq=\"1000\"; userSetting=0;"));
app.MapPost("/cgi/login", (HttpRequest request) =>
{
    string signatureText = DecodeNoPaddingRsa(request.Query["sign"]!);
    Dictionary<string, string> signature = ParsePairs(signatureText);
    aesKey = signature["key"];
    aesIv = signature["iv"];
    string loginText = DecryptAes(request.Query["data"]!, aesKey, aesIv);
    if (loginText != "admin\nmock-password")
        return Results.Text("$.ret=71233;");
    return Results.Text("$.ret=0;");
});
app.MapGet("/", (HttpRequest request) =>
{
    Require(request.Cookies["loginErrorShow"] == "1",
        "Login compatibility cookie was not sent.");
    return Results.Text(
        "<html><script>var token='mock-token';</script></html>", "text/html");
});
app.MapPost("/cgi_gdpr", async (HttpRequest request) =>
{
    if (rejectNextStatusAsExpired)
    {
        rejectNextStatusAsExpired = false;
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    Require(request.Headers["TokenID"] == "mock-token", "Token header missing.");
    Require(aesKey is not null && aesIv is not null, "AES session was not created.");
    using var reader = new StreamReader(request.Body, Encoding.UTF8);
    string encryptedBody = await reader.ReadToEndAsync();
    Dictionary<string, string> fields = ParseLines(encryptedBody);
    string plainRequest = DecryptAes(fields["data"], aesKey!, aesIv!);

    string plainResponse;
    if (plainRequest.Contains("/cgi/clearBusy", StringComparison.Ordinal) ||
        plainRequest.Contains("/cgi/logout", StringComparison.Ordinal))
    {
        logoutRequests.Add(plainRequest);
        managementSessionActive = false;
        plainResponse = "[error]0\r\n";
    }
    else if (plainRequest.Contains("LTE_SMS_SENDNEWMSG", StringComparison.Ordinal))
    {
        smsRequests.Add(plainRequest);
        if (plainRequest.Contains("sendResult", StringComparison.Ordinal))
        {
            sendResultPolls++;
            int sendResult = sendResultPolls switch
            {
                1 => 2,
                2 => 3,
                _ => 1
            };
            plainResponse =
                $"[0,0,0,0,0,0]0\r\nsendResult={sendResult}\r\n";
        }
        else
        {
            plainResponse = "[0,0,0,0,0,0]0\r\n";
        }
    }
    else if (plainRequest.Contains("LTE_SMS_SENDMSGENTRY", StringComparison.Ordinal))
    {
        smsRequests.Add(plainRequest);
        if (plainRequest.StartsWith("4\r\n", StringComparison.Ordinal))
        {
            deletedSentStacks.Add(ReadRequestStack(plainRequest));
            plainResponse = "[error]0\r\n";
        }
        else if (plainRequest.StartsWith("5\r\n", StringComparison.Ordinal) &&
                 deletedSentStacks.Count == 0)
            plainResponse = "[1,0,0,0,0,0]0\r\nindex=403\r\nto=+303333333333\r\n" +
                            "content=Sent message\r\nsendTime=2026-08-08 08:02:00\r\n";
        else
            plainResponse = "[error]0\r\n";
    }
    else if (plainRequest.Contains("LTE_SMS_SENDMSGBOX", StringComparison.Ordinal))
    {
        smsRequests.Add(plainRequest);
        plainResponse = plainRequest.Contains("totalNumber", StringComparison.Ordinal)
            ? $"[0,0,0,0,0,0]0\r\ntotalNumber={(deletedSentStacks.Count == 0 ? 1 : 0)}\r\namountPerPage=8\r\n"
            : "[0,0,0,0,0,0]0\r\n";
    }
    else if (plainRequest.Contains("LTE_SMS_DRAFTMSGENTRY", StringComparison.Ordinal))
    {
        smsRequests.Add(plainRequest);
        if (plainRequest.StartsWith("4\r\n", StringComparison.Ordinal))
        {
            deletedDraftStacks.Add(ReadRequestStack(plainRequest));
            plainResponse = "[error]0\r\n";
        }
        else if (plainRequest.StartsWith("5\r\n", StringComparison.Ordinal) &&
                 deletedDraftStacks.Count == 0)
            plainResponse = "[1,0,0,0,0,0]0\r\nindex=404\r\nto=+304444444444\r\n" +
                            "content=Draft message\r\n";
        else
            plainResponse = "[error]0\r\n";
    }
    else if (plainRequest.Contains("LTE_SMS_DRAFTMSGBOX", StringComparison.Ordinal))
    {
        smsRequests.Add(plainRequest);
        plainResponse = plainRequest.Contains("totalNumber", StringComparison.Ordinal)
            ? $"[0,0,0,0,0,0]0\r\ntotalNumber={(deletedDraftStacks.Count == 0 ? 1 : 0)}\r\namountPerPage=8\r\n"
            : "[0,0,0,0,0,0]0\r\n";
    }
    else if (plainRequest.Contains("LTE_SMS_RECVMSGENTRY", StringComparison.Ordinal))
    {
        smsRequests.Add(plainRequest);
        if (plainRequest.StartsWith("2\r\n", StringComparison.Ordinal))
        {
            firstInboxUnread = plainRequest.Contains("unread=1", StringComparison.Ordinal);
            plainResponse = "[error]0\r\n";
        }
        else if (plainRequest.StartsWith("4\r\n", StringComparison.Ordinal))
        {
            deletedInboxStacks.Add(ReadRequestStack(plainRequest));
            plainResponse = "[error]0\r\n";
        }
        else if (plainRequest.StartsWith("5\r\n", StringComparison.Ordinal))
        {
            var response = new StringBuilder();
            if (!deletedInboxStacks.Contains("1,0,0,0,0,0"))
                response.Append("[1,0,0,0,0,0]0\r\nindex=437\r\nfrom=+301111111111\r\n")
                    .Append("content=First message\r\nreceivedTime=2026-08-08 08:00:00\r\nunread=")
                    .Append(firstInboxUnread ? "1\r\n" : "0\r\n");
            if (!deletedInboxStacks.Contains("2,0,0,0,0,0"))
                response.Append("[2,0,0,0,0,0]0\r\nindex=438\r\nfrom=+302222222222\r\n")
                    .Append("content=Second message\r\nreceivedTime=2026-08-08 08:01:00\r\nunread=0\r\n");
            plainResponse = response.Append("[error]0\r\n").ToString();
        }
        else
            plainResponse = "[error]0\r\n";
    }
    else if (plainRequest.Contains("LTE_SMS_RECVMSGBOX", StringComparison.Ordinal))
    {
        smsRequests.Add(plainRequest);
        plainResponse = plainRequest.Contains("totalNumber", StringComparison.Ordinal)
            ? $"[0,0,0,0,0,0]0\r\ntotalNumber={2 - deletedInboxStacks.Count}\r\namountPerPage=8\r\n"
            : "[0,0,0,0,0,0]0\r\n";
    }
    else if (plainRequest.StartsWith("2&2\r\n", StringComparison.Ordinal))
    {
        writeRequests.Add(plainRequest);
        plainResponse =
            "[1,1,0,0,0,0]0\r\n" +
            "[0,0,0,0,0,0]1\r\n";
    }
    else if (plainRequest.Contains("DEV2_LTE_WAN_CFG", StringComparison.Ordinal))
    {
        if (plainRequest.StartsWith("2\r\n", StringComparison.Ordinal))
        {
            modernMobileNetworkMode = ReadRequestValue(
                plainRequest, "networkPreferredModeSelected");
            writeRequests.Add(plainRequest);
        }
        plainResponse =
            "[1,0,0,0,0,0]0\r\n" +
            "networkPreferredModeOptionList=5G/4G,5G Preferred,5G Only," +
            "4G Preferred,4G Only,3G Only\r\n" +
            $"networkPreferredModeSelected={modernMobileNetworkMode}\r\n";
    }
    else if (plainRequest.Contains("LTE_WAN_CFG", StringComparison.Ordinal) &&
             plainRequest.Contains("networkPreferredMode", StringComparison.Ordinal))
    {
        if (plainRequest.StartsWith("2\r\n", StringComparison.Ordinal))
        {
            legacyMobileNetworkMode = ReadRequestValue(
                plainRequest, "networkPreferredMode");
            writeRequests.Add(plainRequest);
        }
        plainResponse =
            "[1,1,0,0,0,0]0\r\n" +
            $"networkPreferredMode={legacyMobileNetworkMode}\r\n";
    }
    else if (plainRequest.Contains("WAN_COMMON_INTF_CFG", StringComparison.Ordinal))
    {
        plainResponse =
            "[1,0,0,0,0,0]0\r\n" +
            "WANAccessType=LTE\r\n";
    }
    else if (plainRequest.Contains("LAN_HOST_ENTRY", StringComparison.Ordinal))
    {
        plainResponse =
            "[1,0,0,0,0,0]0\r\nIPAddress=192.168.1.20\r\n" +
            "MACAddress=aa-bb-cc-dd-ee-01\r\nhostName=Living room TV\r\n" +
            "X_TP_ConnType=wireless\r\nactive=1\r\n" +
            "[2,0,0,0,0,0]0\r\nIPAddress=192.168.1.21\r\n" +
            "MACAddress=AABBCCDDEE02\r\nhostName=\r\n" +
            "X_TP_ConnType=Ethernet\r\nactive=1\r\n" +
            "[3,0,0,0,0,0]0\r\nIPAddress=192.168.1.99\r\n" +
            "MACAddress=AABBCCDDEE99\r\nhostName=Offline client\r\n" +
            "X_TP_ConnType=wireless\r\nactive=0\r\n";
    }
    else if (plainRequest.Contains("bandSelectSwitch", StringComparison.Ordinal) &&
             plainRequest.Contains("rfInfoCellIDLock", StringComparison.Ordinal))
    {
        plainResponse =
            "[1,1,0,0,0,0]0\r\n" +
            "bandSelectSwitch=0\r\nbandSelectedMaskL=0\r\nbandSelectedMaskH=0\r\n" +
            "[0,0,0,0,0,0]1\r\n" +
            "rfInfoCellIDLock=0\r\nrfInfoCellID=\r\n" +
            "rfInfoEARFCN=0\r\nrfInfoPCI=0\r\n";
    }
    else
    {
        statusRequests++;
        bool exposeLiveIdentity = statusRequests < 3;
        plainResponse =
            "[1,1,0,0,0,0]0\r\n" +
            "simStatus=3\r\nroamingStatus=0\r\nsignalStrength=4\r\n" +
            "networkType=3\r\nconnectStatus=4\r\n" +
            "[1,0,0,0,0,0]1\r\n" +
            "totalStatistics=2147483648\r\ncurRxSpeed=1234567\r\ncurTxSpeed=765432\r\n" +
            "[1,1,0,0,0,0]2\r\n" +
            "regStat=1\r\nrfInfoBand=122\r\nrfInfoRsrq=-9\r\n" +
            "rfInfoRsrp=-97\r\nrfInfoSnr=123\r\nrfInfoRssi=-68\r\n" +
            "rfInfoChannel=1300\r\n" +
            (exposeLiveIdentity
                ? "rfInfoCellID=123456789\r\nrfInfoPCellBand=3\r\n" +
                  "rfInfoPCellChannel=1300\r\nrfInfoPCI=321\r\n"
                : "rfInfoCellID=0\r\nrfInfoPCellBand=3\r\n" +
                  "rfInfoPCellChannel=0\r\nrfInfoPCI=0\r\n") +
            "smsUnreadCount=2\r\n" +
            "[1,1,0,0,0,0]3\r\n" +
            "ispName=Test Carrier\r\nspn=Test\r\n" +
            "[0,0,0,0,0,0]4\r\n" +
            (statusRequests == 3
                ? "rfInfoCellID=A1B2C\r\nrfInfoEARFCN=1300\r\nrfInfoPCI=255\r\n"
                : "rfInfoCellID=A1B2C\r\nrfInfoEARFCN=500\r\nrfInfoPCI=255\r\n") +
            "[0,0,0,0,0,0]5\r\n" +
            $"modelName={mockModel}\r\nhardwareVersion={mockModel} v5\r\n" +
            "softwareVersion=1.5.0 mock\r\n";
    }

    return Results.Text(EncryptAes(plainResponse, aesKey!, aesIv!), "text/plain");
});
app.MapGet("/cgi/logout", () => Results.Text(""));

await app.StartAsync();
IServer server = app.Services.GetRequiredService<IServer>();
string address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();

try
{
    await using var provider = new TpLinkMr600Provider();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    RouterCapabilities capabilities = await provider.ConnectAsync(
        new RouterConnectionOptions
        {
            RouterUri = new Uri(address),
            Password = "mock-password"
        },
        timeout.Token);
    RouterTelemetry telemetry = await provider.ReadAsync(timeout.Token);

    Require(capabilities.SupportsLteTelemetry, "LTE capability was not detected.");
    Require(telemetry.IsConnected, "Connected LTE state was not parsed.");
    Require(telemetry.Isp == "Test Carrier", "ISP was not parsed.");
    Require(telemetry.Band == "B3", "LTE band mapping was not parsed.");
    Require(telemetry.SignalPercent == 100, "Signal strength was not parsed.");
    Require(telemetry.SnrDb == 12.3D, "SNR scaling was not parsed.");
    Require(telemetry.CellId == "123456789" && telemetry.Earfcn == "1300" &&
            telemetry.Pci == "321",
        "The serving-cell status was not preferred over empty Cell Lock values.");
    Require(telemetry.PrimaryBand == "B3",
        "The serving PCell band was not parsed independently of carrier aggregation.");
    RouterTelemetry fallbackTelemetry = await provider.ReadAsync(timeout.Token);
    Require(fallbackTelemetry.Earfcn == "1300" &&
            fallbackTelemetry.Pci == "255" &&
            fallbackTelemetry.CellId == "A1B2C",
        "A matching LTE_CELL_LOCK status identity must fill CID/PCI when this " +
        "MR600 firmware omits them from LTE_NET_STATUS.");
    Require(telemetry.UnreadSmsCount == 2,
        "The unread SMS count was not parsed from LTE status.");
    Require(telemetry.TotalBytes == 2147483648L, "64-bit data usage was not parsed.");
    Require(statusRequests >= 2, "Expected status reads were not sent.");

    IReadOnlyList<RouterConnectedDevice> devices =
        await provider.ReadConnectedDevicesAsync(timeout.Token);
    Require(devices.Count == 2,
        "Only active TP-Link LAN clients should be returned.");
    Require(devices[0].Name == "Living room TV" &&
            devices[0].MacAddress == "AA:BB:CC:DD:EE:01" &&
            devices[0].ConnectionType == "Wi-Fi",
        "Connected-device identity and connection type were not normalized.");
    Require(devices[1].Name == "Unknown device" &&
            devices[1].ConnectionType == "Ethernet",
        "Unnamed Ethernet clients should remain visible with a safe label.");

    RouterMobileNetworkModeState legacyMode =
        await provider.ReadMobileNetworkModeAsync(timeout.Token);
    Require(legacyMode.CurrentValue == "3" && legacyMode.SupportedModes.Count == 3,
        "MR600 network mode and supported options were not parsed.");
    Require(legacyMode.SupportedModes.All(mode =>
            !mode.DisplayName.Contains("5G", StringComparison.OrdinalIgnoreCase)),
        "A 4G+ MR600 must never be offered a 5G network mode.");
    await provider.SetMobileNetworkModeAsync("2", timeout.Token);
    Require(legacyMobileNetworkMode == "2" && writeRequests.Any(request =>
            request.Contains("LTE_WAN_CFG", StringComparison.Ordinal) &&
            request.Contains("networkPreferredMode=2", StringComparison.Ordinal)),
        "MR600 4G-only mode was not written and confirmed.");

    RouterTelemetry identityHidden = await provider.ReadAsync(timeout.Token);
    Require(identityHidden.Earfcn == "1300" && identityHidden.Pci == "-" &&
            identityHidden.CellId == "-",
        "Configured LTE Cell Lock values must never be reported as the live " +
        "serving PCell identity when their EARFCN does not match the live channel.");

    IReadOnlyList<RouterSmsMessage> timeline =
        await provider.ReadSmsTimelineAsync(timeout.Token);
    Require(timeline.Count == 4 &&
            timeline[0].Folder == RouterSmsFolder.Sent &&
            timeline[1].Folder == RouterSmsFolder.Inbox &&
            timeline[2].Folder == RouterSmsFolder.Inbox &&
            timeline[3].Folder == RouterSmsFolder.Draft,
        "Inbox, sent messages and drafts should share one chronological timeline.");
    RouterSmsMessage unreadMessage = timeline.Single(message => message.IsUnread);
    await provider.SetSmsUnreadAsync(
        unreadMessage.Stack, unreadMessage.Index, unreadMessage.PageNumber,
        false, timeout.Token);
    Require(smsRequests.Any(item =>
            item.Contains("LTE_SMS_RECVMSGENTRY", StringComparison.Ordinal) &&
            item.Contains("unread=0", StringComparison.Ordinal)),
        "Opening an unread SMS should mark only that router entry as read.");
    await provider.SetSmsUnreadAsync(
        unreadMessage.Stack, unreadMessage.Index, unreadMessage.PageNumber,
        true, timeout.Token);
    Require(smsRequests.Any(item =>
            item.Contains("LTE_SMS_RECVMSGENTRY", StringComparison.Ordinal) &&
            item.Contains("unread=1", StringComparison.Ordinal)),
        "The selected Inbox SMS should be writable back to unread state.");
    foreach (RouterSmsMessage message in timeline
                 .GroupBy(item => item.Folder)
                 .Select(group => group.First()))
    {
        await provider.DeleteSmsAsync(
            message.Folder, message.Stack, message.Index, message.PageNumber,
            timeout.Token);
    }
    Require(smsRequests.Any(item =>
            item.StartsWith("4\r\n", StringComparison.Ordinal) &&
            item.Contains("LTE_SMS_RECVMSGENTRY", StringComparison.Ordinal)) &&
        smsRequests.Any(item =>
            item.StartsWith("4\r\n", StringComparison.Ordinal) &&
            item.Contains("LTE_SMS_SENDMSGENTRY", StringComparison.Ordinal)) &&
        smsRequests.Any(item =>
            item.StartsWith("4\r\n", StringComparison.Ordinal) &&
            item.Contains("LTE_SMS_DRAFTMSGENTRY", StringComparison.Ordinal)),
        "Delete should target the selected Inbox, Sent, or Draft entry with ACT_DEL.");
    Require(smsRequests.Any(item =>
            item.StartsWith("5\r\n", StringComparison.Ordinal) &&
            item.Contains("LTE_SMS_RECVMSGENTRY", StringComparison.Ordinal)),
        "SMS entry lists must use the MR600 firmware's ACT_GL action.");
    Require(smsRequests.Any(item =>
            item.Contains("LTE_SMS_RECVMSGENTRY#1,0,0,0,0,0#",
                StringComparison.Ordinal) &&
            (item.StartsWith("2\r\n", StringComparison.Ordinal) ||
             item.StartsWith("4\r\n", StringComparison.Ordinal))),
        "SMS mutations must use the firmware-returned __stack, not the index field.");
    Require(smsRequests.Any(item =>
            item.Contains("PageNumber=0", StringComparison.Ordinal)) &&
        smsRequests.Any(item =>
            item.Contains("PageNumber=1", StringComparison.Ordinal)),
        "SMS paging must preserve the MR600 firmware's PageNumber casing and base.");
    await provider.SendSmsAsync(
        "+301234567890",
        "Mock line 1\r\nMock line 2",
        timeout.Token);
    Require(smsRequests.Any(item =>
            item.Contains("LTE_SMS_SENDNEWMSG", StringComparison.Ordinal) &&
            item.Contains("index=1", StringComparison.Ordinal) &&
            item.Contains("textContent=Mock line 1\u0011\u0012Mock line 2",
                StringComparison.Ordinal)),
        "SMS send fields and MR600 newline encoding were not generated correctly.");
    Require(sendResultPolls == 3,
        "MR600 transient SMS states should be polled until final confirmation.");
    await provider.SaveSmsDraftAsync(
        "+301234567890",
        "Unsent draft",
        timeout.Token);
    Require(smsRequests.Any(item =>
            item.Contains("LTE_SMS_SENDNEWMSG", StringComparison.Ordinal) &&
            item.Contains("index=2", StringComparison.Ordinal) &&
            item.Contains("textContent=Unsent draft", StringComparison.Ordinal)),
        "Saving a draft should use the MR600 draft index without sending it.");

    RouterLockState originalLock = await provider.ReadLockStateAsync(timeout.Token);
    Require(!originalLock.CellLockEnabled && !originalLock.BandSelectionEnabled,
        "Automatic selection state was not parsed.");
    Require(
        LteRadioIdentifier.TryNormalizeCellId("0x000abcde", out string? normalizedCid) &&
        normalizedCid == "ABCDE",
        "Hexadecimal CID normalization should accept 0x prefixes and letters.");
    Require(!LteRadioIdentifier.TryNormalizeCellId("ABC-G5", out _),
        "CID normalization should reject non-hexadecimal characters.");
    Require(LteRadioIdentifier.TryNormalizeCellId("FFFFFFFF", out string? missingCid) &&
            missingCid is null,
        "The MR600 unknown-CID sentinel must remain missing, not become a cell identity.");
    await provider.ApplyCellAndBandLockAsync(
        new RouterCellLockTarget
        {
            Bands = [3, 7],
            Earfcn = "1300",
            Pci = "321",
            CellId = null
        },
        timeout.Token);
    string applyRequest = writeRequests.Last();
    Require(applyRequest.StartsWith("2&2\r\n", StringComparison.Ordinal),
        "Cell and band locks should be sent as two ordered writes.");
    Require(applyRequest.Contains("bandSelectedMaskL=68", StringComparison.Ordinal),
        "Band 3 + 7 mask was not encoded correctly.");
    Require(applyRequest.Contains("rfInfoCellID=\r\n", StringComparison.Ordinal),
        "Optional CID should be sent as an empty field.");
    Require(applyRequest.Contains("rfInfoEARFCN=1300", StringComparison.Ordinal) &&
            applyRequest.Contains("rfInfoPCI=321", StringComparison.Ordinal),
        "Cell Lock fields were not sent correctly.");
    await provider.ApplyCellAndBandLockAsync(
        new RouterCellLockTarget
        {
            Bands = [3, 20],
            Earfcn = "1700",
            Pci = "77",
            CellId = "ABCDE"
        },
        timeout.Token);
    string hexadecimalCidRequest = writeRequests.Last();
    Require(hexadecimalCidRequest.Contains(
            "rfInfoCellID=ABCDE", StringComparison.Ordinal),
        "An alphanumeric hexadecimal CID should be accepted by Cell Lock.");
    await provider.ApplyCellAndBandLockAsync(
        new RouterCellLockTarget
        {
            Bands = [32, 40],
            Earfcn = "1300",
            Pci = "321"
        },
        timeout.Token);
    string highBandRequest = writeRequests.Last();
    Require(highBandRequest.Contains(
            "bandSelectedMaskL=-2147483648", StringComparison.Ordinal) &&
            highBandRequest.Contains("bandSelectedMaskH=128", StringComparison.Ordinal),
        "Bands 32 and 40 were not encoded with MR600 signed masks.");

    await provider.ApplyCellAndBandLockAsync(
        new RouterCellLockTarget
        {
            Bands = [3, 20],
            Earfcn = "",
            Pci = ""
        },
        timeout.Token);
    string bandOnlyRequest = writeRequests.Last();
    Require(bandOnlyRequest.Contains("bandSelectedMaskL=524292", StringComparison.Ordinal) &&
            bandOnlyRequest.Contains("rfInfoCellIDLock=0", StringComparison.Ordinal) &&
            !bandOnlyRequest.Contains("rfInfoEARFCN=", StringComparison.Ordinal),
        "Band-only optimization must leave cell selection automatic.");

    bool invalidPciBlocked = false;
    try
    {
        await provider.ApplyCellAndBandLockAsync(
            new RouterCellLockTarget
            {
                Bands = [3],
                Earfcn = "1300",
                Pci = "513"
            },
            timeout.Token);
    }
    catch (RouterConnectionException)
    {
        invalidPciBlocked = true;
    }
    Require(invalidPciBlocked, "Out-of-range PCI should be blocked before a write.");
    await provider.RestoreLockStateAsync(originalLock, timeout.Token);
    string restoreRequest = writeRequests.Last();
    Require(restoreRequest.Contains("bandSelectSwitch=0", StringComparison.Ordinal) &&
            restoreRequest.Contains("rfInfoCellIDLock=0", StringComparison.Ordinal),
        "Original automatic-selection state was not restored.");

    mockModel = "Archer NX200";
    await using (var modernModeProvider = new TpLinkMr600Provider())
    {
        await modernModeProvider.ConnectAsync(
            new RouterConnectionOptions
            {
                RouterUri = new Uri(address),
                Password = "mock-password"
            },
            timeout.Token);
        RouterMobileNetworkModeState modernMode =
            await modernModeProvider.ReadMobileNetworkModeAsync(timeout.Token);
        Require(modernMode.SupportedModes.Count == 6 &&
                modernMode.SupportedModes.Any(mode => mode.Value == "5G Only") &&
                modernMode.CurrentValue == "5G/4G",
            "A 5G TP-Link firmware option list was not detected dynamically.");
        await modernModeProvider.SetMobileNetworkModeAsync(
            "5G Only", timeout.Token);
        Require(modernMobileNetworkMode == "5G Only" && writeRequests.Any(request =>
                request.Contains("DEV2_LTE_WAN_CFG", StringComparison.Ordinal) &&
                request.Contains(
                    "networkPreferredModeSelected=5G Only",
                    StringComparison.Ordinal)),
            "The 5G-only mode reported by modern TP-Link firmware was not applied.");
    }
    mockModel = "Archer MR600";

    bool wrongPasswordStopped = false;
    await using (var wrongPasswordProvider = new TpLinkMr600Provider())
    {
        try
        {
            await wrongPasswordProvider.ConnectAsync(
                new RouterConnectionOptions
                {
                    RouterUri = new Uri(address),
                    Password = "wrong-password"
                },
                timeout.Token);
        }
        catch (RouterAuthenticationException)
        {
            wrongPasswordStopped = true;
        }
    }
    Require(wrongPasswordStopped, "Wrong credentials were not stopped safely.");

    managementSessionActive = true;
    bool occupiedSessionBlocked = false;
    await using (var nonTakeoverProvider = new TpLinkMr600Provider())
    {
        try
        {
            await nonTakeoverProvider.ConnectAsync(
                new RouterConnectionOptions
                {
                    RouterUri = new Uri(address),
                    Password = "mock-password"
                },
                timeout.Token);
        }
        catch (RouterBusyException)
        {
            occupiedSessionBlocked = true;
        }
    }
    Require(occupiedSessionBlocked,
        "An occupied session should require explicit takeover permission.");

    await using (var takeoverProvider = new TpLinkMr600Provider())
    {
        RouterCapabilities takeoverCapabilities = await takeoverProvider.ConnectAsync(
            new RouterConnectionOptions
            {
                RouterUri = new Uri(address),
                Password = "mock-password",
                AllowSessionTakeover = true
            },
            timeout.Token);
        Require(takeoverCapabilities.SupportsLteTelemetry,
            "Explicit session takeover did not reach LTE telemetry.");
    }
    Require(logoutRequests.Any(item =>
            item.Contains("/cgi/clearBusy", StringComparison.Ordinal)) &&
            logoutRequests.Any(item =>
                item.Contains("/cgi/logout", StringComparison.Ordinal)),
        "MR600 encrypted clearBusy/logout sequence was not sent.");

    managementSessionActive = true;
    managementSessionBusy = true;
    await using (var busyTakeoverProvider = new TpLinkMr600Provider())
    {
        RouterCapabilities busyTakeoverCapabilities =
            await busyTakeoverProvider.ConnectAsync(
                new RouterConnectionOptions
                {
                    RouterUri = new Uri(address),
                    Password = "mock-password",
                    AllowSessionTakeover = true
                },
                timeout.Token);
        Require(busyTakeoverCapabilities.SupportsLteTelemetry,
            "Explicit takeover did not proceed after the bounded busy wait.");
    }
    managementSessionBusy = false;

    managementSessionActive = true;
    rejectNextStatusAsExpired = true;
    bool replacedSessionYielded = false;
    try
    {
        await provider.ReadAsync(timeout.Token);
    }
    catch (RouterBusyException)
    {
        replacedSessionYielded = true;
    }
    Require(replacedSessionYielded,
        "A replaced NetPulse session should not silently take control back.");
    managementSessionActive = false;

    await using (var persistentTakeoverProvider = new TpLinkMr600Provider())
    {
        await persistentTakeoverProvider.ConnectAsync(
            new RouterConnectionOptions
            {
                RouterUri = new Uri(address),
                Password = "mock-password",
                AllowSessionTakeover = true
            },
            timeout.Token);
        managementSessionActive = true;
        rejectNextStatusAsExpired = true;
        RouterTelemetry recovered = await persistentTakeoverProvider.ReadAsync(
            timeout.Token);
        Require(recovered.IsConnected,
            "A monitoring provider with session priority should retake a replaced session.");
        managementSessionActive = false;
    }

    bool publicAddressBlocked = false;
    try
    {
        await provider.ConnectAsync(
            new RouterConnectionOptions
            {
                RouterUri = new Uri("http://8.8.8.8/"),
                Password = "unused"
            },
            timeout.Token);
    }
    catch (RouterConnectionException ex) when (
        ex.Message.Contains("private LAN", StringComparison.Ordinal))
    {
        publicAddressBlocked = true;
    }
    Require(publicAddressBlocked, "Public router destinations were not blocked.");
    TestCellHistoryRanking();
    TestAutoLockPolicy();
    TestAutomaticSpeedTests();
    await TestCompanionProtocolAsync();
    TestSettingsMigration();
    TestRegionalClock();
    TestUnreadSmsAlerts();
    TestBandDiscovery();
    TestExperienceServices();
    Console.WriteLine(
        "TP-Link protocol, SMS, LTE history, Band & Cell Discovery, optimizer, health, troubleshooting, update, automatic speed-test, and regional-time tests passed.");
}
finally
{
    await app.StopAsync();
    await app.DisposeAsync();
}

static string DecodeNoPaddingRsa(string encryptedHex)
{
    var result = new StringBuilder();
    for (int offset = 0; offset < encryptedHex.Length; offset += 128)
    {
        string blockHex = encryptedHex.Substring(offset,
            Math.Min(128, encryptedHex.Length - offset));
        byte[] block = Convert.FromHexString(blockHex);
        int length = Array.FindLastIndex(block, value => value != 0) + 1;
        result.Append(Encoding.UTF8.GetString(block, 0, length));
    }
    return result.ToString();
}

static Dictionary<string, string> ParsePairs(string value) =>
    value.Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(item => item.Split('=', 2))
        .ToDictionary(item => item[0], item => item.Length > 1 ? item[1] : "",
            StringComparer.OrdinalIgnoreCase);

static Dictionary<string, string> ParseLines(string value) =>
    value.Split(["\r\n"], StringSplitOptions.RemoveEmptyEntries)
        .Select(item => item.Split('=', 2))
        .ToDictionary(item => item[0], item => item.Length > 1 ? item[1] : "",
            StringComparer.OrdinalIgnoreCase);

static string ReadRequestStack(string request)
{
    int firstHash = request.IndexOf('#');
    int secondHash = firstHash < 0 ? -1 : request.IndexOf('#', firstHash + 1);
    return firstHash >= 0 && secondHash > firstHash
        ? request[(firstHash + 1)..secondHash]
        : "";
}

static string ReadRequestValue(string request, string name)
{
    string prefix = name + "=";
    string? line = request.Split(["\r\n"], StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault(candidate =>
            candidate.StartsWith(prefix, StringComparison.Ordinal));
    return line is null ? "" : line[prefix.Length..];
}

static string EncryptAes(string plainText, string key, string iv)
{
    using Aes aes = Aes.Create();
    aes.Mode = CipherMode.CBC;
    aes.Padding = PaddingMode.PKCS7;
    aes.Key = Encoding.UTF8.GetBytes(key);
    aes.IV = Encoding.UTF8.GetBytes(iv);
    using ICryptoTransform transform = aes.CreateEncryptor();
    byte[] plain = Encoding.UTF8.GetBytes(plainText);
    return Convert.ToBase64String(transform.TransformFinalBlock(plain, 0, plain.Length));
}

static string DecryptAes(string encryptedText, string key, string iv)
{
    using Aes aes = Aes.Create();
    aes.Mode = CipherMode.CBC;
    aes.Padding = PaddingMode.PKCS7;
    aes.Key = Encoding.UTF8.GetBytes(key);
    aes.IV = Encoding.UTF8.GetBytes(iv);
    using ICryptoTransform transform = aes.CreateDecryptor();
    byte[] encrypted = Convert.FromBase64String(encryptedText);
    return Encoding.UTF8.GetString(
        transform.TransformFinalBlock(encrypted, 0, encrypted.Length));
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void TestRegionalClock()
{
    var settings = new AppSettings
    {
        CountryCode = "GR",
        CountryCultureName = "el-GR",
        OfficialTimeZoneId = "GTB Standard Time"
    };
    settings.Normalize();
    var clock = new OfficialClock(settings);

    Require(
        clock.FormatCsv(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc)) ==
        "2026-01-15T14:00:00+02:00",
        "Greek official winter timestamps must be UTC+02:00 regardless of the PC clock.");
    Require(
        clock.FormatCsv(new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc)) ==
        "2026-07-15T15:00:00+03:00",
        "Greek official summer timestamps must apply daylight-saving time.");
}

static void TestUnreadSmsAlerts()
{
    string folder = Path.Combine(
        Path.GetTempPath(), "NetPulseMonitorTests", Guid.NewGuid().ToString("N"));
    string historyPath = Path.Combine(folder, "sms-notification-hashes.txt");
    try
    {
        var tracker = new UnreadSmsAlertTracker(historyPath);
        IReadOnlyList<string> first = tracker.FindNew(
            ["Inbox|1", "Inbox|2", "Inbox|3"]);
        Require(first.SequenceEqual(["Inbox|1", "Inbox|2", "Inbox|3"]),
            "Every newly discovered unread message should get its own notification.");
        for (int sample = 0; sample < 20; sample++)
        {
            Require(tracker.FindNew(["Inbox|1", "Inbox|2", "Inbox|3"]).Count == 0,
                "The same unread identities must never repeat a notification.");
        }
        Require(tracker.FindNew(["Inbox|2", "Inbox|3", "Inbox|4"])
                .SequenceEqual(["Inbox|4"]),
            "A genuinely new unread identity should receive exactly one notification.");

        var restartedTracker = new UnreadSmsAlertTracker(historyPath);
        Require(restartedTracker.FindNew(
                ["Inbox|1", "Inbox|2", "Inbox|3", "Inbox|4"]).Count == 0,
            "Unread SMS notifications must not repeat after an app restart.");
        Require(File.ReadAllLines(historyPath).All(value =>
                value.Length == 64 && value.All(char.IsAsciiHexDigit)),
            "Notification history must contain only privacy-safe hashes.");
    }
    finally
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }
}

static void TestCellHistoryRanking()
{
    Require(!LteCellHistoryStore.IsVisibleToUser(
            Recommendation(
                0,
                50,
                10,
                TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1))),
        "A connection shorter than five minutes should remain hidden from history.");
    Require(LteCellHistoryStore.IsVisibleToUser(
            Recommendation(0, 50, 10, TimeSpan.FromMinutes(5))),
        "A connection should appear after exactly five connected minutes.");
    Require(LteCellHistoryStore.IsVisibleToUser(
            Recommendation(0, 0, 0, TimeSpan.Zero, userAdded: true)),
        "An unmeasured manual profile should remain available to the user.");
    Require(LteCellHistoryStore.IsVisibleToUser(
            Recommendation(0, 0, 0, TimeSpan.Zero, discoveryCandidate: true)),
        "An exact lock-ready discovery candidate should appear immediately.");

    string testFolder = Path.Combine(
        Path.GetTempPath(),
        "NetPulseMonitorTests",
        Guid.NewGuid().ToString("N"));
    string historyPath = Path.Combine(testFolder, "history.json");

    try
    {
        using var history = new LteCellHistoryStore(historyPath);
        DateTime started = DateTime.UtcNow.AddHours(-1);

        RecordCell(history, started, "B1", "500", "10", "111", 600);
        history.RecordSpeedTest(
            Telemetry(started.AddSeconds(600), "B1", "500", "10", "111"),
            Speed(200, 40));
        history.RecordConfirmedOutage(started.AddSeconds(601));

        DateTime secondStart = started.AddSeconds(700);
        RecordCell(history, secondStart, "B3", "1300", "321", "222", 600);
        history.RecordSpeedTest(
            Telemetry(secondStart.AddSeconds(600), "B3", "1300", "321", "222"),
            Speed(50, 10));

        IReadOnlyList<LteCellRecommendation> ranked = history.GetRecommendations();
        Require(ranked.Count == 2, "Every retained LTE profile must have a CID.");
        Require(ranked.All(item => item.CellId is not null),
            "CID-less telemetry must not enter measured LTE history.");
        Require(history.GetActiveProfileKey() == ranked.Single(item =>
                item.Band == "B3").Key,
            "The history store should expose the currently used profile for highlighting.");
        IReadOnlyList<LteCellRecommendation> observedLocks =
            history.GetObservedLockProfiles();
        Require(observedLocks.Count == 2 && observedLocks[0].Band == "B3",
            "Cell Lock choices should show stable observed sets with the active set first.");

        DateTime thirdStart = secondStart.AddSeconds(700);
        RecordCell(history, thirdStart, "B3", "1300", "321", "AAA01", 10);
        RecordCell(history, thirdStart.AddSeconds(20), "B3", "1300", "321", "BBB02", 10);
        Require(history.GetRecommendations().Count(item =>
                    item.Band == "B3" && item.Earfcn == "1300" && item.Pci == "321") == 3,
            "Identical bands/EARFCN/PCI with different CIDs must remain separate profiles.");

        string shortHistoryPath = Path.Combine(testFolder, "short-history.json");
        using (var shortHistory = new LteCellHistoryStore(shortHistoryPath))
        {
            RecordCell(shortHistory, started, "B20", "6400", "55", "333", 299);
            IReadOnlyList<LteCellRecommendation> shortCandidates =
                shortHistory.GetObservedLockProfiles();
            Require(shortCandidates.Count == 1 &&
                    shortCandidates[0].DiscoveryCandidate,
                "An exact short observation should be retained immediately as a " +
                "lock-ready candidate while its measurements remain ineligible.");
        }

        string discoveryCandidatePath = Path.Combine(
            testFolder,
            "discovery-candidates.json");
        using (var candidates = new LteCellHistoryStore(discoveryCandidatePath))
        {
            Require(candidates.AddDiscoveryCandidate(
                    "B3 + B20",
                    "1700",
                    "100",
                    "ABCDE"),
                "A verified discovery identity should be added to LTE History.");
            LteCellRecommendation candidate = candidates.GetRecommendations().Single();
            Require(candidate.DiscoveryCandidate && !candidate.IsEligible &&
                    candidate.AverageDownloadMbps is null &&
                    candidate.AverageUploadMbps is null,
                "Discovery candidates must be lock-ready but unranked and unmeasured.");
            Require(candidates.GetObservedLockProfiles().Single().Key == candidate.Key,
                "The discovered profile should be selectable for Cell Lock immediately.");
            Require(candidates.DeleteProfile(candidate.Key) &&
                    candidates.GetRecommendations().Count == 0,
                "Individual LTE History deletion should remove only the selected profile.");

            Require(!candidates.AddDiscoveryCandidate(
                    "B8",
                    "3501",
                    "-",
                    "-"),
                "Discovery candidates without CID must not enter LTE History.");
        }

        string discoveryCsv = Path.Combine(testFolder, "band-cell-discovery.csv");
        File.WriteAllText(
            discoveryCsv,
            "Timestamp,ScanId,RouterModel,HardwareVersion,RequestedBand,ServingProfile,PrimaryBand,EARFCN,PCI,CellId,RSRPdBm,RSRQdB,SNRdB,Samples,Status\n" +
            "2024-01-01T12:00:00+00:00,synthetic-scan,DemoRouter,Test,B8,B8,B8,3501,,,-90,-10,12,5,Serving cell observed\n" +
            "2024-01-01T12:01:00+00:00,synthetic-scan,DemoRouter,Test,B40,-,-,,,,,,,0,No serving cell observed\n");
        string importedHistoryPath = Path.Combine(
            testFolder,
            "imported-discovery-candidates.json");
        using (var imported = new LteCellHistoryStore(importedHistoryPath))
        {
            Require(imported.ImportDiscoveryCandidates(discoveryCsv) == 0 &&
                    imported.GetRecommendations().Count == 0,
                "Discovery CSV rows without CID must remain excluded from LTE History.");
        }

        string bandOnlyHistoryPath = Path.Combine(testFolder, "band-only-history.json");
        using (var bandOnlyHistory = new LteCellHistoryStore(bandOnlyHistoryPath))
        {
            DateTime bandOnlyStart = started.AddHours(4);
            RecordCell(
                bandOnlyHistory,
                bandOnlyStart,
                "B3 + B20",
                "1700",
                "-",
                "-",
                600);
            bandOnlyHistory.RecordSpeedTest(
                Telemetry(
                    bandOnlyStart.AddSeconds(600),
                    "B3 + B20",
                    "1700",
                    "-",
                    "-"),
                Speed(120, 20));
            IReadOnlyList<LteCellRecommendation> bandOnly =
                bandOnlyHistory.GetRecommendations();
            Require(bandOnly.Count == 0,
                "Telemetry without CID must not create an ambiguous LTE profile.");
        }

        string pcellHistoryPath = Path.Combine(testFolder, "pcell-history.json");
        using (var pcellHistory = new LteCellHistoryStore(pcellHistoryPath))
        {
            pcellHistory.RecordTelemetry(
                Telemetry(started, "B3", "1700", "42", "777"));
            pcellHistory.RecordTelemetry(
                Telemetry(started.AddSeconds(1), "B3 + B28", "-", "-", "-"));
            pcellHistory.RecordTelemetry(
                Telemetry(started.AddSeconds(2), "B3 + B28", "-", "-", "-"));
            pcellHistory.RecordTelemetry(
                Telemetry(started.AddSeconds(3), "B28 + B3", "9500", "-", "-"));
            pcellHistory.RecordTelemetry(
                Telemetry(started.AddSeconds(4), "B3 + B28", "6400", "-", "-"));
            IReadOnlyList<LteCellRecommendation> pcellRows =
                pcellHistory.GetRecommendations();
            LteCellRecommendation samePrimary = pcellRows.Single(item =>
                item.Band == "B3 + B28" && item.Earfcn == "1700");
            Require(samePrimary.PrimaryBand == "B3" && samePrimary.Pci == "42" &&
                    samePrimary.CellId == "777",
                "EARFCN/PCI/CID should carry only from the immediately previous " +
                "state when the PCell remains unchanged.");
            Require(!pcellRows.Any(item =>
                    item.Band == "B28 + B3" && item.Earfcn == "9500"),
                "A changed PCell without CID must not enter LTE History.");
            Require(!pcellRows.Any(item => item.PrimaryBand == "B3" && item.Earfcn == "6400"),
                "An EARFCN outside the serving PCell band must never enter history.");
        }

        string startupAggregationPath = Path.Combine(
            testFolder,
            "startup-aggregation-history.json");
        using (var startupAggregation = new LteCellHistoryStore(startupAggregationPath))
        {
            startupAggregation.RecordTelemetry(
                Telemetry(started, "B1 + B3", "500", "-", "-"));
            Require(startupAggregation.GetRecommendations().Count == 0,
                "An incomplete aggregated state at startup must wait for a real CID.");
            startupAggregation.RecordTelemetry(
                Telemetry(started.AddSeconds(1), "B1", "500", "77", "ABC01"));
            startupAggregation.RecordTelemetry(
                Telemetry(started.AddSeconds(2), "B1 + B3", "500", "-", "-"));
            LteCellRecommendation inherited = startupAggregation.GetRecommendations()
                .Single(item => item.Band == "B1 + B3");
            Require(inherited.Earfcn == "500" && inherited.Pci == "77" &&
                    inherited.CellId == "ABC01",
                "An aggregated state may inherit identity only from its immediately " +
                "preceding live PCell state.");
            Require(startupAggregation.GetRecommendations()
                    .Count(item => item.Band == "B1 + B3") == 1,
                "The completed CID-qualified aggregate must create exactly one row.");
        }

        string restartAggregationPath = Path.Combine(
            testFolder,
            "restart-aggregation-history.json");
        using (var restartAggregation = new LteCellHistoryStore(restartAggregationPath))
        {
            restartAggregation.RecordTelemetry(
                Telemetry(started, "B1 + B3", "500", "77", "ABC01"));
            restartAggregation.RecordTelemetry(new RouterTelemetry
            {
                Timestamp = started.AddSeconds(1),
                IsConnected = false,
                Status = "Disconnected"
            });
            restartAggregation.RecordTelemetry(
                Telemetry(started.AddSeconds(2), "B1 + B3", "500", "-", "-"));
            LteCellRecommendation[] rows = restartAggregation.GetRecommendations()
                .Where(item => item.Band == "B1 + B3" && item.Earfcn == "500")
                .ToArray();
            Require(rows.Length == 1 && rows[0].Pci == "77" &&
                    rows[0].CellId == "ABC01",
                "A restarted session with missing aggregate identifiers must reuse " +
                "the single known ordered PCell profile instead of creating a duplicate.");
        }

        string migrationHistoryPath = Path.Combine(testFolder, "migration-history.json");
        File.WriteAllText(migrationHistoryPath, """
        {
          "Version": 1,
          "Records": [
            {
              "Key": "B20|500|77|AAA",
              "Band": "B20",
              "PrimaryBand": "B20",
              "Earfcn": "500",
              "Pci": "77",
              "CellId": "AAA",
              "ConnectedSeconds": 120,
              "Samples": 2,
              "TimeBuckets": [{ "Period": 1, "ConnectedSeconds": 120, "Samples": 2 }]
            },
            {
              "Key": "B20|6400|77|AAA",
              "Band": "B20",
              "PrimaryBand": "B20",
              "Earfcn": "6400",
              "Pci": "77",
              "CellId": "AAA",
              "ConnectedSeconds": 180,
              "Samples": 3,
              "TimeBuckets": [{ "Period": 1, "ConnectedSeconds": 180, "Samples": 3 }]
            },
            {
              "Key": "B3 + B28|1700|-|*",
              "Band": "B3 + B28",
              "PrimaryBand": "B3",
              "Earfcn": "1700",
              "Pci": "-",
              "ConnectedSeconds": 60,
              "Samples": 1,
              "TimeBuckets": [{ "Period": 1, "ConnectedSeconds": 60, "Samples": 1 }]
            },
            {
              "Key": "B3 + B28|1700|42|BBB",
              "Band": "B3 + B28",
              "PrimaryBand": "B3",
              "Earfcn": "1700",
              "Pci": "42",
              "CellId": "BBB",
              "ConnectedSeconds": 90,
              "Samples": 2,
              "TimeBuckets": [{ "Period": 1, "ConnectedSeconds": 90, "Samples": 2 }]
            },
            {
              "Key": "B1|500|38|CCC",
              "Band": "B1",
              "PrimaryBand": "B1",
              "Earfcn": "500",
              "Pci": "38",
              "CellId": "CCC",
              "ConnectedSeconds": 30,
              "Samples": 1,
              "TimeBuckets": [{ "Period": 1, "ConnectedSeconds": 30, "Samples": 1 }]
            }
          ]
        }
        """);
        using (var migratedHistory = new LteCellHistoryStore(migrationHistoryPath))
        {
            IReadOnlyList<LteCellRecommendation> rows =
                migratedHistory.GetRecommendations();
            LteCellRecommendation repairedB20 = rows.Single(item =>
                item.Band == "B20" && item.Pci == "77" && item.CellId == "AAA");
            Require(repairedB20.Earfcn == "6400" &&
                    Math.Abs(repairedB20.ConnectedTime.TotalSeconds - 300) < 0.1,
                "A stale lock-target EARFCN must be repaired from the valid PCell " +
                "channel and duplicate evidence must be merged.");
            LteCellRecommendation repairedAggregation = rows.Single(item =>
                item.Band == "B3 + B28" && item.CellId == "BBB");
            Require(repairedAggregation.Earfcn == "1700" &&
                    repairedAggregation.Pci == "42" &&
                    repairedAggregation.CellId == "BBB" &&
                    Math.Abs(repairedAggregation.ConnectedTime.TotalSeconds - 90) < 0.1,
                "CID-qualified evidence must remain separate from an ambiguous legacy row.");
            Require(!rows.Any(item => string.IsNullOrWhiteSpace(item.CellId)),
                "Ambiguous legacy evidence without CID must be removed, never assigned or displayed.");
            Require(rows.Any(item => item.Band == "B1" && item.Earfcn == "500"),
                "EARFCN 500 is valid for LTE Band 1 and must not be discarded.");
        }
        Require(!File.Exists(migrationHistoryPath + ".before-identity-repair"),
            "History repair must not recreate the retired identity-repair backup.");

        string timedHistoryPath = Path.Combine(testFolder, "time-history.json");
        using var timedHistory = new LteCellHistoryStore(
            timedHistoryPath,
            TimeZoneInfo.Local);
        DateTime morningLocal = DateTime.SpecifyKind(
            DateTime.Today.AddHours(7),
            DateTimeKind.Local);
        DateTime eveningLocal = DateTime.SpecifyKind(
            DateTime.Today.AddHours(19),
            DateTimeKind.Local);
        DateTime morningUtc = morningLocal.ToUniversalTime();
        DateTime eveningUtc = eveningLocal.ToUniversalTime();

        RecordCell(timedHistory, morningUtc, "B3", "1300", "100", "11",
            3600, bytesPerSecond: 2000, sinrDb: 18, rsrqDb: -7, rsrpDbm: -83);
        timedHistory.RecordSpeedTest(
            Telemetry(morningUtc.AddSeconds(3600), "B3", "1300", "100", "11"),
            Speed(200, 20));
        timedHistory.RecordSpeedTest(
            Telemetry(morningUtc.AddSeconds(3601), "B3", "1300", "100", "11"),
            Speed(200, 20));
        timedHistory.RecordPingSample(40, morningLocal.AddMinutes(10));
        timedHistory.RecordPingSample(60, morningLocal.AddMinutes(20));
        RecordCell(timedHistory, morningUtc, "B7", "2850", "200", "22",
            3600, bytesPerSecond: 1000, sinrDb: 2, rsrqDb: -16, rsrpDbm: -110);
        timedHistory.RecordSpeedTest(
            Telemetry(morningUtc.AddSeconds(3600), "B7", "2850", "200", "22"),
            Speed(50, 10));
        timedHistory.RecordSpeedTest(
            Telemetry(morningUtc.AddSeconds(3601), "B7", "2850", "200", "22"),
            Speed(50, 10));

        RecordCell(timedHistory, eveningUtc, "B3", "1300", "100", "11",
            3600, bytesPerSecond: 1000, sinrDb: 2, rsrqDb: -16, rsrpDbm: -110);
        timedHistory.RecordSpeedTest(
            Telemetry(eveningUtc.AddSeconds(3600), "B3", "1300", "100", "11"),
            Speed(50, 10));
        timedHistory.RecordSpeedTest(
            Telemetry(eveningUtc.AddSeconds(3601), "B3", "1300", "100", "11"),
            Speed(50, 10));
        RecordCell(timedHistory, eveningUtc, "B7", "2850", "200", "22",
            3600, bytesPerSecond: 2000, sinrDb: 18, rsrqDb: -7, rsrpDbm: -83);
        timedHistory.RecordSpeedTest(
            Telemetry(eveningUtc.AddSeconds(3600), "B7", "2850", "200", "22"),
            Speed(200, 20));
        timedHistory.RecordSpeedTest(
            Telemetry(eveningUtc.AddSeconds(3601), "B7", "2850", "200", "22"),
            Speed(200, 20));

        IReadOnlyList<LteCellRecommendation> morningRanked =
            timedHistory.GetRecommendations(morningLocal);
        IReadOnlyList<LteCellRecommendation> eveningRanked =
            timedHistory.GetRecommendations(eveningLocal);
        Require(morningRanked[0].Band == "B3" && eveningRanked[0].Band == "B7",
            "Time-of-day history should select different morning and evening cells.");
        Require(morningRanked[0].TimePeriod.StartsWith("Morning", StringComparison.Ordinal),
            "The current time period should be visible in recommendations.");
        Require(morningRanked[0].TimeEvidenceWeightPercent >= 99,
            "A full hour with two speed tests should receive full time weight.");
        Require(morningRanked[0].UsageSharePercent > 60,
            "Observed data usage should be represented as a share, not a speed score.");
        Require(Math.Abs((morningRanked[0].AveragePingMs ?? 0) - 50) < 0.01,
            "Average ping should be attributed to the active PCell profile and time period.");
        LteCellRecommendation loadedMorningCell = morningRanked.Single(item =>
            item.Band == "B7");
        Require((loadedMorningCell.EstimatedCellLoadPercent ?? 0) >= 70,
            "Cell load should estimate the gap from the profile's observed best download.");
        IReadOnlyList<LteCellRecommendation> groupedHistory =
            timedHistory.GetHistoryRecommendations(morningLocal);
        Require(groupedHistory.Count(item => item.PeriodId == 1) == 2 &&
                groupedHistory.Count(item => item.PeriodId == 3) == 2,
            "LTE history should expose profiles under their measured time periods.");
        Require(groupedHistory.Select(item => item.PeriodId).Distinct()
                .SequenceEqual([1, 3]),
            "Time-period history should not create band or PCell group identities.");

        string locationTimePath = Path.Combine(testFolder, "location-time-history.json");
        TimeZoneInfo locationZone = TimeZoneInfo.CreateCustomTimeZone(
            "NetPulse Test Location UTC+10",
            TimeSpan.FromHours(10),
            "NetPulse Test Location UTC+10",
            "NetPulse Test Location UTC+10");
        using var locationTimeHistory = new LteCellHistoryStore(
            locationTimePath,
            locationZone);
        DateTime locationTestUtc = new(
            2026, 1, 1, 22, 30, 0, DateTimeKind.Utc);
        RecordCell(locationTimeHistory, locationTestUtc, "B3", "1300", "100", "11", 2);
        LteCellRecommendation locationRow = locationTimeHistory
            .GetHistoryRecommendations(locationTestUtc)
            .Single();
        Require(locationRow.PeriodId == 1 &&
                locationRow.TimePeriod.StartsWith("Morning", StringComparison.Ordinal),
            "LTE periods must use the selected location's UTC+10 clock (08:30), " +
            "never the machine's local clock or the raw UTC hour.");
    }
    finally
    {
        if (Directory.Exists(testFolder))
            Directory.Delete(testFolder, recursive: true);
    }
}

static void RecordCell(
    LteCellHistoryStore history,
    DateTime start,
    string band,
    string earfcn,
    string pci,
    string cellId,
    int seconds,
    long bytesPerSecond = 0,
    double? sinrDb = null,
    double? rsrqDb = null,
    double? rsrpDbm = null)
{
    for (int second = 0; second <= seconds; second++)
        history.RecordTelemetry(
            Telemetry(
                start.AddSeconds(second),
                band,
                earfcn,
                pci,
                cellId,
                bytesPerSecond > 0 ? second * bytesPerSecond : null,
                sinrDb,
                rsrqDb,
                rsrpDbm));
}

static RouterTelemetry Telemetry(
    DateTime timestamp,
    string band,
    string earfcn,
    string pci,
    string cellId,
    long? totalBytes = null,
    double? sinrDb = null,
    double? rsrqDb = null,
    double? rsrpDbm = null) => new()
    {
        Timestamp = timestamp,
        IsConnected = true,
        Status = "Connected",
        Band = band,
        Earfcn = earfcn,
        Pci = pci,
        CellId = cellId,
        TotalBytes = totalBytes,
        SnrDb = sinrDb,
        RsrqDb = rsrqDb,
        RsrpDbm = rsrpDbm
    };

static SpeedTestResult Speed(double download, double upload) => new()
{
    DownloadMbps = download,
    UploadMbps = upload
};

static void TestAutoLockPolicy()
{
    LteCellRecommendation excellent = RadioRecommendation(20, -6, -80);
    LteCellRecommendation good = RadioRecommendation(10, -10, -92);
    LteCellRecommendation poor = RadioRecommendation(-3, -18, -112);
    LteCellRecommendation[] candidates = [excellent, good, poor];
    LteRecommendationScoring.AssignScores(candidates);
    Require(excellent.WeightedScore > good.WeightedScore &&
            good.WeightedScore > poor.WeightedScore,
        "LTE ranking must use 50% SINR, 35% RSRQ and 15% RSRP.");
    Require(LteAutoLockPolicy.IsMeaningfullyBetter(excellent, good, candidates),
        "A materially better radio-quality profile should be preferred.");
    Require(LteRecommendationScoring.ScoreSinr(16) >= 90 &&
            LteRecommendationScoring.ScoreSinr(15) >= 90 &&
            LteRecommendationScoring.ScoreRsrq(-7) >= 90 &&
            LteRecommendationScoring.ScoreRsrp(-80) >= 95 &&
            LteRecommendationScoring.ScoreRsrp(-85) >= 95,
        "Excellent radio thresholds must map to their requested score ranges.");
    LteCellRecommendation legacyOnly = Recommendation(
        0, 100, 100, TimeSpan.FromHours(1));
    LteRecommendationScoring.AssignScores([legacyOnly]);
    Require(legacyOnly.WeightedScore == 0,
        "Download, upload and disconnect data must not replace missing radio evidence.");

    DateTime now = DateTime.UtcNow;
    var settings = new AppSettings
    {
        AutomaticCellLockMinimumDwellMinutes = 30,
        AutomaticCellLockMaxChangesPerDay = 6,
        AutomaticCellLockChangesToday = 2,
        LastAutomaticCellLockUtc = now.AddMinutes(-29)
    };
    Require(!LteAutoLockPolicy.CanAttempt(settings, now),
        "The minimum dwell should block rapid switching.");
    settings.LastAutomaticCellLockUtc = now.AddMinutes(-31);
    Require(LteAutoLockPolicy.CanAttempt(settings, now),
        "The optimizer should re-evaluate after the dwell period.");
    settings.AutomaticCellLockChangesToday = 6;
    Require(!LteAutoLockPolicy.CanAttempt(settings, now),
        "The daily change limit should block further automatic writes.");

    static LteCellRecommendation RadioRecommendation(
        double sinr, double rsrq, double rsrp) => new()
        {
            Key = Guid.NewGuid().ToString("N"), Band = "B3",
            PrimaryBand = "B3", Earfcn = "1300", Pci = "77",
            CellId = "ABCDE", TimePeriod = "Morning 06–12",
            UsageBasis = "time", Confidence = "High", IsEligible = true,
            AverageSinrDb = sinr, AverageRsrqDb = rsrq,
            AverageRsrpDbm = rsrp
        };
}

static void TestAutomaticSpeedTests()
{
    var coordinator = new AutomaticSpeedTestCoordinator();
    DateTime now = DateTime.UtcNow;

    coordinator.ObserveRouterTelemetry(
        Telemetry(now, "B3", "1300", "321", "111"), now);
    Require(!coordinator.TryTakeDue(now.AddMinutes(1), out _),
        "The first router sample must establish a baseline without running a test.");

    coordinator.ObserveRouterTelemetry(
        Telemetry(now.AddSeconds(1), "B7", "2850", "120", "222"),
        now.AddSeconds(1));
    Require(!coordinator.TryTakeDue(now.AddSeconds(10), out _),
        "A changed LTE state must settle before testing.");
    Require(coordinator.TryTakeDue(now.AddSeconds(14), out AutomaticSpeedTestRequest? lte) &&
            lte!.Reason.Contains("LTE primary band changed", StringComparison.Ordinal) &&
            lte.Reason.Contains("LTE cell changed", StringComparison.Ordinal),
        "Band and cell changes should coalesce into one attributed test.");

    var aggregationCoordinator = new AutomaticSpeedTestCoordinator();
    aggregationCoordinator.ObserveRouterTelemetry(
        Telemetry(now, "B1", "100", "100", "ABC"), now);
    aggregationCoordinator.ObserveRouterTelemetry(
        Telemetry(now.AddSeconds(1), "B1 + B3", "100", "100", "ABC"),
        now.AddSeconds(1));
    Require(!aggregationCoordinator.TryTakeDue(now.AddMinutes(1), out _),
        "Adding an SCell to the same PCell must not trigger a speed test.");
    aggregationCoordinator.ObserveRouterTelemetry(
        Telemetry(now.AddSeconds(2), "B1", "100", "100", "ABC"),
        now.AddSeconds(2));
    Require(!aggregationCoordinator.TryTakeDue(now.AddMinutes(2), out _),
        "Removing an SCell from the same PCell must not trigger a speed test.");

    coordinator.ObserveOutage();
    coordinator.ObserveRecovery(now.AddSeconds(20));
    Require(coordinator.TryTakeDue(now.AddSeconds(33), out AutomaticSpeedTestRequest? outage) &&
            outage!.Reason.Contains("confirmed outage", StringComparison.Ordinal),
        "A confirmed outage must schedule a test after recovery.");

    coordinator.ObservePublicIp("198.51.100.10", now);
    coordinator.ObservePublicIp("198.51.100.11", now.AddSeconds(40));
    Require(coordinator.TryTakeDue(now.AddSeconds(53), out AutomaticSpeedTestRequest? ip) &&
            ip!.Reason.Contains("public IP changed", StringComparison.Ordinal),
        "A public-IP change must schedule an attributed test.");
}

static async Task TestCompanionProtocolAsync()
{
    var portProbe = new TcpListener(IPAddress.Loopback, 0);
    portProbe.Start();
    int port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
    portProbe.Stop();
    string secret = CompanionService.CreatePairingSecret();
    await using var service = new CompanionService(() => new CompanionSnapshot(
        DateTime.UtcNow, true, false, 42, 3.5, 0.2, 99.9, 1,
        "connected", true, "4G+ LTE-A", "B1 + B3", "B1", "100", "100",
        "ABCDE", -95, -10, 12, 125_000, 250_000, 2));
    service.Start(port, secret);
    using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };

    string testApkPath = Path.Combine(AppContext.BaseDirectory, "NetPulse-Monitor-Companion-Android.apk");
    byte[] testApk = [0x50, 0x4B, 0x03, 0x04, 0x4E, 0x50];
    await File.WriteAllBytesAsync(testApkPath, testApk);
    using (HttpResponseMessage download = await client.GetAsync("download/android"))
    {
        Require(download.IsSuccessStatusCode &&
                download.Content.Headers.ContentType?.MediaType == "application/vnd.android.package-archive" &&
                (await download.Content.ReadAsByteArrayAsync()).SequenceEqual(testApk),
            "The direct LAN Android download endpoint did not return the APK.");
    }
    File.Delete(testApkPath);

    PairingProfile parsedProfile = PairingProfile.Parse(service.PairingUri);
    using (var mobileClient = new CompanionClient(
               parsedProfile with { Host = "127.0.0.1" }))
    {
        MobileSnapshot mobileSnapshot = await mobileClient.ReadSnapshotAsync();
        Require(mobileSnapshot.InternetOnline && mobileSnapshot.PrimaryBand == "B1" &&
                mobileSnapshot.UploadBytesPerSecond == 125_000 && mobileSnapshot.DownloadBytesPerSecond == 250_000,
            "The shared mobile client could not authenticate and decrypt desktop telemetry.");
    }

    using HttpResponseMessage unauthorized = await client.GetAsync("v1/snapshot");
    Require(unauthorized.StatusCode == HttpStatusCode.Unauthorized,
        "The companion endpoint must reject unsigned requests.");

    string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
    string nonce = CompanionService.Base64Url(RandomNumberGenerator.GetBytes(18));
    byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    string signature = CompanionService.Base64Url(HMACSHA256.HashData(
        key, Encoding.UTF8.GetBytes($"GET\n/v1/snapshot\n{timestamp}\n{nonce}")));
    using var request = new HttpRequestMessage(HttpMethod.Get, "v1/snapshot");
    request.Headers.Add("X-NetPulse-Time", timestamp);
    request.Headers.Add("X-NetPulse-Nonce", nonce);
    request.Headers.Add("X-NetPulse-Signature", signature);
    using HttpResponseMessage response = await client.SendAsync(request);
    Require(response.IsSuccessStatusCode,
        "A correctly signed companion request should be accepted.");
    using JsonDocument envelope = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    byte[] encryptedNonce = CompanionService.FromBase64Url(
        envelope.RootElement.GetProperty("nonce").GetString()!);
    byte[] ciphertext = CompanionService.FromBase64Url(
        envelope.RootElement.GetProperty("ciphertext").GetString()!);
    byte[] tag = CompanionService.FromBase64Url(
        envelope.RootElement.GetProperty("tag").GetString()!);
    byte[] plain = new byte[ciphertext.Length];
    using (var aes = new AesGcm(key, tag.Length))
        aes.Decrypt(encryptedNonce, ciphertext, tag, plain);
    using JsonDocument snapshot = JsonDocument.Parse(plain);
    Require(snapshot.RootElement.GetProperty("InternetOnline").GetBoolean() &&
            snapshot.RootElement.GetProperty("Band").GetString() == "B1 + B3",
        "The encrypted companion payload did not preserve live telemetry.");

    using var replay = new HttpRequestMessage(HttpMethod.Get, "v1/snapshot");
    replay.Headers.Add("X-NetPulse-Time", timestamp);
    replay.Headers.Add("X-NetPulse-Nonce", nonce);
    replay.Headers.Add("X-NetPulse-Signature", signature);
    using HttpResponseMessage replayResponse = await client.SendAsync(replay);
    Require(replayResponse.StatusCode == HttpStatusCode.Unauthorized,
        "A companion nonce must not be accepted twice.");
}

static void TestSettingsMigration()
{
    var settings = new AppSettings
    {
        ConnectionDetailsView = "Vdsl",
        DownloadSampleMegabytes = 5,
        UploadSampleMegabytes = 2
    };
    settings.Normalize();
    Require(settings.ConnectionDetailsView == "Dsl",
        "ADSL and VDSL profiles should migrate to the combined DSL profile.");
    Require(settings.DownloadSampleMegabytes == 20 &&
            settings.UploadSampleMegabytes == 5,
        "All speed tests must use the comparable 20 MB / 5 MB samples.");

    settings.ConnectionDetailsView = "Ftth";
    settings.Normalize();
    Require(settings.ConnectionDetailsView == "Fiber",
        "FTTB and FTTH profiles should migrate to the combined fiber profile.");
}

static void TestExperienceServices()
{
    ConnectionHealthAssessment offline = ConnectionHealthEvaluator.Evaluate(
        new MonitorSnapshot { IsOnline = false },
        new RouterTelemetry(),
        null);
    Require(offline.Score == 0 && offline.Rating == "Offline",
        "An offline connection must have a zero health score.");

    ConnectionHealthAssessment healthy = ConnectionHealthEvaluator.Evaluate(
        new MonitorSnapshot
        {
            IsOnline = true,
            CurrentPingMs = 28,
            JitterMs = 3,
            PacketLossPercent = 0,
            AvailabilityPercent = 99.99
        },
        new RouterTelemetry
        {
            IsConnected = true,
            RsrpDbm = -88,
            RsrqDb = -9,
            SnrDb = 15
        },
        new DiagnosticResult { GatewayPing = "2 ms", DnsLookup = "8 ms" });
    Require(healthy.Score >= 90 && healthy.Rating == "Excellent",
        "A stable local, internet, and LTE path should receive an excellent score.");

    ConnectionHealthAssessment impaired = ConnectionHealthEvaluator.Evaluate(
        new MonitorSnapshot
        {
            IsOnline = true,
            CurrentPingMs = 280,
            JitterMs = 95,
            PacketLossPercent = 12,
            AvailabilityPercent = 93
        },
        new RouterTelemetry { IsConnected = true, RsrpDbm = -115, RsrqDb = -17, SnrDb = 1 },
        new DiagnosticResult { GatewayPing = "Unavailable", DnsLookup = "Unavailable" });
    Require(impaired.Score < healthy.Score && impaired.Factors.Count >= 5,
        "Severe loss, latency, LTE quality, and local-path faults must lower health.");

    var contacts = new Dictionary<string, string> { ["301"] = "Support" };
    RouterSmsMessage[] messages =
    [
        new RouterSmsMessage
        {
            Stack = "1", Index = "1", Address = "+301", Content = "First",
            TimeText = "2026-01-01", Timestamp = new DateTime(2026, 1, 1),
            Folder = RouterSmsFolder.Inbox, IsUnread = true
        },
        new RouterSmsMessage
        {
            Stack = "2", Index = "2", Address = "+301", Content = "Reply",
            TimeText = "2026-01-02", Timestamp = new DateTime(2026, 1, 2),
            Folder = RouterSmsFolder.Sent
        }
    ];
    SmsConversation conversation = SmsConversationBuilder.Build(messages, contacts).Single();
    Require(conversation.DisplayName == "Support" && conversation.UnreadCount == 1 &&
            conversation.Messages[0].Content == "First" &&
            conversation.Messages[1].Content == "Reply",
        "SMS conversations must combine Inbox and Sent chronologically per contact.");
    Require(SmsConversationBuilder.Build(messages, contacts, "reply").Count == 1,
        "SMS conversation search should match message content.");

    RouterSmsMessage[] greekNumberVariants =
    [
        new RouterSmsMessage
        {
            Folder = RouterSmsFolder.Inbox,
            Stack = "1",
            Index = "1",
            Address = "+306991234567",
            Content = "International format",
            TimeText = "2026-08-01 12:00:00",
            Timestamp = new DateTime(2026, 8, 1, 12, 0, 0)
        },
        new RouterSmsMessage
        {
            Folder = RouterSmsFolder.Sent,
            Stack = "2",
            Index = "2",
            Address = "6991234567",
            Content = "National format",
            TimeText = "2026-08-01 12:01:00",
            Timestamp = new DateTime(2026, 8, 1, 12, 1, 0)
        },
        new RouterSmsMessage
        {
            Folder = RouterSmsFolder.Sent,
            Stack = "2",
            Index = "3",
            Address = "00306991234567",
            Content = "International prefix",
            TimeText = "2026-08-01 12:02:00",
            Timestamp = new DateTime(2026, 8, 1, 12, 2, 0)
        }
    ];
    SmsConversation greekConversation = SmsConversationBuilder.Build(
        greekNumberVariants,
        new Dictionary<string, string>(),
        countryCode: "GR").Single();
    Require(greekConversation.Messages.Count == 3 &&
            SmsConversationBuilder.NormalizeAddress("+306991234567", "GR") ==
            SmsConversationBuilder.NormalizeAddress("6991234567", "GR") &&
            SmsConversationBuilder.NormalizeAddress("00306991234567", "GR") ==
            SmsConversationBuilder.NormalizeAddress("6991234567", "GR"),
        "Greek national and international SMS numbers must share one conversation.");

    LteCellRecommendation reliable = RadioProfile(18, -7, -83);
    LteCellRecommendation fastButUnstable = RadioProfile(2, -16, -110);
    IReadOnlyList<CellExperimentResult> ranked = CellExperimentEvaluator.Rank(
        [reliable, fastButUnstable]);
    Require(ranked.Count == 2 && ranked[0].Recommendation == reliable,
        "Controlled experiments must respect the 50/35/15 SINR/RSRQ/RSRP policy.");

    Require(ReleaseVersionComparer.IsNewer("v2.0.0", "1.0.7") &&
            !ReleaseVersionComparer.IsNewer("v1.0.7", "1.0.7"),
        "Update comparison should accept v-prefixed tags and reject equal versions.");
}

static void TestBandDiscovery()
{
    var mr600 = new RouterTelemetry
    {
        IsConnected = true,
        Model = "Archer MR600",
        HardwareVersion = "Archer MR600 v5.0 00000001",
        FirmwareVersion = "1.5.0 0.9.1",
        Band = "B3 + B20"
    };
    LteBandScanPlan verified = LteBandDiscovery.CreatePlan(
        mr600,
        "GR",
        ["B1 + B3", "B28"]);
    Require(verified.IsComplete &&
            verified.Bands.SequenceEqual([1, 3, 5, 7, 8, 20, 28, 38, 40, 41]),
        "MR600(EU) V5 discovery must use its complete verified band plan.");

    var unknown = new RouterTelemetry
    {
        IsConnected = true,
        Model = "TP-Link LTE router",
        HardwareVersion = "Unknown",
        Band = "B3 + B20"
    };
    LteBandScanPlan fallback = LteBandDiscovery.CreatePlan(
        unknown,
        "GR",
        ["B28 + B3", "B1", "B3"]);
    Require(!fallback.IsComplete && fallback.Bands.SequenceEqual([1, 3, 20, 28]),
        "Unknown router revisions must scan observed bands only, without speculation.");

    bool read = LteBandDiscovery.TryReadServingCell(
        3,
        new RouterTelemetry
        {
            Timestamp = new DateTime(2026, 8, 11, 10, 0, 0),
            IsConnected = true,
            Band = "B3",
            PrimaryBand = "B3",
            Earfcn = "1700",
            Pci = "77",
            CellId = "ABCDE",
            RsrpDbm = -101,
            RsrqDb = -12,
            SnrDb = 8.5
        },
        out LteBandCellObservation? cell);
    Require(read && cell is not null && cell.RequestedBand == 3 &&
            cell.Earfcn == "1700" && cell.Pci == "77" &&
            cell.CellId == "ABCDE" && cell.Samples == 1 &&
            cell.HasCompleteIdentity,
        "Discovery must retain full serving-cell identifiers.");

    bool incompleteRead = LteBandDiscovery.TryReadServingCell(
        3,
        new RouterTelemetry
        {
            IsConnected = true,
            Band = "B3",
            PrimaryBand = "B3",
            Earfcn = "1700",
            Pci = "-",
            CellId = "-"
        },
        out LteBandCellObservation? incompleteCell);
    Require(incompleteRead && incompleteCell is not null &&
            !incompleteCell.HasCompleteIdentity &&
            incompleteCell.Status.Contains("waiting for complete",
                StringComparison.OrdinalIgnoreCase),
        "A serving band without PCI/CID must remain pending, not count as a " +
        "completed discovery identity.");

    bool staleCombinationAccepted = LteBandDiscovery.TryReadServingCell(
        3,
        new RouterTelemetry
        {
            IsConnected = true,
            Band = "B3 + B20",
            PrimaryBand = "B3",
            Earfcn = "1700",
            Pci = "77",
            CellId = "ABCDE"
        },
        out _);
    Require(!staleCombinationAccepted,
        "A stale carrier-aggregation snapshot must not be attributed to a single-band scan.");
}

static LteCellRecommendation Recommendation(
    double dropsPerHour,
    double download,
    double upload,
    TimeSpan? periodConnectedTime = null,
    bool userAdded = false,
    bool discoveryCandidate = false) => new()
    {
        Key = Guid.NewGuid().ToString("N"),
        Band = "B3",
        Earfcn = "1300",
        Pci = "77",
        DisconnectionsPerHour = dropsPerHour,
        AverageDownloadMbps = download,
        AverageUploadMbps = upload,
        PeriodConnectedTime = periodConnectedTime ?? TimeSpan.Zero,
        TimePeriod = "Morning 06–12",
        UsageBasis = "data",
        Confidence = "High",
        IsEligible = true,
        UserAdded = userAdded,
        DiscoveryCandidate = discoveryCandidate
    };

static LteCellRecommendation RadioProfile(double sinr, double rsrq, double rsrp) =>
    new()
    {
        Key = Guid.NewGuid().ToString("N"),
        Band = "B3",
        PrimaryBand = "B3",
        Earfcn = "1300",
        Pci = "77",
        CellId = "ABCDE",
        PeriodConnectedTime = TimeSpan.FromMinutes(20),
        TimePeriod = "Morning 06–12",
        UsageBasis = "time",
        Confidence = "High",
        IsEligible = true,
        AverageSinrDb = sinr,
        AverageRsrqDb = rsrq,
        AverageRsrpDbm = rsrp
    };

