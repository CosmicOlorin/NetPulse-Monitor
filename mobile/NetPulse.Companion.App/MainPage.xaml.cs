using NetPulse.Companion;

namespace NetPulse.Companion.App;

public partial class MainPage : ContentPage
{
    private const string PairingStorageKey = "netpulse-pairing-v1";
    private const string LiveNotificationPreference = "live-connection-notification";
    private CompanionClient? _client;
    private CancellationTokenSource? _pollCancellation;
    private bool _loaded;
    private MobileSmsMessage? _selectedSms;
    private MobileSmsListItem? _selectedSmsListItem;
    private IReadOnlyList<MobileSmsMessage> _smsMessages = [];
    private bool _showSmsDrafts;
    private MobileLteProfile? _selectedLte;
    private int? _lastUnreadCount;
    private string _activeSection = "Status";
    private DateTime _nextDevicesRefreshUtc = DateTime.MinValue;
    private bool _devicesBusy;
    public MainPage()
    {
        InitializeComponent();
        LiveNotificationSwitch.IsToggled = Preferences.Default.Get(LiveNotificationPreference, true);
    }

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
            await CheckForUpdateAsync(false);
        }
        catch (Exception ex)
        {
            PairingPanel.IsVisible = true; DashboardPanel.IsVisible = false; ConnectionBadge.Text = "OFFLINE";
            PairingErrorLabel.Text = FriendlyError(ex); PairingErrorLabel.IsVisible = true;
        }
    }

    private async void OnCheckUpdateClicked(object sender, EventArgs e) => await CheckForUpdateAsync(true);

    private async Task CheckForUpdateAsync(bool userInitiated)
    {
        if (_client is null) return;
        try
        {
            UpdateButton.IsEnabled = false;
            UpdateStatusLabel.Text = "Checking the paired PC…";
            AndroidAppRelease release = await _client.ReadAndroidAppReleaseAsync();
            Version installed = Version.Parse(AppInfo.Current.VersionString);
            Version available = Version.Parse(release.DisplayVersion);
            if (available <= installed)
            {
                UpdateStatusLabel.Text = $"Version {installed} is up to date.";
                return;
            }

            UpdateStatusLabel.Text = $"Version {release.DisplayVersion} is available ({release.Size / 1024d / 1024d:0.0} MB).";
            bool download = await DisplayAlert("NetPulse update", $"Download and install version {release.DisplayVersion}? Android will ask you to confirm installation.", "Update", "Later");
            if (download)
            {
                UpdateProgressBar.Progress = 0;
                UpdateProgressBar.IsVisible = true;
                string apkPath = Path.Combine(FileSystem.CacheDirectory, $"NetPulse-{release.DisplayVersion}.apk");
                var progress = new Progress<double>(value =>
                {
                    UpdateProgressBar.Progress = value;
                    UpdateStatusLabel.Text = $"Downloading version {release.DisplayVersion}… {value:P0}";
                });
                await _client.DownloadAndroidUpdateAsync(release, apkPath, progress);
                UpdateStatusLabel.Text = "Download verified. Confirm installation in Android.";
                await Launcher.Default.OpenAsync(new OpenFileRequest("Install NetPulse update", new ReadOnlyFile(apkPath)));
            }
        }
        catch (Exception ex)
        {
            UpdateStatusLabel.Text = userInitiated ? FriendlyError(ex) : "Automatic update check will retry later.";
        }
        finally { UpdateButton.IsEnabled = true; UpdateProgressBar.IsVisible = false; }
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
                    if (snapshot.UnreadSmsCount.GetValueOrDefault() > _lastUnreadCount.GetValueOrDefault()) _ = NotifyNewSmsAsync();
                    _lastUnreadCount = snapshot.UnreadSmsCount;
                    if (_activeSection == "Devices" &&
                        DateTime.UtcNow >= _nextDevicesRefreshUtc)
                    {
                        _nextDevicesRefreshUtc = DateTime.UtcNow.AddSeconds(10);
                        _ = MainThread.InvokeOnMainThreadAsync(RefreshDevicesAsync);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex) { MainThread.BeginInvokeOnMainThread(() => { ConnectionBadge.Text = "RETRYING"; LiveErrorLabel.Text = FriendlyError(ex); LiveErrorLabel.IsVisible = true; }); }
                try { await Task.Delay(1000, token); } catch (OperationCanceledException) { break; }
            }
        }, token);
    }

    private async Task NotifyNewSmsAsync()
    {
        if (_client is null) return;
        try
        {
            List<MobileSmsMessage> unread = (await _client.ReadSmsAsync()).Where(message => message.IsUnread).ToList();
            var notified = Preferences.Default.Get("notified-sms", "").Split('|', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            foreach (MobileSmsMessage message in unread.Where(message => !notified.Contains(message.Identity)))
            {
                ShowSmsNotification(message);
                notified.Add(message.Identity);
            }
            Preferences.Default.Set("notified-sms", string.Join('|', notified.TakeLast(500)));
        }
        catch { }
    }

    private static void ShowSmsNotification(MobileSmsMessage message)
    {
#if ANDROID
        const string channelId = "netpulse_sms";
        Android.Content.Context context = Android.App.Application.Context;
        var manager = (Android.App.NotificationManager?)context.GetSystemService(Android.Content.Context.NotificationService);
        if (manager is null) return;
        Android.App.Notification.Builder builder;
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
        {
            manager.CreateNotificationChannel(new Android.App.NotificationChannel(channelId, "Router SMS", Android.App.NotificationImportance.High));
            builder = new Android.App.Notification.Builder(context, channelId);
        }
        else
            builder = new Android.App.Notification.Builder(context);
        builder.SetContentTitle(message.Address)
            .SetContentText(message.Content)
            .SetSmallIcon(Android.Resource.Drawable.IcDialogEmail)
            .SetAutoCancel(true);
        manager.Notify(StringComparer.Ordinal.GetHashCode(message.Identity), builder.Build());
#endif
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
        if (LiveNotificationSwitch.IsToggled) ShowLiveConnectionNotification(s);
    }

    private static string Rate(long? bytesPerSecond)
    {
        if (!bytesPerSecond.HasValue) return "—";
        double bits = Math.Max(0, bytesPerSecond.Value) * 8d;
        return bits >= 1_000_000 ? $"{bits / 1_000_000:0.##} Mbps" : $"{bits / 1_000:0.#} Kbps";
    }

    private async void OnLiveNotificationToggled(object sender, ToggledEventArgs e)
    {
        Preferences.Default.Set(LiveNotificationPreference, e.Value);
#if ANDROID
        if (!e.Value)
        {
            Android.Content.Context context = Android.App.Application.Context;
            var manager = (Android.App.NotificationManager?)context.GetSystemService(Android.Content.Context.NotificationService);
            manager?.Cancel(17011);
            return;
        }
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Tiramisu)
            await Permissions.RequestAsync<Permissions.PostNotifications>();
#endif
    }

    private static void ShowLiveConnectionNotification(MobileSnapshot snapshot)
    {
#if ANDROID
        const string channelId = "netpulse_live";
        Android.Content.Context context = Android.App.Application.Context;
        var manager = (Android.App.NotificationManager?)context.GetSystemService(Android.Content.Context.NotificationService);
        if (manager is null) return;
        Android.App.Notification.Builder builder;
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
        {
            var channel = new Android.App.NotificationChannel(channelId, "Live connection", Android.App.NotificationImportance.Low);
            channel.SetSound(null, null);
            manager.CreateNotificationChannel(channel);
            builder = new Android.App.Notification.Builder(context, channelId);
        }
        else
            builder = new Android.App.Notification.Builder(context);
        string title = $"↓ {Rate(snapshot.DownloadBytesPerSecond)}   ↑ {Rate(snapshot.UploadBytesPerSecond)}";
        string detail = $"Ping {Value(snapshot.PingMs, "ms")}  ·  LTE {Empty(snapshot.Band)}";
        builder.SetContentTitle(title)
            .SetContentText(detail)
            .SetStyle(new Android.App.Notification.BigTextStyle().BigText(detail))
            .SetSmallIcon(Android.Resource.Drawable.IcMenuInfoDetails)
            .SetOnlyAlertOnce(true)
            .SetOngoing(true)
            .SetShowWhen(false);
        manager.Notify(17011, builder.Build());
#endif
    }

    private void ShowSection(View section)
    {
        StatusContent.IsVisible = section == StatusContent;
        SmsContent.IsVisible = section == SmsContent;
        LteContent.IsVisible = section == LteContent;
        DevicesContent.IsVisible = section == DevicesContent;
        _activeSection = section == DevicesContent ? "Devices" :
            section == SmsContent ? "SMS" :
            section == LteContent ? "LTE" : "Status";
    }

    private void OnStatusTabClicked(object sender, EventArgs e) => ShowSection(StatusContent);
    private async void OnSmsTabClicked(object sender, EventArgs e) { ShowSection(SmsContent); await RefreshSmsAsync(); }
    private async void OnLteTabClicked(object sender, EventArgs e) { ShowSection(LteContent); await RefreshLteAsync(); }
    private async void OnDevicesTabClicked(object sender, EventArgs e) { ShowSection(DevicesContent); await RefreshDevicesAsync(); }
    private async void OnRefreshSmsClicked(object sender, EventArgs e) => await RefreshSmsAsync();
    private void OnSmsConversationsClicked(object sender, EventArgs e)
    {
        _showSmsDrafts = false;
        PopulateSmsViews();
    }
    private void OnSmsDraftsClicked(object sender, EventArgs e)
    {
        _showSmsDrafts = true;
        PopulateSmsViews();
    }
    private void OnNewSmsClicked(object sender, EventArgs e)
    {
        _selectedSms = null;
        _selectedSmsListItem = null;
        SmsList.SelectedItem = null;
        SmsThreadList.SelectedItem = null;
        SmsThreadList.ItemsSource = null;
        SmsThreadHeading.Text = "New SMS";
        SmsPhoneEntry.Text = "";
        SmsBodyEntry.Text = "";
        SendSmsButton.Text = "Send SMS";
        UpdateSmsActionButtons();
        SmsPhoneEntry.Focus();
    }
    private async void OnRefreshLteClicked(object sender, EventArgs e) => await RefreshLteAsync();
    private async void OnRefreshDevicesClicked(object sender, EventArgs e) => await RefreshDevicesAsync();

    private async Task RefreshDevicesAsync()
    {
        if (_client is null || _devicesBusy) return;
        _devicesBusy = true;
        try
        {
            DevicesStatusLabel.Text = "Reading the router…";
            List<MobileConnectedDevice> devices = await _client.ReadConnectedDevicesAsync();
            DevicesList.ItemsSource = devices;
            DevicesStatusLabel.Text = devices.Count == 0
                ? "No active client devices were reported by the router."
                : $"{devices.Count} active device{(devices.Count == 1 ? "" : "s")} · live data · not stored";
        }
        catch (Exception ex) { DevicesStatusLabel.Text = FriendlyError(ex); }
        finally
        {
            _nextDevicesRefreshUtc = DateTime.UtcNow.AddSeconds(10);
            _devicesBusy = false;
        }
    }

    private async Task RefreshSmsAsync(
        string? preferredAddress = null,
        string? preferredIdentity = null)
    {
        if (_client is null) return;
        try
        {
            SmsStatusLabel.Text = "Loading…";
            preferredAddress ??= _selectedSmsListItem?.Address;
            preferredIdentity ??= _selectedSms?.Identity;
            _smsMessages = (await _client.ReadSmsAsync())
                .OrderByDescending(message => message.Timestamp ?? DateTime.MinValue)
                .ToArray();
            PopulateSmsViews(preferredAddress, preferredIdentity);
        }
        catch (Exception ex) { SmsStatusLabel.Text = FriendlyError(ex); }
    }

    private void PopulateSmsViews(
        string? preferredAddress = null,
        string? preferredIdentity = null)
    {
        IReadOnlyList<MobileSmsListItem> items = _showSmsDrafts
            ? MobileSmsOrganizer.Drafts(_smsMessages)
            : MobileSmsOrganizer.Conversations(_smsMessages);
        SmsConversationsButton.BackgroundColor = Color.FromArgb(
            _showSmsDrafts ? "#25445E" : "#16875D");
        SmsDraftsButton.BackgroundColor = Color.FromArgb(
            _showSmsDrafts ? "#16875D" : "#25445E");
        SmsList.ItemsSource = items;

        int drafts = _smsMessages.Count(message => message.IsDraft);
        int conversations = MobileSmsOrganizer.Conversations(_smsMessages).Count;
        SmsStatusLabel.Text =
            $"{conversations} conversation{(conversations == 1 ? "" : "s")} · " +
            $"{drafts} draft{(drafts == 1 ? "" : "s")}";

        MobileSmsListItem? selected = !string.IsNullOrWhiteSpace(preferredAddress)
            ? items.FirstOrDefault(item => MobileSmsOrganizer.SameAddress(
                item.Address,
                preferredAddress))
            : null;
        SmsList.SelectedItem = null;
        if (selected is null)
        {
            ClearSmsSelection();
            return;
        }
        SmsList.SelectedItem = selected;
        ShowSmsListItem(selected, preferredIdentity);
    }

    private void OnSmsListSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is MobileSmsListItem item)
            ShowSmsListItem(item);
    }

    private void ShowSmsListItem(
        MobileSmsListItem item,
        string? preferredIdentity = null)
    {
        _selectedSmsListItem = item;
        SmsThreadHeading.Text = item.DisplayAddress;
        SmsThreadList.ItemsSource = item.Messages;
        SmsPhoneEntry.Text = item.Address;
        MobileSmsMessage selected = item.Messages.FirstOrDefault(message =>
                string.Equals(message.Identity, preferredIdentity,
                    StringComparison.Ordinal))
            ?? item.Latest;
        SmsThreadList.SelectedItem = selected;
        SelectSmsMessage(selected);
    }

    private void OnSmsThreadSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is MobileSmsMessage message)
            SelectSmsMessage(message);
    }

    private void SelectSmsMessage(MobileSmsMessage message)
    {
        _selectedSms = message;
        SmsPhoneEntry.Text = message.Address;
        if (message.IsDraft)
        {
            SmsBodyEntry.Text = message.Content;
            SendSmsButton.Text = "Send draft";
            SmsStatusLabel.Text = "Draft loaded. Review it, then send it again.";
        }
        else
        {
            SmsBodyEntry.Text = "";
            SendSmsButton.Text = "Send SMS";
        }
        UpdateSmsActionButtons();
    }

    private void ClearSmsSelection()
    {
        _selectedSms = null;
        _selectedSmsListItem = null;
        SmsThreadList.SelectedItem = null;
        SmsThreadList.ItemsSource = null;
        SmsThreadHeading.Text = _showSmsDrafts
            ? "Select a draft"
            : "Select a conversation";
        SendSmsButton.Text = "Send SMS";
        UpdateSmsActionButtons();
    }

    private void UpdateSmsActionButtons()
    {
        MarkReadSmsButton.IsEnabled = _selectedSms is
        {
            IsInbox: true,
            IsUnread: true
        };
        DeleteSmsButton.IsEnabled = _selectedSms is not null;
    }

    private async void OnMarkReadClicked(object sender, EventArgs e)
    {
        if (_client is null || _selectedSms is null) return;
        try
        {
            string address = _selectedSms.Address;
            string identity = _selectedSms.Identity;
            await _client.SetSmsUnreadAsync(_selectedSms, false);
            await RefreshSmsAsync(address, identity);
        }
        catch (Exception ex) { SmsStatusLabel.Text = FriendlyError(ex); }
    }

    private async void OnDeleteSmsClicked(object sender, EventArgs e)
    {
        if (_client is null || _selectedSms is null || !await DisplayAlert("Delete SMS", "Delete this message from the router?", "Delete", "Cancel")) return;
        try
        {
            string? address = _selectedSms.IsDraft ? null : _selectedSms.Address;
            await _client.DeleteSmsAsync(_selectedSms);
            _selectedSms = null;
            await RefreshSmsAsync(address);
        }
        catch (Exception ex) { SmsStatusLabel.Text = FriendlyError(ex); }
    }

    private async void OnSendSmsClicked(object sender, EventArgs e)
    {
        if (_client is null || string.IsNullOrWhiteSpace(SmsPhoneEntry.Text) || string.IsNullOrWhiteSpace(SmsBodyEntry.Text)) return;
        if (!await DisplayAlert("Send SMS", $"Send this message to {SmsPhoneEntry.Text.Trim()}?", "Send", "Cancel")) return;
        try
        {
            string recipient = SmsPhoneEntry.Text.Trim();
            SmsStatusLabel.Text = _selectedSms?.IsDraft == true ? "Sending draft…" : "Sending…";
            await _client.SendSmsAsync(recipient, SmsBodyEntry.Text);
            if (_selectedSms?.IsDraft == true)
                await _client.DeleteSmsAsync(_selectedSms);
            _selectedSms = null;
            _showSmsDrafts = false;
            SmsBodyEntry.Text = "";
            SendSmsButton.Text = "Send SMS";
            SmsStatusLabel.Text = "Sent.";
            await RefreshSmsAsync(recipient);
        }
        catch (Exception ex) { SmsStatusLabel.Text = FriendlyError(ex); }
    }

    private async Task RefreshLteAsync()
    {
        if (_client is null) return;
        try { LteStatusLabel.Text = "Loading…"; LteList.ItemsSource = await _client.ReadLteHistoryAsync(); LteStatusLabel.Text = ""; }
        catch (Exception ex) { LteStatusLabel.Text = FriendlyError(ex); }
    }

    private void OnLteSelected(object sender, SelectionChangedEventArgs e)
    {
        _selectedLte = e.CurrentSelection.FirstOrDefault() as MobileLteProfile;
        if (_selectedLte is null) return;
        BandsEntry.Text = _selectedLte.PrimaryBand.Trim().TrimStart('B', 'b');
        LockEarfcnEntry.Text = _selectedLte.Earfcn; LockPciEntry.Text = _selectedLte.Pci; LockCidEntry.Text = _selectedLte.CellId;
    }

    private async void OnApplyLockClicked(object sender, EventArgs e)
    {
        if (_client is null) return;
        int[] bands = (BandsEntry.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(value => int.TryParse(value.TrimStart('B', 'b'), out int band) ? band : 0).Where(value => value > 0).ToArray();
        if (bands.Length != 1) { LteStatusLabel.Text = "Enter exactly one PCell band. The router selects SCells automatically."; return; }
        if (string.IsNullOrWhiteSpace(LockCidEntry.Text)) { LteStatusLabel.Text = "CID is required so different serving cells remain separate."; return; }
        if (!await DisplayAlert("Apply LTE lock", "The router connection may briefly disconnect. Apply this band/cell lock?", "Apply", "Cancel")) return;
        try { LteStatusLabel.Text = "Applying…"; await _client.ApplyLteLockAsync(bands, LockEarfcnEntry.Text ?? "", LockPciEntry.Text ?? "", LockCidEntry.Text); LteStatusLabel.Text = "Lock applied."; }
        catch (Exception ex) { LteStatusLabel.Text = FriendlyError(ex); }
    }

    private async void OnApplyBandLockClicked(object sender, EventArgs e)
    {
        if (_client is null) return;
        int[] bands = (BandsEntry.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(value => int.TryParse(value.TrimStart('B', 'b'), out int band) ? band : 0).Where(value => value > 0).ToArray();
        if (bands.Length != 1) { LteStatusLabel.Text = "Enter exactly one PCell band."; return; }
        if (!await DisplayAlert("Apply Band Lock", $"Lock the modem to B{bands[0]} while leaving Cell Lock disabled?", "Apply", "Cancel")) return;
        try { LteStatusLabel.Text = "Applying band lock…"; await _client.ApplyLteBandLockAsync(bands[0]); LteStatusLabel.Text = "Band Lock applied; cell selection remains automatic."; }
        catch (Exception ex) { LteStatusLabel.Text = FriendlyError(ex); }
    }

    private async void OnRestoreAutomaticClicked(object sender, EventArgs e)
    {
        if (_client is null || !await DisplayAlert("Restore automatic", "Return the router to automatic cell and band selection?", "Restore", "Cancel")) return;
        try { LteStatusLabel.Text = "Restoring…"; await _client.RestoreAutomaticAsync(); LteStatusLabel.Text = "Automatic selection restored."; }
        catch (Exception ex) { LteStatusLabel.Text = FriendlyError(ex); }
    }

    private async void OnRestartRouterClicked(object sender, EventArgs e)
    {
        if (_client is null || !await DisplayAlert("Restart router", "Restart the TP-Link router now? Internet and Companion access will be unavailable for several minutes.", "Restart", "Cancel")) return;
        try { LteStatusLabel.Text = "Restarting router…"; await _client.RebootRouterAsync(); LteStatusLabel.Text = "Restart requested. Waiting for the router to return…"; }
        catch (Exception ex) { LteStatusLabel.Text = FriendlyError(ex); }
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
