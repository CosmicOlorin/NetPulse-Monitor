using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        plainResponse = plainRequest.StartsWith("5\r\n", StringComparison.Ordinal)
            ? "[1,0,0,0,0,0]0\r\nindex=3\r\nto=+303333333333\r\n" +
              "content=Sent message\r\nsendTime=2026-08-08 08:02:00\r\n"
            : "[1,0,0,0,0,0]0\r\n";
    }
    else if (plainRequest.Contains("LTE_SMS_SENDMSGBOX", StringComparison.Ordinal))
    {
        smsRequests.Add(plainRequest);
        plainResponse = plainRequest.Contains("totalNumber", StringComparison.Ordinal)
            ? "[0,0,0,0,0,0]0\r\ntotalNumber=1\r\namountPerPage=8\r\n"
            : "[0,0,0,0,0,0]0\r\n";
    }
    else if (plainRequest.Contains("LTE_SMS_DRAFTMSGENTRY", StringComparison.Ordinal))
    {
        smsRequests.Add(plainRequest);
        plainResponse = plainRequest.StartsWith("5\r\n", StringComparison.Ordinal)
            ? "[1,0,0,0,0,0]0\r\nindex=4\r\nto=+304444444444\r\n" +
              "content=Draft message\r\n"
            : "[1,0,0,0,0,0]0\r\n";
    }
    else if (plainRequest.Contains("LTE_SMS_DRAFTMSGBOX", StringComparison.Ordinal))
    {
        smsRequests.Add(plainRequest);
        plainResponse = plainRequest.Contains("totalNumber", StringComparison.Ordinal)
            ? "[0,0,0,0,0,0]0\r\ntotalNumber=1\r\namountPerPage=8\r\n"
            : "[0,0,0,0,0,0]0\r\n";
    }
    else if (plainRequest.Contains("LTE_SMS_RECVMSGENTRY", StringComparison.Ordinal))
    {
        smsRequests.Add(plainRequest);
        plainResponse = plainRequest.StartsWith("5\r\n", StringComparison.Ordinal)
            ? "[1,0,0,0,0,0]0\r\nindex=1\r\nfrom=+301111111111\r\n" +
              "content=First message\r\nreceivedTime=2026-08-08 08:00:00\r\nunread=1\r\n" +
              "[2,0,0,0,0,0]0\r\nindex=2\r\nfrom=+302222222222\r\n" +
              "content=Second message\r\nreceivedTime=2026-08-08 08:01:00\r\nunread=0\r\n"
            : "[1,0,0,0,0,0]0\r\n";
    }
    else if (plainRequest.Contains("LTE_SMS_RECVMSGBOX", StringComparison.Ordinal))
    {
        smsRequests.Add(plainRequest);
        plainResponse = plainRequest.Contains("totalNumber", StringComparison.Ordinal)
            ? "[0,0,0,0,0,0]0\r\ntotalNumber=2\r\namountPerPage=8\r\n"
            : "[0,0,0,0,0,0]0\r\n";
    }
    else if (plainRequest.StartsWith("2&2\r\n", StringComparison.Ordinal))
    {
        writeRequests.Add(plainRequest);
        plainResponse =
            "[1,1,0,0,0,0]0\r\n" +
            "[0,0,0,0,0,0]1\r\n";
    }
    else if (plainRequest.Contains("WAN_COMMON_INTF_CFG", StringComparison.Ordinal))
    {
        plainResponse =
            "[1,0,0,0,0,0]0\r\n" +
            "WANAccessType=LTE\r\n";
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
        plainResponse =
            "[1,1,0,0,0,0]0\r\n" +
            "simStatus=3\r\nroamingStatus=0\r\nsignalStrength=4\r\n" +
            "networkType=3\r\nconnectStatus=4\r\n" +
            "[1,0,0,0,0,0]1\r\n" +
            "totalStatistics=2147483648\r\ncurRxSpeed=1234567\r\ncurTxSpeed=765432\r\n" +
            "[1,1,0,0,0,0]2\r\n" +
            "regStat=1\r\nrfInfoBand=122\r\nrfInfoRsrq=-9\r\n" +
            "rfInfoRsrp=-97\r\nrfInfoSnr=123\r\nrfInfoRssi=-68\r\n" +
            "rfInfoCellID=123456789\r\nrfInfoPCellBand=3\r\n" +
            "rfInfoPCellChannel=1300\r\nrfInfoPCI=321\r\nsmsUnreadCount=2\r\n" +
            "[1,1,0,0,0,0]3\r\n" +
            "ispName=Test Carrier\r\nspn=Test\r\n" +
            "[0,0,0,0,0,0]4\r\n" +
            "rfInfoCellID=0\r\nrfInfoEARFCN=0\r\nrfInfoPCI=0\r\n" +
            "[0,0,0,0,0,0]5\r\n" +
            "modelName=Archer MR600\r\nhardwareVersion=Archer MR600 v5\r\n" +
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
    Require(telemetry.UnreadSmsCount == 2,
        "The unread SMS count was not parsed from LTE status.");
    Require(telemetry.TotalBytes == 2147483648L, "64-bit data usage was not parsed.");
    Require(statusRequests >= 2, "Expected status reads were not sent.");

    IReadOnlyList<RouterSmsMessage> timeline =
        await provider.ReadSmsTimelineAsync(timeout.Token);
    Require(timeline.Count == 4 &&
            timeline[0].Folder == RouterSmsFolder.Sent &&
            timeline[1].Folder == RouterSmsFolder.Inbox &&
            timeline[2].Folder == RouterSmsFolder.Inbox &&
            timeline[3].Folder == RouterSmsFolder.Draft,
        "Inbox, sent messages and drafts should share one chronological timeline.");
    RouterSmsMessage unreadMessage = timeline.Single(message => message.IsUnread);
    await provider.MarkSmsReadAsync(unreadMessage.Stack, timeout.Token);
    Require(smsRequests.Any(item =>
            item.Contains("LTE_SMS_RECVMSGENTRY", StringComparison.Ordinal) &&
            item.Contains("unread=0", StringComparison.Ordinal)),
        "Opening an unread SMS should mark only that router entry as read.");
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
    TestSettingsMigration();
    Console.WriteLine(
        "TP-Link protocol, SMS, LTE history, optimizer, and automatic speed-test tests passed.");
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

    string testFolder = Path.Combine(
        Path.GetTempPath(),
        "NetPulseMonitorTests",
        Guid.NewGuid().ToString("N"));
    string historyPath = Path.Combine(testFolder, "history.json");

    try
    {
        using var history = new LteCellHistoryStore(historyPath);
        DateTime started = DateTime.UtcNow.AddHours(-1);

        RecordCell(history, started, "B1", "100", "10", "111", 600);
        history.RecordSpeedTest(
            Telemetry(started.AddSeconds(600), "B1", "100", "10", "111"),
            Speed(200, 40));
        history.RecordConfirmedOutage(started.AddSeconds(601));

        DateTime secondStart = started.AddSeconds(700);
        RecordCell(history, secondStart, "B3", "1300", "321", "222", 600);
        history.RecordSpeedTest(
            Telemetry(secondStart.AddSeconds(600), "B3", "1300", "321", "222"),
            Speed(50, 10));

        DateTime thirdStart = secondStart.AddSeconds(700);
        RecordCell(history, thirdStart, "B7", "2850", "42", "-", 600);
        history.RecordSpeedTest(
            Telemetry(thirdStart.AddSeconds(600), "B7", "2850", "42", "-"),
            Speed(100, 5));

        IReadOnlyList<LteCellRecommendation> ranked = history.GetRecommendations();
        Require(ranked.Count == 3, "All observed LTE cells should be retained.");
        Require(ranked[0].Band == "B7",
            "The 50/40/10 score should prefer the balanced zero-disconnection profile.");
        Require(ranked[1].Band == "B1",
            "The fastest profile should rank second after its measured drop-rate penalty.");
        Require(ranked[2].Band == "B3",
            "The slowest eligible profile should rank last in this measured set.");
        Require(ranked.Single(item => item.Band == "B7").CellId is null,
            "CID should remain optional when the router does not report it.");
        Require(history.GetActiveProfileKey() == ranked.Single(item =>
                item.Band == "B7").Key,
            "The history store should expose the currently used profile for highlighting.");
        IReadOnlyList<LteCellRecommendation> observedLocks =
            history.GetObservedLockProfiles();
        Require(observedLocks.Count == 3 && observedLocks[0].Band == "B7",
            "Cell Lock choices should show stable observed sets with the active set first.");

        string shortHistoryPath = Path.Combine(testFolder, "short-history.json");
        using (var shortHistory = new LteCellHistoryStore(shortHistoryPath))
        {
            RecordCell(shortHistory, started, "B20", "6300", "55", "333", 299);
            Require(shortHistory.GetObservedLockProfiles().Count == 0,
                "Cell Lock choices should hide sets observed for less than five minutes.");
        }

        string bandOnlyHistoryPath = Path.Combine(testFolder, "band-only-history.json");
        using (var bandOnlyHistory = new LteCellHistoryStore(bandOnlyHistoryPath))
        {
            DateTime bandOnlyStart = started.AddHours(4);
            RecordCell(
                bandOnlyHistory,
                bandOnlyStart,
                "B3 + B20",
                "1451",
                "-",
                "-",
                600);
            bandOnlyHistory.RecordSpeedTest(
                Telemetry(
                    bandOnlyStart.AddSeconds(600),
                    "B3 + B20",
                    "1451",
                    "-",
                    "-"),
                Speed(120, 20));
            IReadOnlyList<LteCellRecommendation> bandOnly =
                bandOnlyHistory.GetRecommendations();
            Require(bandOnly.Count == 1 && bandOnly[0].Pci == "-" &&
                    bandOnly[0].IsEligible,
                "Live EARFCN history should remain useful when firmware hides PCI.");
        }

        string pcellHistoryPath = Path.Combine(testFolder, "pcell-history.json");
        using (var pcellHistory = new LteCellHistoryStore(pcellHistoryPath))
        {
            pcellHistory.AddManualProfile("B3", "1451", "42", "777");
            pcellHistory.RecordTelemetry(
                Telemetry(started, "B3 + B20", "1451", "-", "-"));
            pcellHistory.RecordTelemetry(
                Telemetry(started.AddSeconds(1), "B20 + B3", "1451", "-", "-"));
            pcellHistory.RecordTelemetry(
                Telemetry(started.AddSeconds(2), "B3 + B20", "6300", "-", "-"));
            IReadOnlyList<LteCellRecommendation> pcellRows =
                pcellHistory.GetRecommendations();
            LteCellRecommendation samePrimary = pcellRows.Single(item =>
                item.Band == "B3 + B20" && item.Earfcn == "1451");
            Require(samePrimary.PrimaryBand == "B3" && samePrimary.Pci == "42" &&
                    samePrimary.CellId == "777",
                "PCI/CID should carry across aggregation only when PCell and EARFCN match.");
            LteCellRecommendation changedPrimary = pcellRows.Single(item =>
                item.Band == "B20 + B3" && item.Earfcn == "1451");
            Require(changedPrimary.Pci == "-" && changedPrimary.CellId is null,
                "A changed PCell must not inherit identifiers from the previous primary cell.");
            LteCellRecommendation changedEarfcn = pcellRows.Single(item =>
                item.Band == "B3 + B20" && item.Earfcn == "6300");
            Require(changedEarfcn.Pci == "-" && changedEarfcn.CellId is null,
                "A changed EARFCN must not inherit identifiers from the previous primary cell.");
        }

        string timedHistoryPath = Path.Combine(testFolder, "time-history.json");
        using var timedHistory = new LteCellHistoryStore(timedHistoryPath);
        DateTime morningLocal = DateTime.SpecifyKind(
            DateTime.Today.AddHours(7),
            DateTimeKind.Local);
        DateTime eveningLocal = DateTime.SpecifyKind(
            DateTime.Today.AddHours(19),
            DateTimeKind.Local);
        DateTime morningUtc = morningLocal.ToUniversalTime();
        DateTime eveningUtc = eveningLocal.ToUniversalTime();

        RecordCell(timedHistory, morningUtc, "B3", "1300", "100", "11",
            3600, bytesPerSecond: 2000);
        timedHistory.RecordSpeedTest(
            Telemetry(morningUtc.AddSeconds(3600), "B3", "1300", "100", "11"),
            Speed(200, 20));
        timedHistory.RecordSpeedTest(
            Telemetry(morningUtc.AddSeconds(3601), "B3", "1300", "100", "11"),
            Speed(200, 20));
        RecordCell(timedHistory, morningUtc, "B7", "2850", "200", "22",
            3600, bytesPerSecond: 1000);
        timedHistory.RecordSpeedTest(
            Telemetry(morningUtc.AddSeconds(3600), "B7", "2850", "200", "22"),
            Speed(50, 10));
        timedHistory.RecordSpeedTest(
            Telemetry(morningUtc.AddSeconds(3601), "B7", "2850", "200", "22"),
            Speed(50, 10));

        RecordCell(timedHistory, eveningUtc, "B3", "1300", "100", "11",
            3600, bytesPerSecond: 1000);
        timedHistory.RecordSpeedTest(
            Telemetry(eveningUtc.AddSeconds(3600), "B3", "1300", "100", "11"),
            Speed(50, 10));
        timedHistory.RecordSpeedTest(
            Telemetry(eveningUtc.AddSeconds(3601), "B3", "1300", "100", "11"),
            Speed(50, 10));
        RecordCell(timedHistory, eveningUtc, "B7", "2850", "200", "22",
            3600, bytesPerSecond: 2000);
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
        IReadOnlyList<LteCellRecommendation> groupedHistory =
            timedHistory.GetHistoryRecommendations(morningLocal);
        Require(groupedHistory.Count(item => item.PeriodId == 1) == 2 &&
                groupedHistory.Count(item => item.PeriodId == 3) == 2,
            "LTE history should expose profiles under their measured time periods.");
        Require(groupedHistory.Select(item => item.PeriodId).Distinct()
                .SequenceEqual([1, 3]),
            "Time-period history should not create band or PCell group identities.");
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
    long bytesPerSecond = 0)
{
    for (int second = 0; second <= seconds; second++)
        history.RecordTelemetry(
            Telemetry(
                start.AddSeconds(second),
                band,
                earfcn,
                pci,
                cellId,
                bytesPerSecond > 0 ? second * bytesPerSecond : null));
}

static RouterTelemetry Telemetry(
    DateTime timestamp,
    string band,
    string earfcn,
    string pci,
    string cellId,
    long? totalBytes = null) => new()
    {
        Timestamp = timestamp,
        IsConnected = true,
        Status = "Connected",
        Band = band,
        Earfcn = earfcn,
        Pci = pci,
        CellId = cellId,
        TotalBytes = totalBytes
    };

static SpeedTestResult Speed(double download, double upload) => new()
{
    DownloadMbps = download,
    UploadMbps = upload
};

static void TestAutoLockPolicy()
{
    LteCellRecommendation bestZeroDrop = Recommendation(0, 34.57, 5.18);
    LteCellRecommendation slowZeroDrop = Recommendation(0, 2.59, 3.83);
    LteCellRecommendation fastWithOneDrop = Recommendation(0.89, 19.78, 5.42);
    LteCellRecommendation[] observedExample =
        [bestZeroDrop, slowZeroDrop, fastWithOneDrop];
    LteRecommendationScoring.AssignScores(observedExample);
    Require(bestZeroDrop.WeightedScore > fastWithOneDrop.WeightedScore &&
            fastWithOneDrop.WeightedScore > slowZeroDrop.WeightedScore,
        "The requested 50/40/10 weights should place the faster one-drop " +
        "profile above the very slow zero-drop profile.");

    LteCellRecommendation reliableSlow = Recommendation(0.10, 50, 10);
    LteCellRecommendation lessReliableFast = Recommendation(0.20, 200, 50);
    LteCellRecommendation[] speedWeighted = [reliableSlow, lessReliableFast];
    Require(!LteAutoLockPolicy.IsMeaningfullyBetter(
            reliableSlow,
            lessReliableFast,
            speedWeighted),
        "A 40% reliability advantage must not automatically override the " +
        "combined 60% speed score.");
    Require(LteAutoLockPolicy.IsMeaningfullyBetter(
            lessReliableFast,
            reliableSlow,
            speedWeighted),
        "The 50/40/10 score should allow a materially faster profile to win.");

    LteCellRecommendation fasterDown = Recommendation(0, 140, 10);
    LteCellRecommendation fasterUp = Recommendation(0, 100, 50);
    LteCellRecommendation[] downloadWeighted = [fasterDown, fasterUp];
    Require(LteAutoLockPolicy.IsMeaningfullyBetter(
            fasterDown,
            fasterUp,
            downloadWeighted),
        "The 50% download share should outweigh the 10% upload share.");

    LteCellRecommendation slowerDown = Recommendation(0, 90, 100);
    LteCellRecommendation fasterDownLowUpload = Recommendation(0, 100, 10);
    LteCellRecommendation[] uploadCannotOverride =
        [slowerDown, fasterDownLowUpload];
    Require(!LteAutoLockPolicy.IsMeaningfullyBetter(
            slowerDown,
            fasterDownLowUpload,
            uploadCannotOverride),
        "The 10% upload share must not override the 50% download share.");

    LteCellRecommendation upload40 = Recommendation(0, 100, 40);
    LteCellRecommendation upload20 = Recommendation(0, 100, 20);
    LteCellRecommendation[] uploadTieBreak = [upload40, upload20];
    Require(LteAutoLockPolicy.IsMeaningfullyBetter(
            upload40,
            upload20,
            uploadTieBreak),
        "Upload should decide when disconnections and download are equal.");

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
            lte!.Reason.Contains("LTE band changed", StringComparison.Ordinal) &&
            lte.Reason.Contains("LTE cell changed", StringComparison.Ordinal),
        "Band and cell changes should coalesce into one attributed test.");

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

static LteCellRecommendation Recommendation(
    double dropsPerHour,
    double download,
    double upload,
    TimeSpan? periodConnectedTime = null,
    bool userAdded = false) => new()
    {
        Key = Guid.NewGuid().ToString("N"),
        Band = "B3",
        Earfcn = "1300",
        Pci = "100",
        DisconnectionsPerHour = dropsPerHour,
        AverageDownloadMbps = download,
        AverageUploadMbps = upload,
        PeriodConnectedTime = periodConnectedTime ?? TimeSpan.Zero,
        TimePeriod = "Morning 06–12",
        UsageBasis = "data",
        Confidence = "High",
        IsEligible = true,
        UserAdded = userAdded
    };
