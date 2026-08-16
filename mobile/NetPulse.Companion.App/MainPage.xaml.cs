using NetPulse.Companion;

namespace NetPulse.Companion.App;

public partial class MainPage : ContentPage
{
    private const string PairingStorageKey = "netpulse-pairing-v1";
    private CompanionClient? _client;
    private CancellationTokenSource? _pollCancellation;
    private bool _loaded;
    public MainPage() => InitializeComponent();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        string? code = await SecureStorage.Default.GetAsync(PairingStorageKey);
        if (!string.IsNullOrWhiteSpace(code)) await ConnectAsync(code, false);
    }

    protected override void OnDisappearing() { _pollCancellation?.Cancel(); base.OnDisappearing(); }
    private async void OnConnectClicked(object sender, EventArgs e) => await ConnectAsync(PairingCodeEntry.Text?.Trim() ?? "", true);
    private async void OnScanQrClicked(object sender, EventArgs e)
    {
        var scanner = new QrScannerPage();
        string? result = await scanner.ScanAsync(Navigation);
        if (!string.IsNullOrWhiteSpace(result)) { PairingCodeEntry.Text = result; await ConnectAsync(result, true); }
    }

    private async Task ConnectAsync(string code, bool save)
    {
        PairingErrorLabel.IsVisible = false;
        try
        {
            var profile = PairingProfile.Parse(code);
            _pollCancellation?.Cancel(); _client?.Dispose();
            _client = new CompanionClient(profile);
            MobileSnapshot first = await _client.ReadSnapshotAsync();
            if (save) await SecureStorage.Default.SetAsync(PairingStorageKey, code);
            PairingPanel.IsVisible = false; DashboardPanel.IsVisible = true; ConnectionBadge.Text = "CONNECTED";
            UpdateDashboard(first); StartPolling();
        }
        catch (Exception ex)
        {
            PairingPanel.IsVisible = true; DashboardPanel.IsVisible = false; ConnectionBadge.Text = "OFFLINE";
            PairingErrorLabel.Text = FriendlyError(ex); PairingErrorLabel.IsVisible = true;
        }
    }

    private void StartPolling()
    {
        _pollCancellation = new CancellationTokenSource();
        CancellationToken token = _pollCancellation.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && _client is not null)
            {
                try
                {
                    MobileSnapshot snapshot = await _client.ReadSnapshotAsync(token);
                    MainThread.BeginInvokeOnMainThread(() => { LiveErrorLabel.IsVisible = false; ConnectionBadge.Text = "CONNECTED"; UpdateDashboard(snapshot); });
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex) { MainThread.BeginInvokeOnMainThread(() => { ConnectionBadge.Text = "RETRYING"; LiveErrorLabel.Text = FriendlyError(ex); LiveErrorLabel.IsVisible = true; }); }
                try { await Task.Delay(1000, token); } catch (OperationCanceledException) { break; }
            }
        }, token);
    }

    private void UpdateDashboard(MobileSnapshot s)
    {
        LastUpdateLabel.Text = $"Updated {s.Timestamp.ToLocalTime():g}";
        InternetLabel.Text = s.InternetOnline ? "ONLINE" : "OFFLINE";
        InternetLabel.TextColor = Color.FromArgb(s.InternetOnline ? "#63D9A7" : "#FF7B72");
        RouterLabel.Text = $"{s.RouterState} · {(s.LteRegistered ? "registered" : "not registered")}";
        PingLabel.Text = $"{Value(s.PingMs, "ms")} · {s.JitterMs:0.#} ms";
        QualityLabel.Text = $"{s.PacketLossPercent:0.#}% · {s.AvailabilityPercent:0.###}%";
        ProfileLabel.Text = string.Join(" · ", new[] { s.NetworkType, s.Band, s.PrimaryBand }.Where(v => !string.IsNullOrWhiteSpace(v)));
        SignalLabel.Text = $"RSRP {Value(s.RsrpDbm, "dBm")} · RSRQ {Value(s.RsrqDb, "dB")} · SNR {Value(s.SnrDb, "dB")}";
        EarfcnLabel.Text = Empty(s.Earfcn); IdentityLabel.Text = $"{Empty(s.Pci)} / {Empty(s.CellId)}";
        EventsLabel.Text = $"{s.Outages} / {(s.UnreadSmsCount?.ToString() ?? "—")}";
    }

    private void OnForgetClicked(object sender, EventArgs e)
    {
        _pollCancellation?.Cancel(); _client?.Dispose(); _client = null; SecureStorage.Default.Remove(PairingStorageKey);
        PairingCodeEntry.Text = ""; DashboardPanel.IsVisible = false; PairingPanel.IsVisible = true; PairingErrorLabel.IsVisible = false; ConnectionBadge.Text = "NOT PAIRED";
    }

    private static string Empty(string? v) => string.IsNullOrWhiteSpace(v) ? "—" : v;
    private static string Value<T>(T? v, string unit) where T : struct => v is null ? "—" : $"{v} {unit}";
    private static string FriendlyError(Exception ex) => ex switch
    {
        FormatException or NotSupportedException => ex.Message,
        HttpRequestException => "The PC companion is not reachable. Confirm both devices are on the same Wi-Fi and the desktop companion is enabled.",
        _ => $"Connection failed: {ex.Message}"
    };
}
