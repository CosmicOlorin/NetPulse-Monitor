using System.Diagnostics;

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace NetPulseMonitor;

internal sealed class MainForm : Form
{
    private const int SmsConversationsView = 0;
    private const int SmsDraftsView = 1;
    private const int SmsTimelineView = 2;

    private readonly CsvLogger _logger;
    private readonly LteCellHistoryStore _cellHistory;
    private AppSettings _settings = AppSettings.Load();
    private OfficialClock _clock;
    private MonitorEngine _engine;
    private RouterMonitor _routerMonitor;
    private readonly CompanionService _companionService;
    private string _routerPassword = "";

    private readonly Dictionary<string, Label> _metrics = new();
    private readonly Dictionary<string, Panel> _metricCards = new();
    private readonly TableLayoutPanel _dashboardMetricGrid = new();
    private readonly TableLayoutPanel _dashboardExperienceGrid = new();
    private readonly Dictionary<string, Label> _routerMetrics = new();
    private readonly Dictionary<string, Label> _routerMetricCaptions = new();
    private readonly Dictionary<string, Panel> _routerMetricCards = new();
    private readonly TableLayoutPanel _routerMetricGrid = new();
    private readonly Button _connectionDetailsToggleButton = new();
    private RowStyle? _dashboardConnectionRowStyle;
    private bool _connectionDetailsExpanded;
    private readonly PingChartControl _chart = new();
    private readonly DataGridView _eventsGrid = new();
    private readonly DataGridView _cellHistoryGrid = new FlickerFreeDataGridView();
    private readonly DataGridView _smsGrid = new();
    private readonly DataGridView _connectedDevicesGrid = new();
    private readonly List<MonitorEvent> _eventHistory = [];
    private readonly ConnectionTimelineTracker _connectionTimeline = new();
    private readonly ThemedTabControl _tabs = new();
    private readonly Label _cellSuggestion = new();
    private readonly Label _cellAutoStatus = new();
    private readonly Label _statusBadge = new();
    private readonly Label _footer = new();
    private readonly Button _speedButton = new();
    private readonly Button _pauseButton = new();
    private readonly NotifyIcon _trayIcon = new();
    private readonly System.Windows.Forms.Timer _uiTimer = new();
    private readonly AutomaticSpeedTestCoordinator _automaticSpeedTests = new();
    private readonly Font _smsUnreadFont =
        new("Segoe UI", 9F, FontStyle.Bold);
    private readonly Font _cellGroupFont =
        new("Segoe UI", 9F, FontStyle.Bold);

    private readonly TextBox _targetInput = new();
    private readonly NumericUpDown _pingIntervalInput = new();
    private readonly NumericUpDown _failureInput = new();
    private readonly NumericUpDown _speedIntervalInput = new();
    private readonly CheckBox _startupInput = new();
    private readonly CheckBox _trayInput = new();
    private readonly CheckBox _routerEnabledInput = new();
    private readonly CheckBox _automaticCellLockInput = new();
    private readonly TextBox _routerAddressInput = new();
    private readonly Button _routerSetupButton = new();
    private readonly Button _regionalSetupButton = new();
    private readonly Button _companionSetupButton = new();
    private readonly ComboBox _mobileNetworkModeInput = new();
    private readonly Button _mobileNetworkModeRefreshButton = new();
    private readonly Button _mobileNetworkModeApplyButton = new();
    private readonly AutoFitLabel _routerConnectionState = new();
    private readonly Label _routerDetails = new();
    private readonly ComboBox _connectionViewInput = new();
    private readonly ComboBox _observedCellLockInput = new();
    private readonly CueTextBox _manualBandsInput = new();
    private readonly CueTextBox _manualEarfcnInput = new();
    private readonly CueTextBox _manualPciInput = new();
    private readonly CueTextBox _manualCidInput = new();
    private readonly Label _manualLockStatus = new();
    private readonly Label _smsStatus = new();
    private readonly Label _smsSender = new();
    private readonly Label _smsReceived = new();
    private readonly FlowLayoutPanel _smsThreadPanel = new();
    private readonly TextBox _smsRecipientInput = new();
    private readonly TextBox _smsComposeInput = new();
    private readonly Label _smsLength = new();
    private readonly Button _smsRefreshButton = new();
    private readonly Button _smsReadButton = new();
    private readonly Button _smsUnreadButton = new();
    private readonly Button _smsDeleteButton = new();
    private readonly Button _smsDraftButton = new();
    private readonly Button _smsContactButton = new();
    private readonly Button _smsSendButton = new();
    private readonly TextBox _smsSearchInput = new();
    private readonly ComboBox _smsViewInput = new();
    private bool _smsConversationInitialized;
    private string? _activeSmsConversationAddress;
    private RouterSmsMessage? _selectedSmsMessage;
    private bool _smsNewConversation;
    private bool _settingSmsRecipient;

    private readonly Label _healthScore = new();
    private readonly Label _healthSummary = new();
    private readonly Label _smartRecommendation = new();
    private readonly Button _smartApplyButton = new();
    private readonly Button _smartTestButton = new();
    private readonly Label _updateStatus = new();
    private readonly Button _updateButton = new();
    private readonly Panel _healthCard = new();
    private readonly Panel _smartCard = new();
    private readonly Panel _updatesCard = new();
    private readonly ComboBox _themeInput = new();
    private readonly ComboBox _dashboardLayoutInput = new();
    private readonly CheckBox _healthSummaryInput = new();
    private readonly CheckBox _smartRecommendationInput = new();
    private readonly CheckBox _updateCheckInput = new();
    private readonly NumericUpDown _experimentMinutesInput = new();
    private readonly Button _experimentButton = new();
    private readonly Label _experimentStatus = new();
    private readonly Button _bandDiscoveryButton = new();
    private readonly ProgressBar _bandDiscoveryProgress = new();
    private readonly List<Control> _lteProfileMutationControls = [];
    private readonly ToolTip _buttonTips = new()
    {
        InitialDelay = 350,
        ReshowDelay = 100,
        AutoPopDelay = 12000,
        ShowAlways = true
    };
    private readonly Label _bandDiscoveryStatus = new();
    private readonly ComboBox _eventFilterInput = new();
    private readonly TextBox _eventSearchInput = new();
    private readonly Label _troubleshootingSummary = new();
    private readonly Label _connectedDevicesStatus = new();

    private readonly Label _gatewayValue = new();
    private readonly Label _gatewayPingValue = new();
    private readonly Label _dnsValue = new();
    private readonly Label _ipv4Value = new();
    private readonly Label _ipv6Value = new();
    private readonly Label _diagnosticsSummary = new();

    private CancellationTokenSource? _speedCancellation;
    private CancellationTokenSource? _smsSendCancellation;
    private CancellationTokenSource? _experimentCancellation;
    private CancellationTokenSource? _bandDiscoveryCancellation;
    private Task? _bandDiscoveryTask;
    private bool _speedBusy;
    private bool _speedTestManual;
    private bool _allowExit;
    private DateTime _nextAutomaticSpeedTest;
    private bool _trayHintShown;
    private volatile bool _externalActivationPending;
    private long _lastCellHistoryRevision = -1;
    private int _lastCellHistoryPeriod = -1;
    private bool _cellLockBusy;
    private volatile bool _bandDiscoveryActive;
    private bool _bandDiscoveryExitPending;
    private DateTime _nextAutomaticCellLockCheckUtc = DateTime.MinValue;
    private DateTime _nextPublicIpCheckUtc = DateTime.MinValue;
    private bool _publicIpCheckBusy;
    private bool _smsBusy;
    private DateTime _nextAutomaticSmsRefreshUtc = DateTime.MinValue;
    private DateTime _nextAutomaticDiagnosticsUtc = DateTime.MinValue;
    private bool _diagnosticsBusy;
    private DiagnosticResult? _lastDiagnosticResult;
    private SpeedTestResult? _lastSpeedResult;
    private LteCellRecommendation? _smartCandidate;
    private UpdateCheckResult? _availableUpdate;
    private readonly UnreadSmsAlertTracker _unreadSmsAlerts = new(
        Path.Combine(AppSettings.SettingsFolder, "sms-notification-hashes.txt"));
    private readonly Queue<string> _smsNotificationQueue = new();
    private readonly System.Windows.Forms.Timer _smsNotificationTimer = new();
    private string? _activeSmsNotificationIdentity;
    private int _lastRouterUnreadSmsCount = -1;
    private bool _populatingSmsGrid;
    private string _lastPublicIp = "";
    private string _trackedConnectionFingerprint = "";
    private DateTime _currentConnectionSinceUtc = DateTime.UtcNow;
    private int _currentConnectionOutagesBaseline;
    private string _cellHistorySortColumn = "Rank";
    private bool _cellHistorySortAscending = true;
    private string _observedCellLockFingerprint = "\0";
    private bool _refreshingObservedCellLockProfiles;
    private bool _mobileNetworkModeBusy;
    private bool _connectedDevicesBusy;
    private DateTime _nextConnectedDevicesRefreshUtc = DateTime.MinValue;
    private RouterMobileNetworkModeState? _mobileNetworkModeState;
    private RouterCellLockTarget? _displayedCellLockTarget;
    private IReadOnlyList<RouterSmsMessage> _smsMessages = [];

    public MainForm()
    {
        _settings.Normalize();
        _clock = new OfficialClock(_settings);
        _logger = new CsvLogger(_clock);
        _cellHistory = new LteCellHistoryStore(
            officialTimeZone: _clock.TimeZone);
        if (!_settings.DiscoveryHistoryImported)
        {
            _cellHistory.ImportDiscoveryCandidates(_logger.BandDiscoveryPath);
            _settings.DiscoveryHistoryImported = true;
            _settings.Save();
        }
        _routerPassword = ReadProtectedRouterPassword();
        _engine = CreateEngine();
        _routerMonitor = CreateRouterMonitor();
        _companionService = new CompanionService(CreateCompanionSnapshot, _routerMonitor, _cellHistory,
            () => new Dictionary<string, string>(_settings.SmsContacts, StringComparer.Ordinal));

        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "NetPulse Monitor";
        StartPosition = FormStartPosition.Manual;
        ApplyScreenRelativeWindowSize();
        BackColor = Color.FromArgb(244, 247, 250);
        Font = new Font("Segoe UI", 9F);
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath)
               ?? SystemIcons.Application;

        BuildInterface();
        InterfaceHelp.Install(this, _buttonTips);
        ConfigureTray();
        LoadSettingsIntoControls();
        ApplyAppearanceSettings();

        _uiTimer.Interval = 1000;
        _uiTimer.Tick += (_, _) =>
        {
            RefreshDashboard();
            RouterTelemetry router = _routerMonitor.GetSnapshot();
            RefreshRouterDashboard(router);
            HandleUnreadSmsCount(router.UnreadSmsCount);
            if (_tabs.SelectedTab?.Text == "LTE history")
                RefreshCellHistory();
            if (_tabs.SelectedTab?.Text == "Devices" &&
                DateTime.UtcNow >= _nextConnectedDevicesRefreshUtc)
                _ = RefreshConnectedDevicesAsync(showErrors: false);
            CheckAutomaticSpeedTest();
            CheckPublicIpChange();
            CheckAutomaticCellLock();
            CheckAutomaticSmsRefresh();
            CheckAutomaticDiagnostics();
        };

        Shown += async (_, _) =>
        {
            if (_externalActivationPending)
            {
                _externalActivationPending = false;
                RestoreFromTray();
            }
            if (!_settings.RegionalSetupCompleted)
                ConfigureRegionalSettings(firstRun: true);
            if (!_settings.RouterSetupCompleted)
                await ConfigureRouterAsync(firstRun: true);
            _nextAutomaticSpeedTest = GetNextSpeedTime();
            _engine.Start();
            _routerMonitor.Start();
            await RestartCompanionServiceAsync(showErrors: false);
            _nextAutomaticSmsRefreshUtc = DateTime.UtcNow;
            _nextAutomaticDiagnosticsUtc = DateTime.UtcNow;
            _uiTimer.Start();
            RefreshDashboard();
            RefreshRouterDashboard(_routerMonitor.GetSnapshot());
            RefreshCellHistory(force: true);
            _ = RecoverPendingCellLockAsync();
            if (_settings.CheckForUpdates &&
                (!_settings.LastUpdateCheckUtc.HasValue ||
                 DateTime.UtcNow - _settings.LastUpdateCheckUtc.Value > TimeSpan.FromHours(24)))
                _ = CheckForUpdatesAsync(interactive: false);
        };

        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized && _settings.MinimizeToTray)
                HideToTray();
        };

        FormClosing += OnFormClosing;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NetPulseTheme theme = Enum.TryParse(
                _settings.Theme,
                out NetPulseTheme parsed)
            ? parsed
            : NetPulseTheme.System;
        AppThemeManager.ApplyWindowFrame(this, theme);
    }

    private MonitorEngine CreateEngine()
    {
        var engine = new MonitorEngine(_settings, _logger);

        engine.SampleRecorded += sample =>
        {
            _cellHistory.RecordPingSample(sample);
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(new Action(() => _chart.AddSample(sample)));
        };

        engine.EventOccurred += evt =>
        {
            if (evt.Kind.Equals("OFFLINE", StringComparison.OrdinalIgnoreCase))
            {
                if (!_bandDiscoveryActive)
                {
                    _cellHistory.RecordConfirmedOutage(evt.Timestamp);
                    _automaticSpeedTests.ObserveOutage();
                }
            }
            else if (evt.Kind.Equals("ONLINE", StringComparison.OrdinalIgnoreCase))
            {
                if (!_bandDiscoveryActive)
                    _automaticSpeedTests.ObserveRecovery(DateTime.UtcNow);
            }
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke(new Action(() =>
                {
                    AddEventToGrid(evt);
                    if (evt.Kind.Equals("OFFLINE", StringComparison.OrdinalIgnoreCase) ||
                        evt.Kind.Equals("ONLINE", StringComparison.OrdinalIgnoreCase))
                        ShowOperationalNotification(evt);
                }));
            }
        };

        return engine;
    }

    private RouterMonitor CreateRouterMonitor()
    {
        var monitor = new RouterMonitor(_settings, _logger, _routerPassword);
        monitor.TelemetryUpdated += telemetry =>
        {
            if (!_bandDiscoveryActive)
            {
                _cellHistory.RecordTelemetry(telemetry);
                _automaticSpeedTests.ObserveRouterTelemetry(telemetry, DateTime.UtcNow);
                foreach (MonitorEvent evt in _connectionTimeline.Observe(telemetry))
                {
                    _logger.LogEvent(evt);
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke(new Action(() => AddEventToGrid(evt)));
                }
            }
        };
        monitor.EventOccurred += evt =>
        {
            _logger.LogEvent(evt);
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(new Action(() => AddEventToGrid(evt)));
        };
        return monitor;
    }

    private void BuildInterface()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 88,
            BackColor = Color.White,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(18, 8, 18, 7)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "NetPulse Monitor",
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };

        var subtitle = new Label
        {
            Text = "End-to-end Internet monitoring with optional router telemetry",
            ForeColor = Color.DimGray,
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Padding(3, 0, 0, 0)
        };

        _statusBadge.Text = "STARTING";
        _statusBadge.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        _statusBadge.ForeColor = Color.White;
        _statusBadge.BackColor = Color.DarkGoldenrod;
        _statusBadge.TextAlign = ContentAlignment.MiddleCenter;
        _statusBadge.Size = new Size(130, 36);
        _statusBadge.Dock = DockStyle.Fill;
        _statusBadge.Margin = new Padding(10, 10, 0, 10);

        header.Controls.Add(title, 0, 0);
        header.Controls.Add(subtitle, 0, 1);
        header.Controls.Add(_statusBadge, 1, 0);
        header.SetRowSpan(_statusBadge, 2);
        Controls.Add(header);

        _tabs.Dock = DockStyle.Fill;
        _tabs.Padding = new Point(16, 7);
        _tabs.TabPages.Add(BuildDashboardTab());
        _tabs.TabPages.Add(BuildConnectedDevicesTab());
        _tabs.TabPages.Add(BuildLteHistoryTab());
        _tabs.TabPages.Add(BuildManualCellLockTab());
        _tabs.TabPages.Add(BuildSmsTab());
        _tabs.TabPages.Add(BuildEventsTab());
        _tabs.TabPages.Add(BuildDiagnosticsTab());
        _tabs.TabPages.Add(BuildSettingsTab());
        _tabs.SelectedIndexChanged += async (_, _) =>
        {
            if (_tabs.SelectedTab?.Text == "SMS")
                await RefreshSmsTimelineAsync(showErrors: false);
            else if (_tabs.SelectedTab?.Text == "Devices")
                await RefreshConnectedDevicesAsync(showErrors: true);
            else if (_tabs.SelectedTab?.Text == "LTE history")
                RefreshCellHistory(force: true);
            else if (_tabs.SelectedTab?.Text == "Cell Lock")
                RefreshObservedCellLockProfiles();
            else if (_tabs.SelectedTab?.Text == "Settings")
                await RefreshMobileNetworkModeAsync(showErrors: false);
        };

        Controls.Add(_tabs);
        _tabs.BringToFront();

        var footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 38,
            BackColor = Color.White
        };

        _footer.AutoSize = false;
        _footer.AutoEllipsis = true;
        _footer.Dock = DockStyle.Fill;
        _footer.ForeColor = Color.DimGray;
        _footer.Padding = new Padding(10, 0, 10, 0);
        _footer.TextAlign = ContentAlignment.MiddleLeft;
        _footer.Text = "Logs: " + _logger.LogFolder;
        footerPanel.Controls.Add(_footer);
        Controls.Add(footerPanel);
    }

    private void ApplyScreenRelativeWindowSize()
    {
        Rectangle working = Screen.FromPoint(Cursor.Position).WorkingArea;
        int initialWidth = Math.Min(working.Width, Math.Max(1050,
            (int)Math.Round(working.Width * 0.94)));
        int initialHeight = Math.Min(working.Height, Math.Max(720,
            (int)Math.Round(working.Height * 0.94)));
        int minimumWidth = Math.Min(initialWidth, Math.Max(920,
            (int)Math.Round(working.Width * 0.55)));
        int minimumHeight = Math.Min(initialHeight, Math.Max(640,
            (int)Math.Round(working.Height * 0.62)));
        MinimumSize = new Size(minimumWidth, minimumHeight);
        Size = new Size(initialWidth, initialHeight);
        Location = new Point(
            working.Left + Math.Max(0, (working.Width - initialWidth) / 2),
            working.Top + Math.Max(0, (working.Height - initialHeight) / 2));
    }

    private TabPage BuildDashboardTab()
    {
        var page = new TabPage("Dashboard")
        {
            BackColor = Color.FromArgb(244, 247, 250),
            Padding = new Padding(12)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 5,
            ColumnCount = 1
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 115));
        _dashboardConnectionRowStyle = new RowStyle(SizeType.Absolute, 104);
        layout.RowStyles.Add(_dashboardConnectionRowStyle);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 330));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        _dashboardExperienceGrid.Dock = DockStyle.Fill;
        _dashboardExperienceGrid.ColumnCount = 3;
        _dashboardExperienceGrid.RowCount = 1;
        _dashboardExperienceGrid.Margin = Padding.Empty;
        _dashboardExperienceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
        _dashboardExperienceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        _dashboardExperienceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
        _dashboardExperienceGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        ConfigureHealthCard();
        ConfigureSmartCard();
        ConfigureUpdatesCard();
        _dashboardExperienceGrid.Controls.Add(_healthCard, 0, 0);
        _dashboardExperienceGrid.Controls.Add(_smartCard, 1, 0);
        _dashboardExperienceGrid.Controls.Add(_updatesCard, 2, 0);

        _dashboardMetricGrid.Dock = DockStyle.Fill;
        _dashboardMetricGrid.ColumnCount = 4;
        _dashboardMetricGrid.RowCount = 8;
        for (int column = 0; column < 4; column++)
            _dashboardMetricGrid.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 25F));
        for (int row = 0; row < 8; row++)
            _dashboardMetricGrid.RowStyles.Add(
                new RowStyle(SizeType.Percent, 12.5F));
        AddMetric(_dashboardMetricGrid, 0, 0, "CURRENT LTE SET / CELL", "CurrentLteSet");
        AddMetric(_dashboardMetricGrid, 0, 0, "ACCESS TECHNOLOGY", "AccessType");
        AddMetric(_dashboardMetricGrid, 1, 0, "CURRENT PUBLIC IP", "CurrentIp");
        AddMetric(_dashboardMetricGrid, 2, 0, "PC → INTERNET PING", "Ping");
        AddMetric(_dashboardMetricGrid, 3, 0, "SESSION AVG PC PING", "SessionAveragePing");

        AddMetric(_dashboardMetricGrid, 0, 1, "PC → INTERNET JITTER", "Jitter");
        AddMetric(_dashboardMetricGrid, 1, 1, "PC → INTERNET LOSS", "Loss");
        AddMetric(_dashboardMetricGrid, 2, 1, "SESSION FAILURES", "SuccessFail");
        AddMetric(_dashboardMetricGrid, 3, 1, "SESSION AVG PC JITTER", "SessionAverageJitter");

        AddMetric(_dashboardMetricGrid, 0, 2, "TOTAL DOWNTIME", "Downtime");
        AddMetric(_dashboardMetricGrid, 1, 2, "SESSION AVAILABILITY", "Availability");
        AddMetric(_dashboardMetricGrid, 2, 2, "SESSION OUTAGES", "Outages");
        AddMetric(_dashboardMetricGrid, 3, 2, "SESSION AVG PC LOSS", "SessionAverageLoss");

        AddMetric(_dashboardMetricGrid, 0, 3, "PC SPEEDTEST DOWNLOAD", "Download");
        AddMetric(_dashboardMetricGrid, 1, 3, "PC SPEEDTEST UPLOAD", "Upload");
        AddMetric(_dashboardMetricGrid, 2, 3, "PC SPEEDTEST PING", "SpeedPing");
        AddMetric(_dashboardMetricGrid, 3, 3, "PC SPEEDTEST LOSS", "SpeedLoss");
        AddMetric(
            _dashboardMetricGrid,
            0,
            4,
            "CURRENT CONNECTION + SET + IP TIME",
            "ConnectionStable");
        AddMetric(
            _dashboardMetricGrid,
            1,
            4,
            "CURRENT CONNECTION OUTAGES",
            "ConnectionOutages");
        AddMetric(_dashboardMetricGrid, 2, 4, "RUN TIME", "RunTime");
        ConfigureDashboardMetricGrid(simple: false);

        _chart.Dock = DockStyle.Fill;
        _chart.Margin = new Padding(4);
        _chart.BackColor = Color.White;

        var diagnosticsStrip = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.White,
            Margin = new Padding(4, 5, 4, 1),
            Padding = new Padding(12, 4, 12, 4)
        };
        diagnosticsStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 215));
        diagnosticsStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        diagnosticsStrip.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        diagnosticsStrip.Controls.Add(new Label
        {
            Text = "PC → ROUTER + DNS",
            Dock = DockStyle.Fill,
            ForeColor = Color.DimGray,
            Font = new Font("Segoe UI", 8.5F),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _diagnosticsSummary.Text = "Measuring gateway, DNS and IP availability…";
        _diagnosticsSummary.Dock = DockStyle.Fill;
        _diagnosticsSummary.TextAlign = ContentAlignment.MiddleLeft;
        _diagnosticsSummary.AutoEllipsis = true;
        _diagnosticsSummary.Font = new Font("Segoe UI", 8.5F);
        _diagnosticsSummary.Padding = new Padding(2, 0, 4, 0);
        _buttonTips.SetToolTip(
            _diagnosticsSummary,
            "Local network checks show the router gateway address and response time, DNS response time, and local IPv4/IPv6 availability. They do not describe an LTE band path.");
        diagnosticsStrip.Controls.Add(_diagnosticsSummary, 1, 0);

        var chartArea = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        chartArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        chartArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        chartArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        chartArea.Controls.Add(diagnosticsStrip, 0, 0);
        chartArea.Controls.Add(_chart, 0, 1);

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(4, 10, 4, 4)
        };

        _pauseButton.Text = "Pause monitoring";
        _pauseButton.Size = new Size(155, 40);
        _pauseButton.Click += (_, _) =>
        {
            bool pause = !_engine.IsPaused;
            _engine.SetPaused(pause);
            _pauseButton.Text = pause ? "Resume monitoring" : "Pause monitoring";
            RefreshDashboard();
        };

        _speedButton.Text = "Run speed test now";
        _speedButton.Size = new Size(165, 40);
        _speedButton.Click += async (_, _) =>
        {
            if (_speedBusy)
                _speedCancellation?.Cancel();
            else
                await RunSpeedTestAsync(manual: true);
        };

        var resetButton = new Button
        {
            Text = "Reset session",
            Size = new Size(135, 40)
        };
        resetButton.Click += (_, _) =>
        {
            if (MessageBox.Show(
                    "Reset live counters? Existing CSV logs will remain.",
                    "Reset session",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _engine.ResetSession();
                _chart.ClearSamples();
                RefreshDashboard();
            }
        };

        var openLogs = new Button
        {
            Text = "Open log folder",
            Size = new Size(145, 40)
        };
        openLogs.Click += (_, _) => Process.Start("explorer.exe", _logger.LogFolder);

        controls.Controls.Add(_pauseButton);
        controls.Controls.Add(_speedButton);
        controls.Controls.Add(resetButton);
        controls.Controls.Add(openLogs);

        layout.Controls.Add(_dashboardExperienceGrid, 0, 0);
        layout.Controls.Add(BuildDashboardConnectionPanel(), 0, 1);
        layout.Controls.Add(_dashboardMetricGrid, 0, 2);
        layout.Controls.Add(chartArea, 0, 3);
        layout.Controls.Add(controls, 0, 4);

        page.Controls.Add(layout);
        return page;
    }

    private void ConfigureHealthCard()
    {
        ConfigureExperienceCard(_healthCard, "END-TO-END HEALTH (THIS PC)");
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _healthScore.Dock = DockStyle.Fill;
        _healthScore.AutoSize = false;
        _healthScore.AutoEllipsis = false;
        _healthScore.Font = new Font("Segoe UI", 23F, FontStyle.Bold);
        _healthScore.TextAlign = ContentAlignment.MiddleCenter;
        _healthScore.Text = "--";
        _healthSummary.Dock = DockStyle.Fill;
        _healthSummary.AutoEllipsis = true;
        _healthSummary.TextAlign = ContentAlignment.MiddleLeft;
        _healthSummary.Padding = new Padding(8, 0, 2, 0);
        _healthSummary.Text = "Waiting for measurements";
        _buttonTips.SetToolTip(
            _healthCard,
            "End-to-end health as experienced by this PC. It combines PC-to-Internet monitoring, PC-to-router/DNS checks and LTE radio telemetry only when Mobile/LTE is selected. Heavy local traffic can reduce this score.");
        content.Controls.Add(_healthScore, 0, 0);
        content.Controls.Add(_healthSummary, 1, 0);
        _healthCard.Controls.Add(content);
    }

    private void ConfigureSmartCard()
    {
        ConfigureExperienceCard(_smartCard, "ROUTER LTE RECOMMENDATION");
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _smartApplyButton.Text = "Apply safely";
        _smartApplyButton.Size = new Size(112, 30);
        _smartApplyButton.Enabled = false;
        _smartApplyButton.Click += async (_, _) =>
        {
            if (_smartCandidate is not null)
                await ApplyRecommendationAsync(_smartCandidate, _smartApplyButton,
                    "Smart LTE recommendation");
        };
        _smartTestButton.Text = "Test current";
        _smartTestButton.Size = new Size(105, 30);
        _smartTestButton.Click += async (_, _) =>
            await RunSpeedTestAsync(manual: true, automaticReason: "smart comparison");
        actions.Controls.Add(_smartApplyButton);
        actions.Controls.Add(_smartTestButton);
        _smartRecommendation.Dock = DockStyle.Fill;
        _smartRecommendation.AutoEllipsis = true;
        _smartRecommendation.TextAlign = ContentAlignment.MiddleLeft;
        _smartRecommendation.Text = "Gathering time-of-day evidence";
        _smartCard.Controls.Add(_smartRecommendation);
        _smartCard.Controls.Add(actions);
    }

    private void ConfigureUpdatesCard()
    {
        ConfigureExperienceCard(_updatesCard, "UPDATES");
        _updateButton.Text = "Check for updates";
        _updateButton.Dock = DockStyle.Bottom;
        _updateButton.Height = 30;
        _updateButton.Click += async (_, _) =>
        {
            if (_availableUpdate is not null)
            {
                await InstallAvailableUpdateAsync(_availableUpdate);
                return;
            }
            await CheckForUpdatesAsync(interactive: true);
        };
        _updateStatus.Dock = DockStyle.Fill;
        _updateStatus.AutoEllipsis = true;
        _updateStatus.TextAlign = ContentAlignment.MiddleLeft;
        _updatesCard.Controls.Add(_updateStatus);
        _updatesCard.Controls.Add(_updateButton);
        RefreshUpdateStatus();
    }

    private static void ConfigureExperienceCard(Panel card, string heading)
    {
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(5);
        card.BackColor = Color.White;
        card.BorderStyle = BorderStyle.FixedSingle;
        card.Padding = new Padding(12, 31, 12, 7);
        var title = new Label
        {
            Text = heading,
            Height = 25,
            Location = new Point(12, 4),
            AutoSize = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false,
            ForeColor = Color.DimGray,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
        };
        card.Controls.Add(title);
        card.Layout += (_, _) =>
            title.Width = Math.Max(0, card.ClientSize.Width - 24);
    }

    private Control BuildDashboardConnectionPanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Margin = new Padding(4, 2, 4, 2),
            BackColor = Color.White
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var statusPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.White,
            Padding = new Padding(8, 6, 8, 6),
            Margin = Padding.Empty
        };
        statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 560));
        statusPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _routerConnectionState.Text = "NOT CONFIGURED";
        _routerConnectionState.Dock = DockStyle.Fill;
        _routerConnectionState.TextAlign = ContentAlignment.MiddleCenter;
        _routerConnectionState.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        _routerConnectionState.MaximumFontSize = 10F;
        _routerConnectionState.ForeColor = Color.White;
        _routerConnectionState.BackColor = Color.DimGray;

        _routerDetails.Text = "Configure a compatible TP-Link router to show live information.";
        _routerDetails.Dock = DockStyle.Fill;
        _routerDetails.TextAlign = ContentAlignment.MiddleLeft;
        _routerDetails.AutoEllipsis = true;
        _routerDetails.Padding = new Padding(14, 0, 0, 0);

        statusPanel.Controls.Add(_routerConnectionState, 0, 0);
        statusPanel.Controls.Add(_routerDetails, 1, 0);

        var selectors = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(8, 0, 0, 0)
        };
        selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        selectors.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        selectors.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var accessLabel = new Label
        {
            Text = "Access",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _connectionViewInput.DropDownStyle = ComboBoxStyle.DropDownList;
        _connectionViewInput.Dock = DockStyle.Fill;
        _connectionViewInput.Items.AddRange(
            ["Mobile / LTE", "ADSL / VDSL", "FTTB / FTTH"]);
        _connectionViewInput.SelectedIndexChanged += (_, _) =>
        {
            if (_connectionViewInput.SelectedIndex < 0)
                return;
            _settings.ConnectionDetailsView = _connectionViewInput.SelectedIndex switch
            {
                0 => "Lte",
                1 => "Dsl",
                _ => "Fiber"
            };
            if (IsHandleCreated)
                _settings.Save();
            if (_settings.ConnectionDetailsView == "Lte" &&
                _settings.DashboardLayout == "DSL / Fiber")
                _settings.DashboardLayout = "LTE Advanced";
            else if (_settings.ConnectionDetailsView != "Lte")
                _settings.DashboardLayout = "DSL / Fiber";
            if (_dashboardLayoutInput.Items.Count > 0)
                _dashboardLayoutInput.SelectedItem = _settings.DashboardLayout;
            ApplyDashboardLayout();
            RefreshRouterDashboard(_routerMonitor.GetSnapshot());
            RefreshDashboard();
        };

        selectors.Controls.Add(accessLabel, 0, 0);
        selectors.Controls.Add(_connectionViewInput, 1, 0);

        _connectionDetailsToggleButton.Text = "Show router / line details";
        _connectionDetailsToggleButton.Dock = DockStyle.Fill;
        _connectionDetailsToggleButton.Margin = new Padding(6, 0, 0, 1);
        _connectionDetailsToggleButton.Click += (_, _) =>
        {
            _connectionDetailsExpanded = !_connectionDetailsExpanded;
            _routerMetricGrid.Visible = _connectionDetailsExpanded;
            if (_dashboardConnectionRowStyle is not null)
                _dashboardConnectionRowStyle.Height =
                    _connectionDetailsExpanded ? 252 : 104;
            _connectionDetailsToggleButton.Text = _connectionDetailsExpanded
                ? "Hide router / line details"
                : "Show router / line details";
        };
        selectors.Controls.Add(_connectionDetailsToggleButton, 2, 0);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 3, 0, 3)
        };
        var configureButton = new Button
        {
            Text = "Configure router",
            Size = new Size(132, 34),
            Margin = new Padding(0)
        };
        configureButton.Click += async (_, _) =>
            await ConfigureRouterAsync(firstRun: false);
        var refreshButton = new Button
        {
            Text = "Reconnect",
            Size = new Size(104, 34),
            Margin = new Padding(4, 0, 0, 0)
        };
        refreshButton.Click += async (_, _) =>
        {
            refreshButton.Enabled = false;
            try
            {
                await _routerMonitor.RestartAsync(_settings, _routerPassword);
            }
            finally
            {
                refreshButton.Enabled = true;
            }
        };
        var rebootButton = new Button
        {
            Text = "Restart router",
            Size = new Size(120, 34),
            Margin = new Padding(4, 0, 0, 0)
        };
        rebootButton.Click += async (_, _) =>
        {
            if (MessageBox.Show(
                    "Restart the TP-Link router now? Internet access will be unavailable for several minutes.",
                    "Restart router",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            rebootButton.Enabled = false;
            try
            {
                await _routerMonitor.RebootRouterAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Restart router",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                rebootButton.Enabled = true;
            }
        };
        actions.Controls.Add(configureButton);
        actions.Controls.Add(refreshButton);
        actions.Controls.Add(rebootButton);
        selectors.Controls.Add(actions, 0, 1);
        selectors.SetColumnSpan(actions, 3);
        statusPanel.Controls.Add(selectors, 2, 0);

        _routerMetricGrid.Dock = DockStyle.Fill;
        _routerMetricGrid.ColumnCount = 8;
        _routerMetricGrid.RowCount = 2;
        _routerMetricGrid.Margin = Padding.Empty;
        for (int column = 0; column < 8; column++)
            _routerMetricGrid.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 12.5F));
        for (int row = 0; row < 2; row++)
            _routerMetricGrid.RowStyles.Add(
                new RowStyle(SizeType.Percent, 50F));

        AddRouterMetric(_routerMetricGrid, 0, 0, "ROUTER STATUS", "Status");
        AddRouterMetric(_routerMetricGrid, 1, 0, "ISP", "Isp");
        AddRouterMetric(_routerMetricGrid, 2, 0, "NETWORK TYPE", "Network");
        AddRouterMetric(_routerMetricGrid, 3, 0, "LTE BAND", "Band");
        AddRouterMetric(_routerMetricGrid, 4, 0, "SIGNAL", "Signal");
        AddRouterMetric(_routerMetricGrid, 5, 0, "RSRP", "Rsrp");
        AddRouterMetric(_routerMetricGrid, 6, 0, "RSRQ", "Rsrq");
        AddRouterMetric(_routerMetricGrid, 7, 0, "SNR", "Snr");
        AddRouterMetric(_routerMetricGrid, 0, 1, "PCI", "Pci");
        AddRouterMetric(_routerMetricGrid, 1, 1, "CELL ID", "Cell");
        AddRouterMetric(_routerMetricGrid, 2, 1, "EARFCN", "Earfcn");
        AddRouterMetric(_routerMetricGrid, 3, 1, "SIM STATUS", "Sim");
        AddRouterMetric(_routerMetricGrid, 4, 1, "DATA USED", "Data");
        AddRouterMetric(_routerMetricGrid, 5, 1, "ROUTER UPLOAD", "RouterUpload");
        AddRouterMetric(_routerMetricGrid, 6, 1, "ROUTER DOWNLOAD", "RouterDownload");
        AddRouterMetric(_routerMetricGrid, 7, 1, "LAST UPDATE", "Updated");
        _routerMetricGrid.Visible = false;

        layout.Controls.Add(statusPanel, 0, 0);
        layout.Controls.Add(_routerMetricGrid, 0, 1);
        return layout;
    }

    private TabPage BuildLteHistoryTab()
    {
        var page = new TabPage("LTE history")
        {
            BackColor = Color.FromArgb(244, 247, 250),
            Padding = new Padding(12)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));

        var summaryPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(16, 10, 16, 10),
            Margin = new Padding(5)
        };
        var heading = new Label
        {
            Text = "LTE cell and band recommendation",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold)
        };
        _cellSuggestion.Text =
            "Waiting for LTE observations. PCI and CID are used when available.";
        _cellSuggestion.Dock = DockStyle.Fill;
        _cellSuggestion.AutoEllipsis = true;
        _cellSuggestion.TextAlign = ContentAlignment.MiddleLeft;
        _cellSuggestion.ForeColor = Color.DimGray;
        summaryPanel.Controls.Add(_cellSuggestion);
        summaryPanel.Controls.Add(heading);

        _cellHistoryGrid.Dock = DockStyle.Fill;
        _cellHistoryGrid.ReadOnly = true;
        _cellHistoryGrid.AllowUserToAddRows = false;
        _cellHistoryGrid.AllowUserToDeleteRows = false;
        _cellHistoryGrid.AllowUserToResizeRows = false;
        _cellHistoryGrid.MultiSelect = false;
        _cellHistoryGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _cellHistoryGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _cellHistoryGrid.RowHeadersVisible = false;
        _cellHistoryGrid.BackgroundColor = Color.White;
        _cellHistoryGrid.BorderStyle = BorderStyle.Fixed3D;
        AddCellHistoryColumn("Rank", "Rank", 6);
        AddCellHistoryColumn("Band", "Band", 8);
        AddCellHistoryColumn("Earfcn", "EARFCN", 9);
        AddCellHistoryColumn("Pci", "PCI", 7);
        AddCellHistoryColumn("Cid", "CID", 11);
        AddCellHistoryColumn("Score", "RF score", 8);
        AddCellHistoryColumn("TestGrade", "Test / rollback", 12);
        AddCellHistoryColumn("Time", "Seen", 11);
        AddCellHistoryColumn("Ping", "Avg ping", 10);
        AddCellHistoryColumn("Load", "Cell load*", 10);
        AddCellHistoryColumn("Drops", "Drops P/A", 10);
        AddCellHistoryColumn("DropRate", "Drop/h", 10);
        AddCellHistoryColumn("Down", "Down", 11);
        AddCellHistoryColumn("Up", "Up", 10);
        AddCellHistoryColumn("Confidence", "Confidence", 11);
        _cellHistoryGrid.ColumnHeaderMouseClick += (_, args) =>
            SortCellHistoryByColumn(args.ColumnIndex);

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(5, 10, 5, 4)
        };
        var applyButton = new Button
        {
            Text = "Apply selected",
            Size = new Size(175, 40)
        };
        applyButton.Click += async (_, _) =>
            await ApplySelectedCellLockAsync(applyButton);

        var automaticButton = new Button
        {
            Text = "Restore automatic",
            Size = new Size(205, 40)
        };
        automaticButton.Click += async (_, _) =>
            await RestoreAutomaticCellSelectionAsync(automaticButton);

        _experimentButton.Text = "Run controlled";
        _experimentButton.Size = new Size(205, 40);
        _experimentButton.Click += async (_, _) =>
        {
            if (_experimentCancellation is not null)
            {
                _experimentCancellation.Cancel();
                _speedCancellation?.Cancel();
            }
            else
                await RunCellExperimentAsync();
        };

        _bandDiscoveryButton.Text = "Scan bands & cells";
        _bandDiscoveryButton.UseMnemonic = false;
        _bandDiscoveryButton.Size = new Size(220, 40);
        _bandDiscoveryButton.Click += async (_, _) =>
        {
            if (_bandDiscoveryCancellation is not null)
            {
                _bandDiscoveryCancellation.Cancel();
                return;
            }

            _bandDiscoveryTask = RunBandCellDiscoveryAsync();
            try
            {
                await _bandDiscoveryTask;
            }
            finally
            {
                _bandDiscoveryTask = null;
            }
        };

        var copyButton = new Button
        {
            Text = "Copy selected lock",
            Size = new Size(205, 40)
        };
        copyButton.Click += (_, _) => CopySelectedCellLock();

        var deleteButton = new Button
        {
            Text = "Delete selected",
            Size = new Size(195, 40)
        };
        deleteButton.Click += (_, _) => DeleteSelectedHistoryProfile();

        var clearButton = new Button
        {
            Text = "Clear LTE history",
            Size = new Size(150, 40)
        };
        clearButton.Click += (_, _) =>
        {
            if (MessageBox.Show(
                    "Permanently clear the locally stored LTE cell history?",
                    "Clear LTE history",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            _cellHistory.Clear();
            RefreshCellHistory(force: true);
        };

        _buttonTips.SetToolTip(
            applyButton,
            "Apply the selected band profile and serving-cell identity as a guarded router lock.");
        _buttonTips.SetToolTip(
            _bandDiscoveryButton,
            "Automatically test every verified LTE band, collect real EARFCN/PCI/CID identities, add lock-ready candidates to LTE History, and restore the previous router state.");
        _buttonTips.SetToolTip(
            _experimentButton,
            "Measure candidate profiles in sequence with controlled locks, stability checks, and speed tests.");
        _buttonTips.SetToolTip(
            automaticButton,
            "Remove the NetPulse lock and return cell and band selection to the router's automatic mode.");
        _buttonTips.SetToolTip(
            copyButton,
            "Copy the selected profile's complete band, EARFCN, PCI, and CID identity.");
        _buttonTips.SetToolTip(
            deleteButton,
            "Delete only the selected band/cell profile from LTE History. Other profiles remain untouched.");
        _buttonTips.SetToolTip(
            clearButton,
            "Permanently delete every locally stored LTE History profile.");

        _cellAutoStatus.Text =
            "Adaptive auto is off • 30-minute dwell • daily limit • 90-second rollback validation";
        _cellAutoStatus.AutoSize = true;
        _cellAutoStatus.ForeColor = Color.DimGray;
        _cellAutoStatus.Margin = new Padding(14, 12, 0, 0);
        controls.Controls.Add(applyButton);
        controls.Controls.Add(_bandDiscoveryButton);
        controls.Controls.Add(_experimentButton);
        controls.Controls.Add(automaticButton);
        controls.Controls.Add(copyButton);
        controls.Controls.Add(deleteButton);
        controls.Controls.Add(clearButton);
        _experimentStatus.AutoSize = true;
        _experimentStatus.ForeColor = Color.DimGray;
        _experimentStatus.Margin = new Padding(14, 12, 0, 0);
        _experimentStatus.Text = "Experiment mode is idle";
        controls.Controls.Add(_experimentStatus);
        _bandDiscoveryStatus.AutoSize = true;
        _bandDiscoveryStatus.ForeColor = Color.DimGray;
        _bandDiscoveryStatus.Margin = new Padding(14, 12, 0, 0);
        _bandDiscoveryStatus.Text = "Automatic discovery is idle";
        controls.Controls.Add(_bandDiscoveryStatus);
        controls.Controls.Add(_cellAutoStatus);

        _bandDiscoveryProgress.Size = new Size(220, 18);
        _bandDiscoveryProgress.Style = ProgressBarStyle.Marquee;
        _bandDiscoveryProgress.MarqueeAnimationSpeed = 28;
        _bandDiscoveryProgress.Visible = false;
        _bandDiscoveryProgress.Margin = new Padding(14, 20, 0, 0);
        controls.Controls.Add(_bandDiscoveryProgress);

        _lteProfileMutationControls.AddRange(
            [applyButton, _experimentButton, automaticButton, copyButton,
             deleteButton, clearButton]);

        layout.Controls.Add(summaryPanel, 0, 0);
        layout.Controls.Add(_cellHistoryGrid, 0, 1);
        layout.Controls.Add(controls, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private void AddCellHistoryColumn(
        string name,
        string header,
        float fillWeight)
    {
        var column = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            FillWeight = fillWeight,
            SortMode = DataGridViewColumnSortMode.Programmatic
        };
        column.HeaderCell.ToolTipText =
            InterfaceHelp.ColumnDescription(name, header);
        _cellHistoryGrid.Columns.Add(column);
    }

    private TabPage BuildManualCellLockTab()
    {
        var page = new TabPage("Cell Lock")
        {
            BackColor = Color.FromArgb(244, 247, 250),
            Padding = new Padding(12)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));

        var explanation = new Label
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(18, 12, 18, 12),
            Text = "Manual TP-Link LTE Band / Cell Lock\r\nBand Lock and Cell Lock work independently. Enter one PCell band; " +
                   "CID, EARFCN and PCI are required. Saving adds the profile " +
                   "to LTE history without inventing measurements. Applying always asks " +
                   "for confirmation and keeps automatic rollback protection.",
            Font = new Font("Segoe UI", 10F),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            BackColor = Color.White,
            Padding = new Padding(22, 16, 22, 16),
            Margin = new Padding(5)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        for (int row = 0; row < 6; row++)
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 16.667F));
        _observedCellLockInput.DropDownStyle = ComboBoxStyle.DropDownList;
        _observedCellLockInput.SelectedIndexChanged += (_, _) =>
            UseSelectedObservedCellLockProfile();
        _manualBandsInput.CueText = "PCell band, e.g. B3";
        _manualEarfcnInput.CueText = "Primary EARFCN";
        _manualPciInput.CueText = "0-512";
        _manualCidInput.CueText = "Required decimal or hex CID (e.g. ABCDE)";
        AddManualLockField(fields, 0, "Previously observed set", _observedCellLockInput);
        AddManualLockField(fields, 1, "PCell band", _manualBandsInput);
        AddManualLockField(fields, 2, "Primary EARFCN", _manualEarfcnInput);
        AddManualLockField(fields, 3, "PCI", _manualPciInput);
        AddManualLockField(fields, 4, "CID", _manualCidInput);
        _manualLockStatus.Dock = DockStyle.Fill;
        _manualLockStatus.TextAlign = ContentAlignment.MiddleLeft;
        _manualLockStatus.ForeColor = Color.DimGray;
        _manualLockStatus.Text =
            "Profiles with the same PCell and EARFCN keep known PCI/CID across carrier aggregation changes.";
        fields.Controls.Add(_manualLockStatus, 0, 5);
        fields.SetColumnSpan(_manualLockStatus, 2);

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(5, 12, 5, 5)
        };
        var save = new Button { Text = "Save profile to history", Size = new Size(195, 40) };
        save.Click += (_, _) => SaveManualCellProfile();
        var bandOnly = new Button { Text = "Apply band lock", Size = new Size(155, 40) };
        bandOnly.Click += async (_, _) => await ApplyManualBandLockAsync(bandOnly);
        var scanOne = new Button { Text = "Scan this band", Size = new Size(150, 40) };
        scanOne.Click += async (_, _) => await ScanManualBandAsync(scanOne);
        var apply = new Button { Text = "Apply PCell lock", Size = new Size(160, 40) };
        apply.Click += async (_, _) => await ApplyManualCellLockAsync(apply);
        var restore = new Button { Text = "Restore automatic selection", Size = new Size(210, 40) };
        restore.Click += async (_, _) => await RestoreAutomaticCellSelectionAsync(restore);
        controls.Controls.Add(save);
        controls.Controls.Add(bandOnly);
        controls.Controls.Add(scanOne);
        controls.Controls.Add(apply);
        controls.Controls.Add(restore);
        _lteProfileMutationControls.AddRange([save, bandOnly, scanOne, apply, restore]);

        layout.Controls.Add(explanation, 0, 0);
        layout.Controls.Add(fields, 0, 1);
        layout.Controls.Add(controls, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private static void AddManualLockField(
        TableLayoutPanel layout,
        int row,
        string caption,
        Control input)
    {
        layout.Controls.Add(new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold)
        }, 0, row);
        input.Dock = DockStyle.Fill;
        input.Margin = new Padding(5, 8, 5, 8);
        layout.Controls.Add(input, 1, row);
    }

    private TabPage BuildSmsTab()
    {
        var page = new TabPage("SMS")
        {
            BackColor = Color.FromArgb(244, 247, 250),
            Padding = new Padding(12)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));

        _smsStatus.Dock = DockStyle.Fill;
        _smsStatus.BackColor = Color.White;
        _smsStatus.Padding = new Padding(14, 0, 14, 0);
        _smsStatus.TextAlign = ContentAlignment.MiddleLeft;
        _smsStatus.Text = "Connect TP-Link monitoring to read and send SIM messages.";
        _smsStatus.ForeColor = Color.DimGray;

        var smsFilters = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Color.White,
            Padding = new Padding(10, 6, 10, 6)
        };
        smsFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
        smsFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        smsFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        smsFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        smsFilters.Controls.Add(new Label
        {
            Text = "View",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _smsViewInput.Dock = DockStyle.Fill;
        _smsViewInput.DropDownStyle = ComboBoxStyle.DropDownList;
        _smsViewInput.Items.AddRange(["Conversations", "Drafts", "Timeline"]);
        _smsViewInput.SelectedIndex = SmsConversationsView;
        _smsViewInput.SelectedIndexChanged += (_, _) => ChangeSmsView();
        smsFilters.Controls.Add(_smsViewInput, 1, 0);
        smsFilters.Controls.Add(new Label
        {
            Text = "Search",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 2, 0);
        _smsSearchInput.Dock = DockStyle.Fill;
        _smsSearchInput.PlaceholderText = "Contact, number or message text";
        _smsSearchInput.TextChanged += (_, _) => PopulateSmsGrid();
        smsFilters.Controls.Add(_smsSearchInput, 3, 0);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(5)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));

        _smsGrid.Dock = DockStyle.Fill;
        _smsGrid.ReadOnly = true;
        _smsGrid.AllowUserToAddRows = false;
        _smsGrid.AllowUserToDeleteRows = false;
        _smsGrid.AllowUserToResizeRows = false;
        _smsGrid.MultiSelect = false;
        _smsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _smsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _smsGrid.RowHeadersVisible = false;
        _smsGrid.BackgroundColor = Color.White;
        _smsGrid.Columns.Add("SmsState", "");
        _smsGrid.Columns.Add("SmsFrom", "Conversation");
        _smsGrid.Columns.Add("SmsReceived", "Latest");
        _smsGrid.Columns.Add("SmsPreview", "Preview");
        _smsGrid.Columns[0].FillWeight = 10;
        _smsGrid.Columns[1].FillWeight = 31;
        _smsGrid.Columns[2].FillWeight = 25;
        _smsGrid.Columns[3].FillWeight = 34;
        _smsGrid.SelectionChanged += async (_, _) =>
        {
            if (_populatingSmsGrid || _smsGrid.SelectedRows.Count == 0)
                return;
            if (_smsGrid.SelectedRows[0].Tag is SmsConversationRow conversation)
            {
                string normalized = SmsConversationBuilder.NormalizeAddress(
                    conversation.Address,
                    _settings.CountryCode);
                if (!string.Equals(
                        normalized,
                        _activeSmsConversationAddress,
                        StringComparison.Ordinal) ||
                    _smsThreadPanel.Controls.Count == 0)
                    ShowSmsConversation(conversation.Address);
                return;
            }
            await OpenSelectedSmsAsync(markRead: false);
        };
        _smsGrid.CellClick += async (_, args) =>
        {
            if (_populatingSmsGrid || args.RowIndex < 0)
                return;
            if (_smsGrid.Rows[args.RowIndex].Tag is SmsConversationRow conversation)
                ShowSmsConversation(conversation.Address);
            else
                await OpenSelectedSmsAsync(markRead: true);
        };

        var reader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            BackColor = Color.White,
            Padding = new Padding(16, 12, 16, 12)
        };
        reader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        reader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        reader.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        reader.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        reader.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        reader.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        reader.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        reader.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        AddSmsReadLabel(reader, 0, "Contact", _smsSender);
        AddSmsReadLabel(reader, 1, "Time", _smsReceived);

        var messageActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 4),
            Margin = Padding.Empty
        };
        _smsReadButton.Text = "Mark read";
        _smsReadButton.Size = new Size(100, 34);
        _smsReadButton.Enabled = false;
        _smsReadButton.Click += async (_, _) =>
            await SetSelectedSmsUnreadAsync(unread: false, automatic: false);
        _smsUnreadButton.Text = "Mark unread";
        _smsUnreadButton.Size = new Size(120, 34);
        _smsUnreadButton.Enabled = false;
        _smsUnreadButton.Click += async (_, _) =>
            await SetSelectedSmsUnreadAsync(unread: true, automatic: false);
        _smsDeleteButton.Text = "Delete...";
        _smsDeleteButton.Size = new Size(100, 34);
        _smsDeleteButton.ForeColor = Color.Firebrick;
        _smsDeleteButton.Enabled = false;
        _smsDeleteButton.Click += async (_, _) => await DeleteSelectedSmsAsync();
        messageActions.Controls.Add(_smsReadButton);
        messageActions.Controls.Add(_smsUnreadButton);
        messageActions.Controls.Add(_smsDeleteButton);
        reader.Controls.Add(messageActions, 0, 2);
        reader.SetColumnSpan(messageActions, 2);

        _smsThreadPanel.Dock = DockStyle.Fill;
        _smsThreadPanel.AutoScroll = true;
        _smsThreadPanel.FlowDirection = FlowDirection.TopDown;
        _smsThreadPanel.WrapContents = false;
        _smsThreadPanel.Padding = new Padding(8);
        _smsThreadPanel.Margin = new Padding(0, 4, 0, 6);
        _smsThreadPanel.BackColor = Color.FromArgb(244, 247, 250);
        _smsThreadPanel.Resize += (_, _) => ResizeSmsConversationRows();
        reader.Controls.Add(_smsThreadPanel, 0, 3);
        reader.SetColumnSpan(_smsThreadPanel, 2);
        reader.Controls.Add(new Label
        {
            Text = "To",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        }, 0, 4);
        _smsRecipientInput.Dock = DockStyle.Fill;
        _smsRecipientInput.PlaceholderText = "+30...";
        _smsRecipientInput.Margin = new Padding(3, 6, 3, 6);
        _smsRecipientInput.TextChanged += (_, _) => TryJoinTypedSmsConversation();
        reader.Controls.Add(_smsRecipientInput, 1, 4);
        _smsComposeInput.Dock = DockStyle.Fill;
        _smsComposeInput.Multiline = true;
        _smsComposeInput.ScrollBars = ScrollBars.Vertical;
        _smsComposeInput.MaxLength = 765;
        _smsComposeInput.PlaceholderText = "Write a message";
        _smsComposeInput.TextChanged += (_, _) => RefreshSmsLength();
        var composer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        composer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        composer.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        var composeEditor = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 0, 0, 24)
        };
        _smsLength.Dock = DockStyle.Bottom;
        _smsLength.Height = 24;
        _smsLength.TextAlign = ContentAlignment.MiddleRight;
        _smsLength.ForeColor = Color.DimGray;
        composeEditor.Controls.Add(_smsComposeInput);
        composeEditor.Controls.Add(_smsLength);
        composer.Controls.Add(composeEditor, 0, 0);
        var composeActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty
        };
        _smsSendButton.Text = "Send SMS";
        _smsSendButton.Size = new Size(120, 34);
        _smsSendButton.Click += async (_, _) => await SendSmsAsync();
        composeActions.Controls.Add(_smsSendButton);
        composer.Controls.Add(composeActions, 0, 1);
        reader.Controls.Add(composer, 0, 5);
        reader.SetColumnSpan(composer, 2);
        RefreshSmsLength();

        content.Controls.Add(_smsGrid, 0, 0);
        content.Controls.Add(reader, 1, 0);

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(5, 10, 5, 4)
        };
        _smsRefreshButton.Text = "Refresh messages";
        _smsRefreshButton.Size = new Size(145, 40);
        _smsRefreshButton.Click += async (_, _) =>
            await RefreshSmsTimelineAsync(showErrors: true);
        var newButton = new Button { Text = "New SMS", Size = new Size(125, 40) };
        newButton.Click += (_, _) => StartNewSms();
        _smsDraftButton.Text = "Save draft";
        _smsDraftButton.Size = new Size(115, 40);
        _smsDraftButton.Click += async (_, _) => await SaveSmsDraftAsync();
        _smsContactButton.Text = "Save contact...";
        _smsContactButton.Size = new Size(135, 40);
        _smsContactButton.Click += (_, _) => SaveSmsContact();
        controls.Controls.Add(_smsRefreshButton);
        controls.Controls.Add(newButton);
        controls.Controls.Add(_smsDraftButton);
        controls.Controls.Add(_smsContactButton);

        layout.Controls.Add(_smsStatus, 0, 0);
        layout.Controls.Add(smsFilters, 0, 1);
        layout.Controls.Add(content, 0, 2);
        layout.Controls.Add(controls, 0, 3);
        page.Controls.Add(layout);
        return page;
    }

    private static void AddSmsReadLabel(
        TableLayoutPanel layout,
        int row,
        string caption,
        Label value)
    {
        layout.Controls.Add(new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        }, 0, row);
        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.MiddleLeft;
        value.AutoEllipsis = true;
        layout.Controls.Add(value, 1, row);
    }

    private TabPage BuildEventsTab()
    {
        var page = new TabPage("Timeline")
        {
            BackColor = Color.FromArgb(244, 247, 250),
            Padding = new Padding(12)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var filters = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Color.White,
            Padding = new Padding(10, 7, 10, 7)
        };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filters.Controls.Add(new Label
        {
            Text = "Category",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _eventFilterInput.Dock = DockStyle.Fill;
        _eventFilterInput.DropDownStyle = ComboBoxStyle.DropDownList;
        _eventFilterInput.Items.AddRange(
            ["All", "Connectivity", "LTE", "Speed tests", "SMS", "System"]);
        _eventFilterInput.SelectedIndex = 0;
        _eventFilterInput.SelectedIndexChanged += (_, _) => RefreshEventGrid();
        filters.Controls.Add(_eventFilterInput, 1, 0);
        filters.Controls.Add(new Label
        {
            Text = "Search",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 2, 0);
        _eventSearchInput.Dock = DockStyle.Fill;
        _eventSearchInput.PlaceholderText = "Filter the connection timeline";
        _eventSearchInput.TextChanged += (_, _) => RefreshEventGrid();
        filters.Controls.Add(_eventSearchInput, 3, 0);

        _eventsGrid.Dock = DockStyle.Fill;
        _eventsGrid.ReadOnly = true;
        _eventsGrid.AllowUserToAddRows = false;
        _eventsGrid.AllowUserToDeleteRows = false;
        _eventsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _eventsGrid.RowHeadersVisible = false;
        _eventsGrid.BackgroundColor = Color.White;
        _eventsGrid.Columns.Add("Timestamp", "Timestamp");
        _eventsGrid.Columns.Add("Kind", "Kind");
        _eventsGrid.Columns.Add("Message", "Message");
        _eventsGrid.Columns[0].FillWeight = 25;
        _eventsGrid.Columns[1].FillWeight = 15;
        _eventsGrid.Columns[2].FillWeight = 60;

        layout.Controls.Add(filters, 0, 0);
        layout.Controls.Add(_eventsGrid, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildConnectedDevicesTab()
    {
        var page = new TabPage("Devices")
        {
            BackColor = Color.FromArgb(244, 247, 250),
            Padding = new Padding(12)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(14, 8, 14, 8),
            Margin = new Padding(5),
            BackColor = Color.White
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        header.Controls.Add(new Label
        {
            Text = "CONNECTED DEVICES",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _connectedDevicesStatus.Text = "Open this tab to read the router's live device list.";
        _connectedDevicesStatus.Dock = DockStyle.Fill;
        _connectedDevicesStatus.AutoEllipsis = true;
        _connectedDevicesStatus.ForeColor = Color.DimGray;
        _connectedDevicesStatus.TextAlign = ContentAlignment.MiddleLeft;
        header.Controls.Add(_connectedDevicesStatus, 1, 0);
        var refresh = new Button
        {
            Text = "Refresh",
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 4, 0, 4)
        };
        refresh.Click += async (_, _) =>
            await RefreshConnectedDevicesAsync(showErrors: true);
        _buttonTips.SetToolTip(refresh,
            "Read the current active-device list directly from the TP-Link router.");
        header.Controls.Add(refresh, 2, 0);

        _connectedDevicesGrid.Dock = DockStyle.Fill;
        _connectedDevicesGrid.Margin = new Padding(5);
        _connectedDevicesGrid.ReadOnly = true;
        _connectedDevicesGrid.AllowUserToAddRows = false;
        _connectedDevicesGrid.AllowUserToDeleteRows = false;
        _connectedDevicesGrid.AllowUserToResizeRows = false;
        _connectedDevicesGrid.AutoGenerateColumns = false;
        _connectedDevicesGrid.MultiSelect = false;
        _connectedDevicesGrid.RowHeadersVisible = false;
        _connectedDevicesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _connectedDevicesGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _connectedDevicesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "DeviceName", HeaderText = "Device", DataPropertyName = "Name",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 34
        });
        _connectedDevicesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "DeviceIp", HeaderText = "IP address", DataPropertyName = "IpAddress",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 22
        });
        _connectedDevicesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "DeviceMac", HeaderText = "MAC address", DataPropertyName = "MacAddress",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 26
        });
        _connectedDevicesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "DeviceConnection", HeaderText = "Connection", DataPropertyName = "ConnectionType",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 18
        });

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(_connectedDevicesGrid, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private async Task RefreshConnectedDevicesAsync(bool showErrors)
    {
        if (_connectedDevicesBusy)
            return;
        _connectedDevicesBusy = true;
        _connectedDevicesStatus.Text = "Reading active devices from the router…";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            IReadOnlyList<RouterConnectedDevice> devices =
                await _routerMonitor.ReadConnectedDevicesAsync(timeout.Token);
            _connectedDevicesGrid.DataSource = devices.ToList();
            _connectedDevicesStatus.Text = devices.Count == 0
                ? "No active client devices were reported by the router."
                : $"{devices.Count} active device{(devices.Count == 1 ? "" : "s")} · live router data · not stored";
            _nextConnectedDevicesRefreshUtc = DateTime.UtcNow.AddSeconds(10);
        }
        catch (Exception ex)
        {
            _connectedDevicesStatus.Text = ex.Message;
            _nextConnectedDevicesRefreshUtc = DateTime.UtcNow.AddSeconds(15);
            if (showErrors)
                MessageBox.Show(ex.Message, "Connected devices",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _connectedDevicesBusy = false;
        }
    }

    private TabPage BuildDiagnosticsTab()
    {
        var page = new TabPage("Diagnostics")
        {
            BackColor = Color.FromArgb(244, 247, 250),
            Padding = new Padding(20)
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            BackColor = Color.White,
            Padding = new Padding(15)
        };

        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        for (int row = 0; row < 5; row++)
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        AddDiagnosticRow(grid, 0, "Default gateway", _gatewayValue);
        AddDiagnosticRow(grid, 1, "Gateway latency", _gatewayPingValue);
        AddDiagnosticRow(grid, 2, "DNS lookup latency", _dnsValue);
        AddDiagnosticRow(grid, 3, "IPv4", _ipv4Value);
        AddDiagnosticRow(grid, 4, "IPv6", _ipv6Value);

        var runButton = new Button
        {
            Text = "Run diagnostics",
            Size = new Size(180, 40)
        };
        var exportButton = new Button
        {
            Text = "Export full ISP evidence...",
            Size = new Size(245, 40)
        };
        var troubleshootButton = new Button
        {
            Text = "Why is my connection slow?",
            Size = new Size(270, 40)
        };
        troubleshootButton.Click += async (_, _) =>
        {
            troubleshootButton.Enabled = false;
            try
            {
                await RefreshDiagnosticsAsync(showErrors: true);
                TroubleshootingAssessment assessment = TroubleshootingAdvisor.Analyze(
                    _engine.GetSnapshot(),
                    _routerMonitor.GetSnapshot(),
                    _lastDiagnosticResult,
                    _lastSpeedResult);
                ShowTroubleshootingAssessment(assessment);
            }
            finally
            {
                troubleshootButton.Enabled = true;
            }
        };

        runButton.Click += async (_, _) =>
        {
            runButton.Enabled = false;
            runButton.Text = "Running…";

            try
            {
                await RefreshDiagnosticsAsync(showErrors: true);
            }
            finally
            {
                runButton.Enabled = true;
                runButton.Text = "Run diagnostics";
            }
        };

        exportButton.Click += async (_, _) =>
        {
            if (MessageBox.Show(
                    "Create a full technical ISP evidence ZIP?\r\n\r\n" +
                    "It includes public/local IP addresses, the numeric gateway, full LTE " +
                    "band/PCell/EARFCN/PCI/CID identifiers, signal measurements, events and " +
                    "speed tests. It excludes router credentials, tokens, SMS and contacts.",
                    "Full ISP evidence",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            runButton.Enabled = false;
            exportButton.Enabled = false;
            exportButton.Text = "Creating technical ZIP...";
            try
            {
                if (string.IsNullOrWhiteSpace(_lastPublicIp))
                {
                    using var ipTimeout = new CancellationTokenSource(
                        TimeSpan.FromSeconds(8));
                    _lastPublicIp = await PublicIpProbe.ReadAsync(ipTimeout.Token) ?? "";
                }
                MonitorSnapshot monitor = _engine.GetSnapshot();
                RouterTelemetry router = _routerMonitor.GetSnapshot();
                string accessTechnology = GetAccessTechnologyLabel();
                IReadOnlyList<LteCellRecommendation> lteHistory =
                    _cellHistory.GetHistoryRecommendations();
                string path = await Task.Run(() => IspEvidenceExporter.Export(
                    _logger,
                    accessTechnology,
                    monitor,
                    router,
                    _lastDiagnosticResult,
                    _lastPublicIp,
                    lteHistory,
                    _clock));
                MessageBox.Show(
                    "The full technical ISP evidence ZIP was created.\r\n\r\n" +
                    path +
                    "\r\n\r\nIP addresses and full serving-cell identifiers are included. " +
                    "Credentials, tokens, SMS content and contacts are excluded.",
                    "ISP evidence export",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "The evidence ZIP could not be created.\r\n\r\n" + ex.Message,
                    "ISP evidence export",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                runButton.Enabled = true;
                exportButton.Enabled = true;
                exportButton.Text = "Export full ISP evidence...";
            }
        };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0)
        };
        actions.Controls.Add(runButton);
        actions.Controls.Add(troubleshootButton);
        actions.Controls.Add(exportButton);
        _troubleshootingSummary.Dock = DockStyle.Fill;
        _troubleshootingSummary.BackColor = Color.FromArgb(242, 247, 251);
        _troubleshootingSummary.Padding = new Padding(14);
        _troubleshootingSummary.TextAlign = ContentAlignment.MiddleCenter;
        _troubleshootingSummary.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        _troubleshootingSummary.Text =
            "Guided troubleshooting combines the local gateway, DNS, Internet ping, " +
            "LTE quality, disconnections and the latest comparable speed test.";
        grid.Controls.Add(_troubleshootingSummary, 0, 5);
        grid.SetColumnSpan(_troubleshootingSummary, 2);
        grid.Controls.Add(actions, 0, 6);
        grid.SetColumnSpan(actions, 2);

        page.Controls.Add(grid);
        return page;
    }

    private TabPage BuildSettingsTab()
    {
        var page = new TabPage("Settings")
        {
            BackColor = Color.FromArgb(244, 247, 250),
            Padding = new Padding(12)
        };

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        TableLayoutPanel monitoring = CreateSettingsSection(
            "Monitoring, dashboard and appearance", 9);
        TableLayoutPanel integration = CreateSettingsSection(
            "Windows, regional time, TP-Link and mobile", 11);

        _pingIntervalInput.Minimum = 1;
        _pingIntervalInput.Maximum = 300;
        _failureInput.Minimum = 1;
        _failureInput.Maximum = 20;
        _speedIntervalInput.Minimum = 0;
        _speedIntervalInput.Maximum = 1440;
        AddSettingRow(monitoring, 0, "Ping target", _targetInput);
        AddSettingRow(monitoring, 1, "Ping interval (seconds)", _pingIntervalInput);
        AddSettingRow(monitoring, 2, "Failures required for outage", _failureInput);
        AddSettingRow(monitoring, 3,
            "Periodic test interval (minutes; 0 = off)",
            _speedIntervalInput);
        var sampleSize = new Label
        {
            Text = "20 MB download / 5 MB upload",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(35, 83, 120)
        };
        AddSettingRow(monitoring, 4, "Every speed-test sample", sampleSize);
        _themeInput.DropDownStyle = ComboBoxStyle.DropDownList;
        _themeInput.Items.AddRange(["System", "Light", "Dark"]);
        _themeInput.SelectedIndexChanged += (_, _) =>
        {
            if (_themeInput.SelectedItem is string value &&
                Enum.TryParse(value, out NetPulseTheme theme))
            {
                AppThemeManager.Apply(this, theme);
                RefreshThemeDependentContent();
            }
        };
        AddSettingRow(monitoring, 5, "Theme", _themeInput);
        _dashboardLayoutInput.DropDownStyle = ComboBoxStyle.DropDownList;
        _dashboardLayoutInput.Items.AddRange(
            ["LTE Simple", "LTE Advanced", "DSL / Fiber", "ISP troubleshooting"]);
        _dashboardLayoutInput.SelectedIndexChanged += (_, _) => ApplyDashboardLayout();
        AddSettingRow(monitoring, 6, "Dashboard layout", _dashboardLayoutInput);
        _healthSummaryInput.Text = "Show connection health score";
        AddSettingRow(monitoring, 7, "Health summary", _healthSummaryInput);
        _smartRecommendationInput.Text = "Show measured LTE recommendation";
        AddSettingRow(monitoring, 8, "Smart recommendation", _smartRecommendationInput);

        AddSettingRow(integration, 0, "Start with Windows", _startupInput);
        AddSettingRow(integration, 1, "Minimize to system tray", _trayInput);
        _regionalSetupButton.Click += (_, _) =>
            ConfigureRegionalSettings(firstRun: false);
        AddSettingRow(integration, 2, "Country and official time", _regionalSetupButton);
        AddSettingRow(integration, 3, "TP-Link router live monitoring", _routerEnabledInput);
        AddSettingRow(integration, 4, "TP-Link router address", _routerAddressInput);

        _routerSetupButton.Text = "Configure protected password...";
        _routerSetupButton.Click += async (_, _) =>
            await ConfigureRouterAsync(firstRun: false);
        AddSettingRow(integration, 5, "TP-Link local credentials", _routerSetupButton);
        _automaticCellLockInput.Text =
            "Allow guarded time-aware cell + band optimization";
        _automaticCellLockInput.AutoSize = false;
        _automaticCellLockInput.AutoEllipsis = true;
        AddSettingRow(integration, 6, "TP-Link LTE optimization", _automaticCellLockInput);
        _updateCheckInput.Text = "Check GitHub for a newer release once per day";
        _updateCheckInput.AutoEllipsis = true;
        AddSettingRow(integration, 7, "Update checks", _updateCheckInput);
        _experimentMinutesInput.Minimum = 2;
        _experimentMinutesInput.Maximum = 30;
        AddSettingRow(integration, 8, "Experiment minutes per profile", _experimentMinutesInput);
        var mobileNetworkModeEditor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty
        };
        mobileNetworkModeEditor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        mobileNetworkModeEditor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        mobileNetworkModeEditor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        mobileNetworkModeEditor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _mobileNetworkModeInput.Dock = DockStyle.Fill;
        _mobileNetworkModeInput.DropDownStyle = ComboBoxStyle.DropDownList;
        _mobileNetworkModeInput.DisplayMember = nameof(
            RouterMobileNetworkModeOption.DisplayName);
        _mobileNetworkModeInput.SelectedIndexChanged += (_, _) =>
            UpdateMobileNetworkModeControls();
        _mobileNetworkModeRefreshButton.Text = "Refresh";
        _mobileNetworkModeRefreshButton.Dock = DockStyle.Fill;
        _mobileNetworkModeRefreshButton.Click += async (_, _) =>
            await RefreshMobileNetworkModeAsync(showErrors: true);
        _mobileNetworkModeApplyButton.Text = "Apply";
        _mobileNetworkModeApplyButton.Dock = DockStyle.Fill;
        _mobileNetworkModeApplyButton.Click += async (_, _) =>
            await ApplyMobileNetworkModeAsync();
        mobileNetworkModeEditor.Controls.Add(_mobileNetworkModeInput, 0, 0);
        mobileNetworkModeEditor.Controls.Add(_mobileNetworkModeRefreshButton, 1, 0);
        mobileNetworkModeEditor.Controls.Add(_mobileNetworkModeApplyButton, 2, 0);
        _buttonTips.SetToolTip(
            _mobileNetworkModeInput,
            "Shows only the mobile network modes reported by the connected TP-Link firmware.");
        _buttonTips.SetToolTip(
            _mobileNetworkModeRefreshButton,
            "Read the current mobile network mode and supported choices from the router.");
        _buttonTips.SetToolTip(
            _mobileNetworkModeApplyButton,
            "Apply the selected mode. Mobile service can disconnect briefly while the router registers again.");
        AddSettingRow(integration, 9, "Mobile network mode", mobileNetworkModeEditor);
        _companionSetupButton.Text = "Configure persistent phone pairing...";
        _companionSetupButton.Click += async (_, _) => await ConfigureCompanionAsync();
        AddSettingRow(integration, 10, "Mobile companion", _companionSetupButton);
        ShowMobileNetworkModePlaceholder(
            "Open Settings while TP-Link monitoring is connected");

        var saveButton = new Button
        {
            Text = "Save settings",
            Dock = DockStyle.Fill,
            Height = 42
        };

        saveButton.Click += async (_, _) =>
        {
            saveButton.Enabled = false;
            try
            {
                await SaveSettingsAsync();
            }
            finally
            {
                saveButton.Enabled = true;
            }
        };
        saveButton.Margin = new Padding(5, 8, 5, 2);
        outer.Controls.Add(monitoring, 0, 0);
        outer.Controls.Add(integration, 1, 0);
        outer.Controls.Add(saveButton, 0, 1);
        outer.SetColumnSpan(saveButton, 2);

        page.Controls.Add(outer);
        return page;
    }

    private async Task RefreshMobileNetworkModeAsync(bool showErrors)
    {
        if (_mobileNetworkModeBusy)
            return;

        _mobileNetworkModeBusy = true;
        UpdateMobileNetworkModeControls();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            RouterMobileNetworkModeState state =
                await _routerMonitor.ReadMobileNetworkModeAsync(timeout.Token);
            _mobileNetworkModeState = state;
            _mobileNetworkModeInput.BeginUpdate();
            try
            {
                _mobileNetworkModeInput.Items.Clear();
                foreach (RouterMobileNetworkModeOption mode in state.SupportedModes)
                    _mobileNetworkModeInput.Items.Add(mode);
                int currentIndex = state.SupportedModes
                    .Select((mode, index) => (mode, index))
                    .Where(item => item.mode.Value.Equals(
                        state.CurrentValue, StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.index)
                    .DefaultIfEmpty(-1)
                    .First();
                _mobileNetworkModeInput.SelectedIndex = currentIndex;
            }
            finally
            {
                _mobileNetworkModeInput.EndUpdate();
            }

            _buttonTips.SetToolTip(
                _mobileNetworkModeInput,
                $"{state.Model}: only modes reported by this firmware are listed. " +
                "A 4G router will never be offered a 5G-only setting.");
        }
        catch (Exception ex)
        {
            _mobileNetworkModeState = null;
            ShowMobileNetworkModePlaceholder("Connect router to load modes");
            _buttonTips.SetToolTip(
                _mobileNetworkModeInput,
                FriendlyUiError(ex));
            if (showErrors)
            {
                MessageBox.Show(
                    FriendlyUiError(ex),
                    "Mobile network mode",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _mobileNetworkModeBusy = false;
            UpdateMobileNetworkModeControls();
        }
    }

    private async Task ApplyMobileNetworkModeAsync()
    {
        if (_mobileNetworkModeBusy ||
            _mobileNetworkModeInput.SelectedItem is not
                RouterMobileNetworkModeOption selected ||
            _mobileNetworkModeState is null)
            return;

        RouterMobileNetworkModeOption? current =
            _mobileNetworkModeState.CurrentMode;
        if (current?.Value.Equals(
                selected.Value, StringComparison.OrdinalIgnoreCase) == true)
            return;

        DialogResult answer = MessageBox.Show(
            $"Change the router mobile network mode from " +
            $"{current?.DisplayName ?? "the current mode"} to " +
            $"{selected.DisplayName}?\r\n\r\n" +
            "Mobile data can disconnect briefly while the router registers again.",
            "Change mobile network mode",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
            return;

        _mobileNetworkModeBusy = true;
        UpdateMobileNetworkModeControls();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await _routerMonitor.SetMobileNetworkModeAsync(
                selected.Value, timeout.Token);
            AddLoggedEvent(new MonitorEvent
            {
                Kind = "ROUTER",
                Message = "TP-Link mobile network mode changed to " +
                          selected.DisplayName
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                FriendlyUiError(ex),
                "Mobile network mode",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            _mobileNetworkModeBusy = false;
        }

        await RefreshMobileNetworkModeAsync(showErrors: false);
    }

    private void ShowMobileNetworkModePlaceholder(string text)
    {
        _mobileNetworkModeInput.BeginUpdate();
        try
        {
            _mobileNetworkModeInput.Items.Clear();
            _mobileNetworkModeInput.Items.Add(
                new RouterMobileNetworkModeOption("", text));
            _mobileNetworkModeInput.SelectedIndex = 0;
        }
        finally
        {
            _mobileNetworkModeInput.EndUpdate();
        }
    }

    private void UpdateMobileNetworkModeControls()
    {
        bool hasModes = _mobileNetworkModeState?.SupportedModes.Count > 0;
        _mobileNetworkModeInput.Enabled = !_mobileNetworkModeBusy && hasModes;
        _mobileNetworkModeRefreshButton.Enabled = !_mobileNetworkModeBusy;
        _mobileNetworkModeApplyButton.Enabled =
            !_mobileNetworkModeBusy &&
            _mobileNetworkModeInput.SelectedItem is RouterMobileNetworkModeOption selected &&
            selected.Value.Length > 0 &&
            !_mobileNetworkModeState!.CurrentValue.Equals(
                selected.Value, StringComparison.OrdinalIgnoreCase);
    }

    private static TableLayoutPanel CreateSettingsSection(string heading, int fieldRows)
    {
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = fieldRows + 2,
            BackColor = Color.White,
            Margin = new Padding(5),
            Padding = new Padding(14, 10, 14, 10)
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        for (int row = 0; row < fieldRows; row++)
            section.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / fieldRows));
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));

        var title = new Label
        {
            Text = heading,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(25, 70, 105)
        };
        section.Controls.Add(title, 0, 0);
        section.SetColumnSpan(title, 2);
        return section;
    }

    private void AddMetric(
        TableLayoutPanel grid, int column, int row, string caption, string key)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(5, 2, 5, 2),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(10, 2, 10, 2)
        };

        var captionLabel = new Label
        {
            Text = caption,
            AutoSize = false,
            Dock = DockStyle.None,
            ForeColor = Color.DimGray,
            Font = new Font("Segoe UI", 8.5F),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };

        var valueLabel = new AutoFitLabel
        {
            Text = "",
            Dock = DockStyle.None,
            MaximumFontSize = 28F,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };

        _metrics[key] = valueLabel;
        _metricCards[key] = card;
        card.Controls.Add(captionLabel);
        card.Controls.Add(valueLabel);
        card.Layout += (_, _) =>
            ArrangeMetricCard(card, captionLabel, valueLabel, 0F);
        grid.Controls.Add(card, column, row);
        ArrangeMetricCard(card, captionLabel, valueLabel, 0F);
    }

    private void AddRouterMetric(
        TableLayoutPanel grid, int column, int row, string caption, string key)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(5, 3, 5, 3),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(12, 4, 12, 5)
        };
        var captionLabel = new Label
        {
            Text = caption,
            AutoSize = false,
            Dock = DockStyle.None,
            ForeColor = Color.DimGray,
            Font = new Font("Segoe UI", 8.5F),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };
        var valueLabel = new AutoFitLabel
        {
            Text = "",
            Dock = DockStyle.None,
            MaximumFontSize = 32F,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        _routerMetrics[key] = valueLabel;
        _routerMetricCaptions[key] = captionLabel;
        _routerMetricCards[key] = card;
        card.Controls.Add(captionLabel);
        card.Controls.Add(valueLabel);
        card.Layout += (_, _) =>
            ArrangeMetricCard(card, captionLabel, valueLabel, 0F);
        grid.Controls.Add(card, column, row);
        ArrangeMetricCard(card, captionLabel, valueLabel, 0F);
    }

    private static void ArrangeMetricCard(
        Panel card,
        Control caption,
        Control value,
        float captionFraction)
    {
        int width = Math.Max(0, card.ClientSize.Width - card.Padding.Horizontal);
        int height = Math.Max(0, card.ClientSize.Height - card.Padding.Vertical);
        int measuredCaptionHeight = TextRenderer.MeasureText(
            caption.Text,
            caption.Font,
            new Size(Math.Max(1, width), int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Height;
        int captionHeight = Math.Min(
            height,
            Math.Max(
                measuredCaptionHeight + 1,
                (int)Math.Round(height * captionFraction)));
        int valueHeight = Math.Max(0, height - captionHeight);
        int left = card.Padding.Left;
        int top = card.Padding.Top;

        caption.SetBounds(left, top, width, captionHeight);
        value.SetBounds(left, top + captionHeight, width, valueHeight);
    }

    private static void AddDiagnosticRow(
        TableLayoutPanel grid, int row, string caption, Label value)
    {
        var label = new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };

        value.Text = "Not measured";
        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.MiddleLeft;

        grid.Controls.Add(label, 0, row);
        grid.Controls.Add(value, 1, row);
    }

    private static void AddSettingRow(
        TableLayoutPanel grid, int row, string caption, Control control)
    {
        var label = new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        control.Dock = DockStyle.None;
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        control.MinimumSize = new Size(0, 30);
        control.Margin = new Padding(3, 8, 3, 8);

        grid.Controls.Add(label, 0, row + 1);
        grid.Controls.Add(control, 1, row + 1);
    }

    private void RefreshDashboard()
    {
        MonitorSnapshot snapshot = _engine.GetSnapshot();
        RouterTelemetry lte = _routerMonitor.GetSnapshot();
        bool hasSamples = snapshot.SuccessfulPings + snapshot.FailedPings > 0;

        UpdateCurrentConnectionIdentity(snapshot, lte);
        int currentConnectionOutages = Math.Max(
            0,
            snapshot.Outages - _currentConnectionOutagesBaseline);

        _statusBadge.Text = !hasSamples
            ? "STARTING"
            : snapshot.IsPaused
            ? "PAUSED"
            : snapshot.IsOnline ? "ONLINE" : "OFFLINE";

        _statusBadge.BackColor = !hasSamples
            ? Color.DarkGoldenrod
            : snapshot.IsPaused
            ? Color.DarkOrange
            : snapshot.IsOnline ? Color.SeaGreen : Color.Firebrick;

        _metrics["CurrentLteSet"].Text = FormatCurrentLteSet(lte);
        _metrics["AccessType"].Text = GetAccessTechnologyLabel();
        _metrics["CurrentIp"].Text = string.IsNullOrWhiteSpace(_lastPublicIp)
            ? "Checking"
            : _lastPublicIp;
        _metrics["ConnectionStable"].Text =
            FormatDuration(DateTime.UtcNow - _currentConnectionSinceUtc);
        _metrics["ConnectionOutages"].Text = hasSamples
            ? currentConnectionOutages.ToString(CultureInfo.CurrentCulture)
            : "";
        _metrics["Ping"].Text = snapshot.CurrentPingMs.HasValue
            ? snapshot.CurrentPingMs + " ms"
            : hasSamples ? "No reply" : "";
        _metrics["Jitter"].Text = snapshot.SuccessfulPings >= 2
            ? snapshot.JitterMs.ToString("0.#") + " ms"
            : "";
        _metrics["Loss"].Text = hasSamples
            ? snapshot.PacketLossPercent.ToString("0.#") + "%"
            : "";
        long sessionSamples = snapshot.SuccessfulPings + snapshot.FailedPings;
        double sessionFailurePercent = sessionSamples > 0
            ? snapshot.FailedPings * 100D / sessionSamples
            : 0;
        _metrics["SuccessFail"].Text = hasSamples
            ? $"{snapshot.FailedPings} / {sessionFailurePercent:0.#}%"
            : "";
        _metrics["RunTime"].Text = hasSamples ? FormatDuration(snapshot.RunTime) : "";
        _metrics["Downtime"].Text = snapshot.TotalDowntime > TimeSpan.Zero
            ? FormatDuration(snapshot.TotalDowntime)
            : hasSamples ? "0 s" : "";
        _metrics["Availability"].Text = hasSamples
            ? snapshot.AvailabilityPercent.ToString("0.###") + "%"
            : "";
        _metrics["Outages"].Text = hasSamples
            ? snapshot.Outages.ToString(CultureInfo.CurrentCulture)
            : "";
        _metrics["SessionAveragePing"].Text = hasSamples
            ? FormatOptionalNumber(snapshot.AveragePingMs, "0.#") + " ms"
            : "";
        _metrics["SessionAverageJitter"].Text = snapshot.SuccessfulPings >= 2
            ? snapshot.SessionAverageJitterMs.ToString("0.#") + " ms"
            : "";
        _metrics["SessionAverageLoss"].Text = hasSamples
            ? snapshot.SessionPacketLossPercent.ToString("0.#") + "%"
            : "";

        RefreshDashboardMetricQuality(
            snapshot,
            lte,
            hasSamples,
            currentConnectionOutages);

        _footer.Text =
            $"Target: {_settings.PingTarget}   •   " +
            $"Next automatic speed test: {FormatNextSpeedTest()}   •   " +
            $"Logs: {_logger.LogFolder}";
        RefreshExperienceSummary(snapshot);
    }

    private void UpdateCurrentConnectionIdentity(
        MonitorSnapshot snapshot,
        RouterTelemetry lte)
    {
        string fingerprint = IsLteConnectionView()
            ? string.Join(
                "|",
                NormalizeConnectionIdentity(lte.Band),
                NormalizeConnectionIdentity(lte.CellId),
                NormalizeConnectionIdentity(lte.Earfcn),
                NormalizeConnectionIdentity(lte.Pci),
                NormalizeConnectionIdentity(_lastPublicIp))
            : string.Join(
                "|",
                NormalizeConnectionIdentity(GetAccessTechnologyLabel()),
                NormalizeConnectionIdentity(_lastPublicIp));

        if (_trackedConnectionFingerprint.Length == 0)
        {
            _trackedConnectionFingerprint = fingerprint;
            _currentConnectionOutagesBaseline = snapshot.Outages;
            return;
        }

        if (snapshot.Outages < _currentConnectionOutagesBaseline)
            _currentConnectionOutagesBaseline = snapshot.Outages;

        if (string.Equals(
                _trackedConnectionFingerprint,
                fingerprint,
                StringComparison.OrdinalIgnoreCase))
            return;

        _trackedConnectionFingerprint = fingerprint;
        _currentConnectionSinceUtc = DateTime.UtcNow;
        _currentConnectionOutagesBaseline = snapshot.Outages;
    }

    private static string NormalizeConnectionIdentity(string? value)
    {
        string normalized = value?.Trim() ?? "";
        return normalized.Length == 0 || normalized.Equals(
            "Unknown",
            StringComparison.OrdinalIgnoreCase)
            ? "-"
            : normalized.ToUpperInvariant();
    }

    private string FormatCurrentLteSet(RouterTelemetry telemetry)
    {
        if (!telemetry.IsConnected || string.IsNullOrWhiteSpace(telemetry.Band) ||
            telemetry.Band is "-" or "Unknown")
            return "Not registered";
        RouterCellLockTarget? displayedLock = GetDisplayedCellLockTarget();
        string cid = IsKnownRadioIdentity(telemetry.CellId)
            ? telemetry.CellId
            : displayedLock?.CellId ?? "";
        string pci = IsKnownRadioIdentity(telemetry.Pci)
            ? telemetry.Pci
            : displayedLock?.Pci ?? "";
        string earfcn = IsKnownRadioIdentity(telemetry.Earfcn)
            ? telemetry.Earfcn
            : displayedLock?.Earfcn ?? "";
        var identity = new List<string>();
        if (IsKnownRadioIdentity(cid)) identity.Add($"CID {cid}");
        if (IsKnownRadioIdentity(pci)) identity.Add($"PCI {pci}");
        if (IsKnownRadioIdentity(earfcn)) identity.Add($"EARFCN {earfcn}");
        return identity.Count == 0
            ? telemetry.Band
            : telemetry.Band + " • " + string.Join(" • ", identity);
    }

    private static string FormatOptionalNumber(double? value, string format) =>
        value.HasValue
            ? value.Value.ToString(format, CultureInfo.CurrentCulture)
            : "-";

    private void RefreshDashboardMetricQuality(
        MonitorSnapshot snapshot,
        RouterTelemetry lte,
        bool hasSamples,
        int currentConnectionOutages)
    {
        double? currentPingQuality = snapshot.CurrentPingMs.HasValue
            ? ScoreLowerIsBetter(snapshot.CurrentPingMs.Value, 40, 80, 150, 300)
            : hasSamples ? 0 : null;
        double recentJitterQuality = ScoreLowerIsBetter(
            snapshot.JitterMs, 10, 25, 60, 120);
        double recentLossQuality = ScoreLowerIsBetter(
            snapshot.PacketLossPercent, 0, 1, 3, 10);
        double sessionPingQuality = snapshot.AveragePingMs.HasValue
            ? ScoreLowerIsBetter(snapshot.AveragePingMs.Value, 40, 80, 150, 300)
            : 0;
        double sessionJitterQuality = ScoreLowerIsBetter(
            snapshot.SessionAverageJitterMs, 10, 25, 60, 120);
        double sessionLossQuality = ScoreLowerIsBetter(
            snapshot.SessionPacketLossPercent, 0, 1, 3, 10);
        long samples = snapshot.SuccessfulPings + snapshot.FailedPings;
        double successPercent = samples > 0
            ? snapshot.SuccessfulPings * 100D / samples
            : 0;
        double availabilityQuality = ScoreHigherIsBetter(
            snapshot.AvailabilityPercent, 99.9, 99, 95, 0);
        double outageRate = snapshot.RunTime.TotalHours > 0
            ? snapshot.Outages / Math.Max(snapshot.RunTime.TotalHours, 1D / 60D)
            : 0;
        double currentConnectionHours = Math.Max(
            (DateTime.UtcNow - _currentConnectionSinceUtc).TotalHours,
            1D / 60D);
        double currentConnectionOutageRate =
            currentConnectionOutages / currentConnectionHours;

        SetDashboardMetricQuality(
            "CurrentLteSet", null,
            $"Current ordered LTE serving set and PCell identity: " +
            $"Band {DisplayValue(lte.Band)}, CID {DisplayValue(lte.CellId)}, " +
            $"EARFCN {DisplayValue(lte.Earfcn)}, PCI {DisplayValue(lte.Pci)}. " +
            "This identity is not a quality score.");
        SetDashboardMetricQuality(
            "AccessType", null,
            "Selected access technology. General Internet measurements remain available for LTE, ADSL/VDSL and FTTB/FTTH.");
        SetDashboardMetricQuality(
            "CurrentIp", null,
            "Current public IP observed by NetPulse. It refreshes automatically and changes independently of LTE RF quality.");
        SetDashboardMetricQuality(
            "ConnectionStable", null,
            (IsLteConnectionView()
                ? "Elapsed time since the last LTE set, PCell identity or public-IP change: " +
                  $"Band {DisplayValue(lte.Band)}, CID {DisplayValue(lte.CellId)}, " +
                  $"EARFCN {DisplayValue(lte.Earfcn)}, PCI {DisplayValue(lte.Pci)}, " +
                  $"public IP {(string.IsNullOrWhiteSpace(_lastPublicIp) ? "pending" : _lastPublicIp)}. "
                : $"Elapsed time since the selected {GetAccessTechnologyLabel()} access profile or public IP last changed. ") +
            "It resets only when that connection identity changes. " +
            "Ping failures, outages and online/offline detection do not reset it; it is independent of RUN TIME.");
        SetDashboardMetricQuality(
            "ConnectionOutages", hasSamples
                ? ScoreLowerIsBetter(currentConnectionOutageRate, 0, 0.25, 1, 3)
                : null,
            "Confirmed outages while the current access identity and public IP have remained unchanged. " +
            "An outage increments this value but does not reset it or the connection timer. " +
            "The value resets only when that access identity or public IP changes.");
        SetDashboardMetricQuality(
            "Ping", hasSamples ? currentPingQuality : null,
            "Measured by this PC to the configured Internet target. Local downloads, Wi-Fi/Ethernet, router queueing and the ISP path all affect it. 0–40 ms excellent, 41–80 ms good, 81–150 ms weak, and 300+ ms critical.");
        SetDashboardMetricQuality(
            "SessionAveragePing", snapshot.AveragePingMs.HasValue
                ? sessionPingQuality
                : null,
            "Average successful PC-to-Internet ping since the application opened or the session was reset. Local traffic such as a large download affects it.");
        SetDashboardMetricQuality(
            "SessionAverageJitter", snapshot.SuccessfulPings >= 2
                ? sessionJitterQuality
                : null,
            "Average PC-to-Internet jitter since the application opened or the session was reset. It is end-to-end, not an LTE modem RF value.");
        SetDashboardMetricQuality(
            "SessionAverageLoss", hasSamples ? sessionLossQuality : null,
            "PC-to-Internet packet-loss percentage across the complete session since the application opened or the session was reset.");
        SetDashboardMetricQuality(
            "Jitter", snapshot.SuccessfulPings >= 2 ? recentJitterQuality : null,
            "Recent PC-to-Internet jitter. Local downloads, Wi-Fi/Ethernet, router queueing and the ISP path all affect it. Up to 10 ms excellent, 25 ms good, 60 ms weak, and 120+ ms critical.");
        SetDashboardMetricQuality(
            "Loss", hasSamples ? recentLossQuality : null,
            "Recent PC-to-Internet packet loss. It does not by itself prove an LTE radio failure. 0% excellent, 1% good, 3% weak, and 10% critical.");
        SetDashboardMetricQuality(
            "SuccessFail", hasSamples
                ? ScoreHigherIsBetter(successPercent, 99.9, 99, 95, 0)
                : null,
            $"Failed ping samples and their percentage of {samples} total session samples since the application opened or the session was reset.");
        SetDashboardMetricQuality(
            "RunTime", null,
            "Elapsed time since the live monitoring session started or was reset; duration itself is not graded.");
        SetDashboardMetricQuality(
            "Downtime", hasSamples ? availabilityQuality : null,
            "Accumulated confirmed Internet downtime in this session; its color follows session availability.");
        SetDashboardMetricQuality(
            "Availability", hasSamples ? availabilityQuality : null,
            "Session availability: 99.9%+ excellent, 99% good, 95% weak, and lower values progressively critical.");
        SetDashboardMetricQuality(
            "Outages", hasSamples
                ? ScoreLowerIsBetter(outageRate, 0, 0.25, 1, 3)
                : null,
            "Confirmed outages per elapsed session hour: zero is green; three or more per hour is critical.");
        SetDashboardMetricQuality(
            "Download", _lastSpeedResult?.DownloadMbps is double download
                ? ScoreHigherIsBetter(download, 25, 10, 3, 0)
                : null,
            "Downloaded by this PC from an external server. Local traffic and the complete PC-to-router-to-ISP path affect it. 25+ Mbps excellent, 10 Mbps good, 3 Mbps weak, and zero critical.");
        SetDashboardMetricQuality(
            "Upload", _lastSpeedResult?.UploadMbps is double upload
                ? ScoreHigherIsBetter(upload, 10, 3, 1, 0)
                : null,
            "Uploaded by this PC to an external server. Local traffic and the complete PC-to-router-to-ISP path affect it. 10+ Mbps excellent, 3 Mbps good, 1 Mbps weak, and zero critical.");
        SetDashboardMetricQuality(
            "SpeedPing", _lastSpeedResult is not null
                ? ScoreLowerIsBetter(_lastSpeedResult.LatencyMs, 40, 80, 150, 300)
                : null,
            "Latency measured by this PC during the last completed external speed test.");
        SetDashboardMetricQuality(
            "SpeedLoss", _lastSpeedResult is not null
                ? ScoreLowerIsBetter(_lastSpeedResult.PacketLossPercent, 0, 1, 3, 10)
                : null,
            "Packet loss measured by this PC during the last completed external speed test.");
    }

    private void SetDashboardMetricQuality(
        string key,
        double? quality,
        string explanation)
    {
        Label value = _metrics[key];
        value.ForeColor = quality.HasValue
            ? QualityColor(quality.Value)
            : NeutralMetricColor();
        string tooltip = quality.HasValue
            ? $"Quality {Math.Clamp(quality.Value, 0, 100):0}/100. {explanation}"
            : explanation;
        Panel card = _metricCards[key];
        _buttonTips.SetToolTip(card, tooltip);
        foreach (Control child in card.Controls)
            _buttonTips.SetToolTip(child, tooltip);
    }

    private static double ScoreLowerIsBetter(
        double value,
        double excellent,
        double good,
        double weak,
        double critical)
    {
        if (value <= excellent)
            return 100;
        if (value <= good)
            return ScaleQuality(value, excellent, good, 100, 75);
        if (value <= weak)
            return ScaleQuality(value, good, weak, 75, 40);
        if (value <= critical)
            return ScaleQuality(value, weak, critical, 40, 0);
        return 0;
    }

    private static double ScoreHigherIsBetter(
        double value,
        double excellent,
        double good,
        double weak,
        double critical)
    {
        if (value >= excellent)
            return 100;
        if (value >= good)
            return ScaleQuality(value, good, excellent, 75, 100);
        if (value >= weak)
            return ScaleQuality(value, weak, good, 40, 75);
        if (value >= critical)
            return ScaleQuality(value, critical, weak, 0, 40);
        return 0;
    }

    private static double ScaleQuality(
        double value,
        double low,
        double high,
        double lowScore,
        double highScore)
    {
        if (high <= low)
            return highScore;
        return lowScore + Math.Clamp((value - low) / (high - low), 0, 1) *
            (highScore - lowScore);
    }

    private Color QualityColor(double quality)
    {
        quality = Math.Clamp(quality, 0, 100);
        bool dark = IsDarkThemeActive();
        Color red = dark ? Color.FromArgb(230, 62, 70) : Color.FromArgb(148, 18, 29);
        Color orange = dark ? Color.FromArgb(241, 126, 48) : Color.FromArgb(190, 76, 19);
        Color yellow = dark ? Color.FromArgb(244, 200, 72) : Color.FromArgb(145, 112, 8);
        Color lightGreen = dark ? Color.FromArgb(153, 207, 91) : Color.FromArgb(55, 135, 55);
        Color green = dark ? Color.FromArgb(67, 207, 124) : Color.FromArgb(20, 116, 58);
        return quality switch
        {
            < 35 => InterpolateColor(red, orange, quality / 35D),
            < 60 => InterpolateColor(orange, yellow, (quality - 35D) / 25D),
            < 80 => InterpolateColor(yellow, lightGreen, (quality - 60D) / 20D),
            _ => InterpolateColor(lightGreen, green, (quality - 80D) / 20D)
        };
    }

    private Color NeutralMetricColor() => IsDarkThemeActive()
        ? Color.FromArgb(230, 235, 241)
        : Color.FromArgb(28, 39, 50);

    private void RefreshExperienceSummary(MonitorSnapshot snapshot)
    {
        ConnectionHealthAssessment health = ConnectionHealthEvaluator.Evaluate(
            snapshot,
            _routerMonitor.GetSnapshot(),
            _lastDiagnosticResult,
            includeLteRadio: IsLteConnectionView());
        _healthScore.Text = snapshot.SuccessfulPings + snapshot.FailedPings == 0
            ? "--"
            : health.Score.ToString(CultureInfo.CurrentCulture);
        _healthScore.ForeColor = QualityColor(health.Score);
        _healthSummary.Text = $"{health.Rating} - {health.Summary}";

        _smartCandidate = _cellHistory.GetRecommendations()
            .Where(LteCellHistoryStore.IsVisibleToUser)
            .FirstOrDefault(item =>
                item.HasRankingEvidence && item.IsEligible &&
                item.Confidence is "Medium" or "High" &&
                TryCreateLockTarget(item, out _, out _));
        if (_smartCandidate is null)
        {
            _smartRecommendation.Text =
                "Gathering controlled reliability and speed evidence for a complete CID profile in this time period.";
            _smartApplyButton.Enabled = false;
        }
        else
        {
            _smartRecommendation.Text =
                $"{_smartCandidate.Band}, EARFCN {_smartCandidate.Earfcn} - " +
                $"rank {_smartCandidate.WeightedScore:0.0}/100, " +
                $"RF {_smartCandidate.RadioScore:0.0}/100; " +
                $"SINR {_smartCandidate.AverageSinrDb:0.#} dB, " +
                $"RSRQ {_smartCandidate.AverageRsrqDb:0.#} dB, " +
                $"RSRP {_smartCandidate.AverageRsrpDbm:0.#} dBm; " +
                $"{_smartCandidate.Confidence.ToLowerInvariant()} confidence.";
            _smartApplyButton.Enabled = !_cellLockBusy && !_bandDiscoveryActive &&
                                        _settings.TpLinkRouterEnabled;
        }
    }

    private void RefreshCellHistory(bool force = false)
    {
        long revision = _cellHistory.Revision;
        int period = LteCellHistoryStore.GetTimePeriod(_clock.Now.DateTime);
        if (!force && revision == _lastCellHistoryRevision &&
            period == _lastCellHistoryPeriod)
            return;
        _lastCellHistoryRevision = revision;
        _lastCellHistoryPeriod = period;

        string? selectedKey = _cellHistoryGrid.SelectedRows.Count > 0 &&
                              _cellHistoryGrid.SelectedRows[0].Tag is
                                  LteCellRecommendation selected
            ? GetCellHistoryRowKey(selected)
            : null;
        IReadOnlyList<LteCellRecommendation> allCurrentRecommendations =
            _cellHistory.GetRecommendations();
        IReadOnlyList<LteCellRecommendation> currentRecommendations =
            allCurrentRecommendations
                .Where(item => !string.IsNullOrWhiteSpace(item.CellId))
                .Where(LteCellHistoryStore.IsVisibleToUser)
                .ToArray();
        string? activeProfileKey = _cellHistory.GetActiveProfileKey();
        bool shortProfilesHidden =
            allCurrentRecommendations.Count > currentRecommendations.Count;
        LteCellRecommendation? recommendedProfile = currentRecommendations
            .FirstOrDefault(item => item.HasRankingEvidence);

        CellHistoryScrollAnchor scrollAnchor = CaptureCellHistoryScrollAnchor();
        var displayRows = new List<CellHistoryDisplayRow>();
        int eligibleRank = 0;
        foreach (LteCellRecommendation item in SortCellHistory(currentRecommendations))
        {
            bool isActive = string.Equals(item.Key, activeProfileKey,
                StringComparison.Ordinal);
            CellHistoryRowStyle style = isActive
                ? CellHistoryRowStyle.Active
                : string.Equals(item.Key, recommendedProfile?.Key, StringComparison.Ordinal)
                    ? CellHistoryRowStyle.Recommended
                : item.UserAdded ? CellHistoryRowStyle.UserAdded
                : item.HasRankingEvidence ? CellHistoryRowStyle.Eligible
                : CellHistoryRowStyle.Ineligible;
            string rank = item.HasRankingEvidence
                ? (++eligibleRank).ToString(CultureInfo.CurrentCulture)
                : "-";
            displayRows.Add(CreateCellHistoryDisplayRow(item, rank, style));
        }

        bool rebuiltRows = ApplyCellHistoryRows(displayRows, selectedKey);

        RefreshObservedCellLockProfiles();

        LteCellRecommendation? best = recommendedProfile;
        if (best is not null)
        {
            _cellSuggestion.Text =
                $"Recommended now: {best.Band} on EARFCN {best.Earfcn}. " +
                "Select the highlighted row to review or apply it.";
            _cellSuggestion.ForeColor = IsDarkThemeActive()
                ? Color.FromArgb(129, 224, 157)
                : Color.FromArgb(25, 82, 45);
        }
        else if (currentRecommendations.Count > 0)
        {
            _cellSuggestion.Text =
                "Collecting Rank evidence: run controlled stability and speed tests for the " +
                "complete CID/EARFCN/PCI candidates in this official-time period. RF remains visible separately.";
            _cellSuggestion.ForeColor = IsDarkThemeActive()
                ? Color.FromArgb(246, 199, 92)
                : Color.DarkGoldenrod;
        }
        else if (shortProfilesHidden)
        {
            _cellSuggestion.Text =
                "Collecting LTE history. A complete CID profile appears after 5 connected minutes.";
            _cellSuggestion.ForeColor = IsDarkThemeActive()
                ? Color.FromArgb(246, 199, 92)
                : Color.DarkGoldenrod;
        }
        else
        {
            _cellSuggestion.Text =
                "Waiting for LTE observations with CID, EARFCN and PCI.";
            _cellSuggestion.ForeColor = IsDarkThemeActive()
                ? Color.FromArgb(177, 187, 199)
                : Color.DimGray;
        }

        if (_cellHistoryGrid.SelectedRows.Count == 0 ||
            _cellHistoryGrid.SelectedRows[0].Tag is not LteCellRecommendation)
        {
            _cellHistoryGrid.ClearSelection();
            _cellHistoryGrid.CurrentCell = null;
        }
        if (rebuiltRows)
            RestoreCellHistoryScrollAnchor(scrollAnchor);

        if (_settings.AutomaticCellLockEnabled)
        {
            TimeSpan dwellRemaining = TimeSpan.Zero;
            if (_settings.LastAutomaticCellLockUtc.HasValue)
            {
                DateTime eligibleUtc = _settings.LastAutomaticCellLockUtc.Value
                    .ToUniversalTime()
                    .AddMinutes(_settings.AutomaticCellLockMinimumDwellMinutes);
                if (eligibleUtc > DateTime.UtcNow)
                    dwellRemaining = eligibleUtc - DateTime.UtcNow;
            }
            string next = dwellRemaining > TimeSpan.Zero
                ? $"next switch allowed in {Math.Ceiling(dwellRemaining.TotalMinutes):0} min"
                : "ready to follow a materially better time-period result";
            _cellAutoStatus.Text =
                $"Adaptive auto on • {_settings.AutomaticCellLockChangesToday}/" +
                $"{_settings.AutomaticCellLockMaxChangesPerDay} changes today • {next}";
            _cellAutoStatus.ForeColor = Color.FromArgb(25, 82, 45);
        }
        else
        {
            _cellAutoStatus.Text =
                "Adaptive auto is off • enable it in Settings after enough time-of-day evidence is collected";
            _cellAutoStatus.ForeColor = Color.DimGray;
        }
    }

    private static CellHistoryDisplayRow CreateCellHistoryDisplayRow(
        LteCellRecommendation item,
        string rank,
        CellHistoryRowStyle style)
    {
        bool hasCurrentPeriodUsage = item.PeriodConnectedTime > TimeSpan.Zero;
        return new CellHistoryDisplayRow(
            $"R|{GetCellHistoryRowKey(item)}",
            item,
            [
                rank,
                item.Band,
                item.Earfcn,
                item.Pci,
                item.CellId ?? "-",
                item.PeriodHasRadioEvidence
                    ? item.RadioScore.ToString("0.0", CultureInfo.CurrentCulture)
                    : "",
                FormatControlledTestGrade(item),
                hasCurrentPeriodUsage ? FormatCompactDuration(item.PeriodConnectedTime) : "",
                hasCurrentPeriodUsage ? FormatHistoryPing(item.AveragePingMs) : "",
                hasCurrentPeriodUsage ? FormatEstimatedCellLoad(item.EstimatedCellLoadPercent) : "",
                hasCurrentPeriodUsage ? $"{item.PeriodDisconnections} / {item.Disconnections}" : "",
                hasCurrentPeriodUsage ? item.DisconnectionsPerHour.ToString("0.00", CultureInfo.CurrentCulture) : "",
                hasCurrentPeriodUsage ? FormatMbps(item.AverageDownloadMbps) : "",
                hasCurrentPeriodUsage ? FormatMbps(item.AverageUploadMbps) : "",
                hasCurrentPeriodUsage ? item.Confidence : "Awaiting usage in current time period"
            ],
            style,
            item.PeriodFailureRatePercent);
    }

    private static string FormatControlledTestGrade(LteCellRecommendation item)
    {
        if (item.PeriodControlledTests <= 0)
            return "Not tested this period";
        double failure = item.PeriodFailureRatePercent ?? 0;
        return $"{item.PeriodControlledTests} test" +
               (item.PeriodControlledTests == 1 ? "" : "s") +
               $" • {failure:0}% failed • {item.PeriodControlledRollbacks} rollback" +
               (item.PeriodControlledRollbacks == 1 ? "" : "s");
    }

    /// <summary>
    /// Keeps the grid stable during one-second telemetry updates. A full rebuild
    /// is used only when rows are added, removed, or reordered;
    /// otherwise only cell values that actually changed are assigned.
    /// </summary>
    private bool ApplyCellHistoryRows(
        IReadOnlyList<CellHistoryDisplayRow> desiredRows,
        string? selectedKey)
    {
        bool sameStructure = _cellHistoryGrid.Rows.Count == desiredRows.Count;
        if (sameStructure)
        {
            for (int index = 0; index < desiredRows.Count; index++)
            {
                if (!string.Equals(
                        GetCellHistoryStructureKey(_cellHistoryGrid.Rows[index].Tag),
                        desiredRows[index].StructureKey,
                        StringComparison.Ordinal))
                {
                    sameStructure = false;
                    break;
                }
            }
        }

        _cellHistoryGrid.SuspendLayout();
        try
        {
            if (!sameStructure)
            {
                _cellHistoryGrid.Rows.Clear();
                foreach (CellHistoryDisplayRow desired in desiredRows)
                {
                    int rowIndex = _cellHistoryGrid.Rows.Add(desired.Values);
                    DataGridViewRow row = _cellHistoryGrid.Rows[rowIndex];
                    row.Tag = desired.Tag;
                    ApplyCellHistoryRowStyle(
                        row,
                        desired.Style,
                        desired.TestFailureRatePercent);
                    if (desired.Tag is LteCellRecommendation tooltipRecommendation)
                        ApplyCellHistoryValueToolTips(row, tooltipRecommendation);
                    if (desired.Tag is LteCellRecommendation recommendation &&
                        string.Equals(
                            GetCellHistoryRowKey(recommendation),
                            selectedKey,
                            StringComparison.Ordinal))
                        row.Selected = true;
                }
                return true;
            }

            for (int rowIndex = 0; rowIndex < desiredRows.Count; rowIndex++)
            {
                CellHistoryDisplayRow desired = desiredRows[rowIndex];
                DataGridViewRow row = _cellHistoryGrid.Rows[rowIndex];
                row.Tag = desired.Tag;
                for (int cellIndex = 0; cellIndex < desired.Values.Length; cellIndex++)
                {
                    object? newValue = desired.Values[cellIndex];
                    if (!Equals(row.Cells[cellIndex].Value, newValue))
                        row.Cells[cellIndex].Value = newValue;
                }
                ApplyCellHistoryRowStyle(
                    row,
                    desired.Style,
                    desired.TestFailureRatePercent);
                if (desired.Tag is LteCellRecommendation recommendation)
                    ApplyCellHistoryValueToolTips(row, recommendation);
            }
            return false;
        }
        finally
        {
            _cellHistoryGrid.ResumeLayout();
        }
    }

    private void ApplyCellHistoryRowStyle(
        DataGridViewRow row,
        CellHistoryRowStyle style,
        double? testFailureRatePercent)
    {
        row.HeaderCell.Tag = style;
        row.DefaultCellStyle.BackColor = Color.Empty;
        row.DefaultCellStyle.ForeColor = Color.Empty;
        row.DefaultCellStyle.SelectionBackColor = Color.Empty;
        row.DefaultCellStyle.SelectionForeColor = Color.Empty;
        row.DefaultCellStyle.Font = null;
        DataGridViewCell testGradeCell = row.Cells["TestGrade"];
        testGradeCell.Style.BackColor = Color.Empty;
        testGradeCell.Style.ForeColor = Color.Empty;
        testGradeCell.Style.SelectionBackColor = Color.Empty;
        testGradeCell.Style.SelectionForeColor = Color.Empty;
        row.Cells["Band"].Style.Padding = style == CellHistoryRowStyle.Group
            ? Padding.Empty
            : new Padding(10, 0, 0, 0);

        bool dark = IsDarkThemeActive();
        switch (style)
        {
            case CellHistoryRowStyle.Group:
                row.DefaultCellStyle.BackColor = dark
                    ? Color.FromArgb(43, 58, 72)
                    : Color.FromArgb(225, 235, 244);
                row.DefaultCellStyle.ForeColor = dark
                    ? Color.FromArgb(224, 237, 247)
                    : Color.FromArgb(25, 70, 105);
                row.DefaultCellStyle.SelectionBackColor = dark
                    ? Color.FromArgb(55, 78, 96)
                    : Color.FromArgb(195, 218, 235);
                row.DefaultCellStyle.SelectionForeColor = dark
                    ? Color.White
                    : Color.FromArgb(18, 57, 86);
                row.DefaultCellStyle.Font = _cellGroupFont;
                break;
            case CellHistoryRowStyle.Ineligible:
                row.DefaultCellStyle.ForeColor = dark
                    ? Color.FromArgb(151, 161, 173)
                    : Color.DimGray;
                break;
            case CellHistoryRowStyle.UserAdded:
                row.DefaultCellStyle.BackColor = dark
                    ? Color.FromArgb(76, 62, 34)
                    : Color.FromArgb(250, 247, 232);
                row.DefaultCellStyle.ForeColor = dark
                    ? Color.FromArgb(255, 239, 190)
                    : Color.FromArgb(75, 60, 20);
                row.DefaultCellStyle.SelectionBackColor = dark
                    ? Color.FromArgb(110, 86, 38)
                    : Color.FromArgb(232, 216, 160);
                row.DefaultCellStyle.SelectionForeColor = dark
                    ? Color.White
                    : Color.FromArgb(55, 42, 10);
                break;
            case CellHistoryRowStyle.Active:
                row.DefaultCellStyle.BackColor = dark
                    ? Color.FromArgb(27, 82, 145)
                    : Color.FromArgb(198, 224, 252);
                row.DefaultCellStyle.ForeColor = dark
                    ? Color.FromArgb(232, 244, 255)
                    : Color.FromArgb(17, 64, 113);
                row.DefaultCellStyle.SelectionBackColor = dark
                    ? Color.FromArgb(38, 111, 190)
                    : Color.FromArgb(126, 184, 238);
                row.DefaultCellStyle.SelectionForeColor = dark
                    ? Color.White
                    : Color.FromArgb(8, 42, 78);
                break;
            case CellHistoryRowStyle.Recommended:
                row.DefaultCellStyle.BackColor = dark
                    ? Color.FromArgb(76, 47, 122)
                    : Color.FromArgb(228, 214, 249);
                row.DefaultCellStyle.ForeColor = dark
                    ? Color.FromArgb(247, 239, 255)
                    : Color.FromArgb(69, 35, 113);
                row.DefaultCellStyle.SelectionBackColor = dark
                    ? Color.FromArgb(105, 67, 164)
                    : Color.FromArgb(181, 151, 225);
                row.DefaultCellStyle.SelectionForeColor = dark
                    ? Color.White
                    : Color.FromArgb(45, 20, 76);
                break;
        }

        if (testFailureRatePercent.HasValue)
        {
            if (style is CellHistoryRowStyle.Active or CellHistoryRowStyle.Recommended)
                ApplyControlledTestGrade(testGradeCell, testFailureRatePercent.Value, dark);
            else
                ApplyControlledTestGrade(row, testFailureRatePercent.Value, dark);
        }
        if (style is CellHistoryRowStyle.Active or CellHistoryRowStyle.Recommended)
            row.DefaultCellStyle.Font = _cellGroupFont;
    }

    private static void ApplyCellHistoryValueToolTips(
        DataGridViewRow row,
        LteCellRecommendation item)
    {
        row.Cells["Score"].ToolTipText = FormatRfScoreToolTip(item);
        row.Cells["Rank"].ToolTipText =
            $"Rank {item.WeightedScore:0.0}/100\r\n" +
            "50% stable controlled connection without failure/rollback\r\n" +
            "25% download versus the best current-period candidate\r\n" +
            "25% upload versus the best current-period candidate\r\n" +
            "Missing reliability or speed evidence contributes 0. RF is shown separately and does not affect Rank.";
        row.Cells["TestGrade"].ToolTipText = item.PeriodControlledTests > 0
            ? $"Current-period controlled reliability: {item.PeriodReliabilityScore:0.0}/100. " +
              $"{item.PeriodControlledFailures} failed and " +
              $"{item.PeriodControlledRollbacks} rolled back from " +
              $"{item.PeriodControlledTests} tests."
            : "No controlled stability test has completed in the current official-time period; the 50% reliability component is therefore 0.";
    }

    private static string FormatRfScoreToolTip(LteCellRecommendation item)
    {
        if (!LteRecommendationScoring.HasRadioEvidence(item))
            return "RF score unavailable in this official-time period. Signal/SINR, RSRQ and RSRP are required.";

        return $"TP-Link signal {FormatOptionalNumber(item.AverageSignalPercent, "0.0")}%\r\n" +
               $"SNR/SINR {item.AverageSinrDb:0.0} dB\r\n" +
               $"RSRQ {item.AverageRsrqDb:0.0} dB\r\n" +
               $"RSRP {item.AverageRsrpDbm:0.0} dBm\r\n" +
               "Current-period measured values; RF does not affect Rank.";
    }

    private static void ApplyControlledTestGrade(
        DataGridViewRow row,
        double failureRatePercent,
        bool dark)
    {
        double failure = Math.Clamp(failureRatePercent / 100D, 0D, 1D);
        Color green = dark
            ? Color.FromArgb(24, 89, 57)
            : Color.FromArgb(202, 239, 214);
        Color deepRed = dark
            ? Color.FromArgb(96, 18, 26)
            : Color.FromArgb(214, 72, 79);
        Color background = InterpolateColor(green, deepRed, failure);
        row.DefaultCellStyle.BackColor = background;
        row.DefaultCellStyle.ForeColor = dark || failure >= 0.62
            ? Color.White
            : Color.FromArgb(18, 48, 29);
        row.DefaultCellStyle.SelectionBackColor = InterpolateColor(
            background,
            Color.White,
            dark ? 0.18 : 0.12);
        row.DefaultCellStyle.SelectionForeColor = dark || failure >= 0.62
            ? Color.White
            : Color.FromArgb(9, 35, 18);
    }

    private static void ApplyControlledTestGrade(
        DataGridViewCell cell,
        double failureRatePercent,
        bool dark)
    {
        double failure = Math.Clamp(failureRatePercent / 100D, 0D, 1D);
        Color green = dark
            ? Color.FromArgb(24, 89, 57)
            : Color.FromArgb(202, 239, 214);
        Color deepRed = dark
            ? Color.FromArgb(96, 18, 26)
            : Color.FromArgb(214, 72, 79);
        Color background = InterpolateColor(green, deepRed, failure);
        cell.Style.BackColor = background;
        cell.Style.ForeColor = dark || failure >= 0.62
            ? Color.White
            : Color.FromArgb(18, 48, 29);
        cell.Style.SelectionBackColor = InterpolateColor(
            background,
            Color.White,
            dark ? 0.18 : 0.12);
        cell.Style.SelectionForeColor = dark || failure >= 0.62
            ? Color.White
            : Color.FromArgb(9, 35, 18);
    }

    private static Color InterpolateColor(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0D, 1D);
        return Color.FromArgb(
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }

    private static string? GetCellHistoryStructureKey(object? tag) => tag switch
    {
        LteCellRecommendation recommendation =>
            $"R|{GetCellHistoryRowKey(recommendation)}",
        _ => null
    };

    private static string GetCellHistoryRowKey(LteCellRecommendation item) =>
        item.Key;

    private CellHistoryScrollAnchor CaptureCellHistoryScrollAnchor()
    {
        int firstRow;
        try
        {
            firstRow = _cellHistoryGrid.FirstDisplayedScrollingRowIndex;
        }
        catch (InvalidOperationException)
        {
            firstRow = -1;
        }

        string? recommendationKey = null;
        if (firstRow >= 0 && firstRow < _cellHistoryGrid.Rows.Count)
        {
            object? tag = _cellHistoryGrid.Rows[firstRow].Tag;
            if (tag is LteCellRecommendation recommendation)
                recommendationKey = GetCellHistoryRowKey(recommendation);
        }
        return new CellHistoryScrollAnchor(firstRow, recommendationKey);
    }

    private void RestoreCellHistoryScrollAnchor(CellHistoryScrollAnchor anchor)
    {
        if (anchor.RowIndex < 0 || _cellHistoryGrid.Rows.Count == 0)
            return;

        int rowIndex = -1;
        for (int index = 0; index < _cellHistoryGrid.Rows.Count; index++)
        {
            object? tag = _cellHistoryGrid.Rows[index].Tag;
            if (tag is LteCellRecommendation recommendation &&
                anchor.RecommendationKey is not null &&
                string.Equals(
                    GetCellHistoryRowKey(recommendation),
                    anchor.RecommendationKey,
                    StringComparison.Ordinal))
            {
                rowIndex = index;
                break;
            }
        }

        if (rowIndex < 0)
            rowIndex = Math.Min(anchor.RowIndex, _cellHistoryGrid.Rows.Count - 1);
        try
        {
            _cellHistoryGrid.FirstDisplayedScrollingRowIndex = rowIndex;
        }
        catch (InvalidOperationException)
        {
            // The grid can be relaid out between refresh and restore; the next
            // automatic refresh will retry with the newly visible anchor.
        }
    }

    private void SortCellHistoryByColumn(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= _cellHistoryGrid.Columns.Count)
            return;
        string column = _cellHistoryGrid.Columns[columnIndex].Name;
        if (string.Equals(column, _cellHistorySortColumn, StringComparison.Ordinal))
            _cellHistorySortAscending = !_cellHistorySortAscending;
        else
        {
            _cellHistorySortColumn = column;
            _cellHistorySortAscending = true;
        }
        foreach (DataGridViewColumn item in _cellHistoryGrid.Columns)
            item.HeaderCell.SortGlyphDirection = SortOrder.None;
        _cellHistoryGrid.Columns[columnIndex].HeaderCell.SortGlyphDirection =
            _cellHistorySortAscending ? SortOrder.Ascending : SortOrder.Descending;
        RefreshCellHistory(force: true);
    }

    private IEnumerable<LteCellRecommendation> SortCellHistory(
        IEnumerable<LteCellRecommendation> source)
    {
        if (_cellHistorySortColumn == "Rank")
            return _cellHistorySortAscending ? source : source.Reverse();
        return _cellHistorySortColumn switch
        {
            "Band" => OrderCellHistory(source, item => item.Band),
            "Earfcn" => OrderCellHistory(source, item => NumericSort(item.Earfcn)),
            "Pci" => OrderCellHistory(source, item => NumericSort(item.Pci)),
            "Cid" => OrderCellHistory(source, item => NumericSort(item.CellId)),
            "Score" => OrderCellHistory(source, item => item.WeightedScore),
            "TestGrade" => OrderCellHistory(
                source,
                item => item.PeriodFailureRatePercent ?? double.MaxValue),
            "Time" => OrderCellHistory(source, item => item.PeriodConnectedTime),
            "Ping" => OrderCellHistory(source, item => item.AveragePingMs ?? double.MaxValue),
            "Load" => OrderCellHistory(source, item => item.EstimatedCellLoadPercent ?? double.MaxValue),
            "Drops" => OrderCellHistory(source, item => item.PeriodDisconnections),
            "DropRate" => OrderCellHistory(source, item => item.DisconnectionsPerHour),
            "Down" => OrderCellHistory(source, item => item.AverageDownloadMbps ?? -1),
            "Up" => OrderCellHistory(source, item => item.AverageUploadMbps ?? -1),
            "Confidence" => OrderCellHistory(source, item => item.Confidence),
            _ => source
        };
    }

    private IEnumerable<LteCellRecommendation> OrderCellHistory<T>(
        IEnumerable<LteCellRecommendation> source,
        Func<LteCellRecommendation, T> selector) =>
        _cellHistorySortAscending
            ? source.OrderBy(selector)
            : source.OrderByDescending(selector);

    private static long NumericSort(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
            out long number)
            ? number
            : long.MaxValue;

    private void RefreshObservedCellLockProfiles()
    {
        IReadOnlyList<LteCellRecommendation> profiles =
            _cellHistory.GetObservedLockProfiles();
        string fingerprint = string.Join("\n", profiles.Select(item =>
            $"{item.Key}|{item.Band}|{item.Earfcn}|{item.Pci}|{item.CellId}"));
        if (string.Equals(
                fingerprint,
                _observedCellLockFingerprint,
                StringComparison.Ordinal))
            return;

        string? selectedKey =
            (_observedCellLockInput.SelectedItem as ObservedCellLockOption)?.Profile?.Key;
        _refreshingObservedCellLockProfiles = true;
        try
        {
            _observedCellLockInput.BeginUpdate();
            _observedCellLockInput.Items.Clear();
            _observedCellLockInput.Items.Add(new ObservedCellLockOption(
                profiles.Count == 0
                    ? "No five-minute observed sets yet"
                    : "Choose a previously observed set...",
                null));
            foreach (LteCellRecommendation profile in profiles)
                _observedCellLockInput.Items.Add(new ObservedCellLockOption(
                    FormatObservedCellLockProfile(profile),
                    profile));
            _observedCellLockInput.SelectedIndex = Math.Max(
                0,
                _observedCellLockInput.Items.Cast<ObservedCellLockOption>()
                    .Select((item, index) => (item, index))
                    .FirstOrDefault(pair => string.Equals(
                        pair.item.Profile?.Key,
                        selectedKey,
                        StringComparison.Ordinal)).index);
            _observedCellLockInput.Enabled = profiles.Count > 0;
            _observedCellLockFingerprint = fingerprint;
        }
        finally
        {
            _observedCellLockInput.EndUpdate();
            _refreshingObservedCellLockProfiles = false;
        }
    }

    private static string FormatObservedCellLockProfile(LteCellRecommendation item)
    {
        string pci = item.Pci == "-" ? "" : $", PCI {item.Pci}";
        string cid = string.IsNullOrWhiteSpace(item.CellId) || item.CellId == "-"
            ? ""
            : $", CID {item.CellId}";
        return $"{item.Band}, EARFCN {item.Earfcn}{pci}{cid}";
    }

    private void UseSelectedObservedCellLockProfile()
    {
        if (_refreshingObservedCellLockProfiles ||
            _observedCellLockInput.SelectedItem is not ObservedCellLockOption
            { Profile: { } profile })
            return;

        _manualBandsInput.Text = profile.PrimaryBand;
        _manualEarfcnInput.Text = profile.Earfcn;
        _manualPciInput.Text = profile.Pci == "-" ? "" : profile.Pci;
        _manualCidInput.Text = profile.CellId is null or "-" ? "" : profile.CellId;
        _manualLockStatus.Text = profile.Pci == "-"
            ? "Observed set loaded. Enter the PCI required by Cell Lock before applying."
            : "Previously observed set loaded. Review it before saving or applying.";
        _manualLockStatus.ForeColor = profile.Pci == "-"
            ? Color.DarkGoldenrod
            : Color.FromArgb(25, 82, 45);
    }

    private void SaveManualCellProfile()
    {
        if (!TryReadManualCellProfile(
                out string band,
                out string earfcn,
                out string pci,
                out string? cid,
                out _,
                out string error))
        {
            MessageBox.Show(error, "Manual Cell Lock",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            _cellHistory.AddManualProfile(band, earfcn, pci, cid);
            _manualLockStatus.Text =
                $"Saved {band}, EARFCN {earfcn}, PCI {pci} to LTE history. Measurements remain empty until observed.";
            _manualLockStatus.ForeColor = Color.FromArgb(25, 82, 45);
            RefreshCellHistory(force: true);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, "Manual Cell Lock",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ApplyManualCellLockAsync(Button button)
    {
        if (_cellLockBusy)
            return;
        if (!TryReadManualCellProfile(
                out string band,
                out string earfcn,
                out string pci,
                out string? cid,
                out RouterCellLockTarget? target,
                out string error))
        {
            MessageBox.Show(error, "Manual Cell Lock",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        bool internetIsOnline = _engine.GetSnapshot().IsOnline;
        string cidText = cid!;
        string safetyText = internetIsOnline
            ? "NetPulse will validate connectivity and restore the previous settings if validation fails."
            : "Internet is already offline. If the router accepts this lock, NetPulse will keep it so you can use it to recover service. Use Restore automatic if needed.";
        if (MessageBox.Show(
                $"Save and apply this MR600 primary-cell lock?\r\n\r\n" +
                $"Band profile: {band}\r\nEARFCN: {earfcn}\r\nPCI: {pci}\r\nCID: {cidText}\r\n\r\n" +
                "Mobile service may briefly disconnect. " + safetyText,
                "Manual Cell Lock",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        _cellHistory.AddManualProfile(band, earfcn, pci, cid);
        RefreshCellHistory(force: true);
        LteCellRecommendation recommendation = _cellHistory.GetRecommendations()
            .First(item =>
                item.Band == band && item.Earfcn == earfcn && item.Pci == pci &&
                string.Equals(item.CellId ?? "", cid ?? "", StringComparison.Ordinal));
        button.Enabled = false;
        try
        {
            await ApplyCellLockWithRollbackAsync(recommendation, target!, automatic: false);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private bool TryReadManualCellProfile(
        out string band,
        out string earfcn,
        out string pci,
        out string? cid,
        out RouterCellLockTarget? target,
        out string error)
    {
        if (!LteCellHistoryStore.TryNormalizeBandProfile(
                _manualBandsInput.Text,
                out band,
                out error))
        {
            earfcn = pci = "";
            cid = null;
            target = null;
            return false;
        }
        earfcn = _manualEarfcnInput.Text.Trim();
        pci = _manualPciInput.Text.Trim();
        string cidInput = _manualCidInput.Text.Trim();
        cid = null;
        if (!int.TryParse(earfcn, NumberStyles.None, CultureInfo.InvariantCulture,
                out int earfcnValue) || earfcnValue is < 1 or > 65535)
        {
            error = "EARFCN must be a number from 1 to 65535.";
            target = null;
            return false;
        }
        if (!int.TryParse(pci, NumberStyles.None, CultureInfo.InvariantCulture,
                out int pciValue) || pciValue is < 0 or > 512)
        {
            error = "PCI must be a number from 0 to 512.";
            target = null;
            return false;
        }
        if (!LteRadioIdentifier.TryNormalizeCellId(cidInput, out cid))
        {
            error = "CID must be a decimal or hexadecimal value " +
                    "(for example ABCDE).";
            target = null;
            return false;
        }
        if (cid is null)
        {
            error = "CID is required so different serving cells are recorded separately.";
            target = null;
            return false;
        }
        int[] bands = Regex.Matches(band, @"B(?<band>\d+)")
            .Select(match => int.Parse(
                match.Groups["band"].Value,
                CultureInfo.InvariantCulture))
            .ToArray();
        target = new RouterCellLockTarget
        {
            Bands = bands,
            Earfcn = earfcn,
            Pci = pci,
            CellId = cid
        };
        error = "";
        return true;
    }

    private async Task RefreshSmsTimelineAsync(bool showErrors)
    {
        if (_smsBusy || IsDisposed)
            return;
        SetSmsBusy(true, "Refreshing SIM messages...");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            _smsMessages = await _routerMonitor.ReadSmsTimelineAsync(timeout.Token);
            QueueUnreadSmsNotifications(_smsMessages);
            string? selectedIdentity = _selectedSmsMessage?.Identity ??
                (_smsGrid.SelectedRows.Count > 0
                    ? (_smsGrid.SelectedRows[0].Tag as RouterSmsMessage)?.Identity
                    : null);
            PopulateSmsGrid(selectedIdentity);
            int unread = _smsMessages.Count(message => message.IsUnread);
            int inbox = _smsMessages.Count(message =>
                message.Folder == RouterSmsFolder.Inbox);
            int sent = _smsMessages.Count(message =>
                message.Folder == RouterSmsFolder.Sent);
            int drafts = _smsMessages.Count(message =>
                message.Folder == RouterSmsFolder.Draft);
            UpdateSmsStatusSummary(inbox, sent, drafts, unread);
        }
        catch (Exception ex)
        {
            _smsStatus.Text = "SIM messages unavailable: " + FriendlyUiError(ex);
            _smsStatus.ForeColor = Color.Firebrick;
            if (showErrors)
            {
                MessageBox.Show(
                    "SIM messages could not be refreshed.\r\n\r\n" + FriendlyUiError(ex),
                    "MR600 SMS",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _nextAutomaticSmsRefreshUtc = DateTime.UtcNow.AddMinutes(30);
            SetSmsBusy(false);
        }
    }

    private bool TryReadSingleManualBand(out int band, out string error)
    {
        band = 0;
        Match match = Regex.Match(_manualBandsInput.Text,
            @"^\s*B?(?<band>\d{1,3})\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !int.TryParse(match.Groups["band"].Value,
                CultureInfo.InvariantCulture, out band) || band is < 1 or > 64)
        {
            error = "Enter exactly one LTE PCell band from 1 to 64.";
            return false;
        }
        error = "";
        return true;
    }

    private async Task ApplyManualBandLockAsync(Button button)
    {
        if (!TryReadSingleManualBand(out int band, out string error))
        {
            MessageBox.Show(error, "Band Lock", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show(
                $"Apply Band Lock to B{band}?\r\n\r\nCell Lock will remain disabled; " +
                "the modem will choose the serving cell and any aggregation SCells.",
                "Band Lock", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        button.Enabled = false;
        try
        {
            var target = new RouterCellLockTarget
            {
                Bands = [band], Earfcn = "", Pci = "", CellId = null
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await _routerMonitor.ApplyCellAndBandLockAsync(target, timeout.Token);
            _displayedCellLockTarget = null;
            AddCellLockEvent($"Band Lock applied to B{band}; Cell Lock disabled");
        }
        catch (Exception ex)
        {
            MessageBox.Show(FriendlyUiError(ex), "Band Lock",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { button.Enabled = true; }
    }

    private async Task ScanManualBandAsync(Button button)
    {
        if (!TryReadSingleManualBand(out int band, out string error))
        {
            MessageBox.Show(error, "Single-band scan", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        button.Enabled = false;
        try { await RunBandCellDiscoveryAsync(band); }
        finally { button.Enabled = true; }
    }

    private void PopulateSmsGrid(string? selectedIdentity = null)
    {
        string? firstVisibleIdentity = null;
        int previousFirstVisibleIndex = -1;
        if (_smsGrid.Rows.Count > 0)
        {
            previousFirstVisibleIndex = _smsGrid.FirstDisplayedScrollingRowIndex;
            if (previousFirstVisibleIndex >= 0 &&
                previousFirstVisibleIndex < _smsGrid.Rows.Count &&
                _smsGrid.Rows[previousFirstVisibleIndex].Tag is RouterSmsMessage firstVisible)
                firstVisibleIdentity = firstVisible.Identity;
        }

        _populatingSmsGrid = true;
        _smsGrid.SuspendLayout();
        try
        {
            _smsGrid.Rows.Clear();
            DataGridViewRow? selectedRow = null;
            DataGridViewRow? firstVisibleRow = null;
            DataGridViewRow? activeConversationRow = null;
            string search = _smsSearchInput.Text.Trim();
            bool conversationsView =
                _smsViewInput.SelectedIndex == SmsConversationsView;
            bool draftsView = _smsViewInput.SelectedIndex == SmsDraftsView;
            _smsGrid.Columns["SmsFrom"].HeaderText = conversationsView
                ? "Conversation"
                : draftsView ? "To" : "Contact / number";
            _smsGrid.Columns["SmsReceived"].HeaderText = conversationsView
                ? "Latest"
                : draftsView ? "Saved" : "Time";
            if (conversationsView)
            {
                IReadOnlyList<SmsConversation> conversations =
                    SmsConversationBuilder.Build(
                        _smsMessages,
                        _settings.SmsContacts,
                        search,
                        _settings.CountryCode);
                foreach (SmsConversation conversation in conversations)
                {
                    RouterSmsMessage latest = conversation.Messages
                        .OrderByDescending(message => message.Timestamp ?? DateTime.MinValue)
                        .First();
                    string preview = latest.IsUnread
                        ? "Unread message"
                        : Regex.Replace(latest.Content, @"\s+", " ").Trim();
                    if (preview.Length > 58)
                        preview = preview[..55] + "...";
                    int headerIndex = _smsGrid.Rows.Add(
                        conversation.UnreadCount > 0 ? "●" : "",
                        conversation.DisplayName,
                        conversation.LastTimestamp.HasValue
                            ? _clock.FormatWallClock(conversation.LastTimestamp.Value)
                            : "",
                        preview);
                    DataGridViewRow header = _smsGrid.Rows[headerIndex];
                    header.Tag = new SmsConversationRow(conversation.Address);
                    header.DefaultCellStyle.Font = conversation.UnreadCount > 0
                        ? _smsUnreadFont
                        : Font;
                    bool dark = IsDarkThemeActive();
                    header.DefaultCellStyle.BackColor = dark
                        ? Color.FromArgb(35, 42, 51)
                        : Color.White;
                    header.DefaultCellStyle.ForeColor = dark
                        ? Color.FromArgb(232, 238, 245)
                        : Color.FromArgb(24, 28, 33);
                    header.DefaultCellStyle.SelectionBackColor = dark
                        ? Color.FromArgb(40, 91, 126)
                        : Color.FromArgb(220, 237, 250);
                    header.DefaultCellStyle.SelectionForeColor = dark
                        ? Color.White
                        : Color.FromArgb(18, 57, 86);
                    if (string.Equals(
                            SmsConversationBuilder.NormalizeAddress(
                                conversation.Address,
                                _settings.CountryCode),
                            _activeSmsConversationAddress,
                            StringComparison.Ordinal))
                        activeConversationRow = header;
                }

                if (!_smsConversationInitialized && !_smsNewConversation &&
                    conversations.Count > 0)
                {
                    _activeSmsConversationAddress =
                        SmsConversationBuilder.NormalizeAddress(
                            conversations[0].Address,
                            _settings.CountryCode);
                    _smsConversationInitialized = true;
                    activeConversationRow = _smsGrid.Rows[0];
                }
            }
            else
            {
                IEnumerable<RouterSmsMessage> messages = _smsMessages
                    .Where(message => draftsView
                        ? message.Folder == RouterSmsFolder.Draft
                        : message.Folder is RouterSmsFolder.Inbox or
                            RouterSmsFolder.Sent)
                    .Where(message => SmsConversationBuilder.MatchesSearch(
                        message,
                        _settings.SmsContacts,
                        search,
                        _settings.CountryCode))
                    .OrderByDescending(message =>
                        message.Timestamp ?? DateTime.MinValue)
                    .ThenByDescending(message => message.Identity,
                        StringComparer.Ordinal);
                foreach (RouterSmsMessage message in messages)
                {
                    AddSmsMessageRow(
                        message,
                        selectedIdentity,
                        firstVisibleIdentity,
                        ref selectedRow,
                        ref firstVisibleRow);
                }
                if (draftsView && selectedRow is null &&
                    _smsGrid.Rows.Count > 0)
                    selectedRow = _smsGrid.Rows[0];
            }

            _smsGrid.ClearSelection();
            _smsGrid.CurrentCell = null;
            if (selectedRow is not null)
            {
                _smsGrid.CurrentCell = selectedRow.Cells[0];
                selectedRow.Selected = true;
            }
            else if (activeConversationRow is not null)
            {
                _smsGrid.CurrentCell = activeConversationRow.Cells[1];
                activeConversationRow.Selected = true;
            }

            if (_smsGrid.Rows.Count > 0)
            {
                int firstIndex = firstVisibleRow?.Index ??
                                 Math.Clamp(previousFirstVisibleIndex, 0,
                                     _smsGrid.Rows.Count - 1);
                if (previousFirstVisibleIndex >= 0 || firstVisibleRow is not null)
                    _smsGrid.FirstDisplayedScrollingRowIndex = firstIndex;
            }
        }
        finally
        {
            _smsGrid.ResumeLayout();
            _populatingSmsGrid = false;
        }

        if (_smsViewInput.SelectedIndex == SmsConversationsView &&
            !_smsNewConversation &&
            !string.IsNullOrWhiteSpace(_activeSmsConversationAddress))
        {
            RenderSmsConversation(_activeSmsConversationAddress, selectedIdentity);
        }
        else
        {
            RouterSmsMessage? restoredMessage = _smsGrid.SelectedRows.Count > 0
                ? _smsGrid.SelectedRows[0].Tag as RouterSmsMessage
                : null;
            if (restoredMessage is null)
                ClearSmsReader();
            else if (restoredMessage.Folder == RouterSmsFolder.Inbox &&
                     restoredMessage.IsUnread)
                ShowSmsAwaitingReadConfirmation(restoredMessage);
            else
                DisplaySmsMessage(restoredMessage);
        }
    }

    private void ChangeSmsView()
    {
        _selectedSmsMessage = null;
        _smsNewConversation = false;
        _activeSmsConversationAddress = null;
        _smsComposeInput.Clear();
        if (_smsViewInput.SelectedIndex == SmsConversationsView)
            _smsConversationInitialized = false;
        PopulateSmsGrid();
    }

    private void AddSmsMessageRow(
        RouterSmsMessage message,
        string? selectedIdentity,
        string? firstVisibleIdentity,
        ref DataGridViewRow? selectedRow,
        ref DataGridViewRow? firstVisibleRow)
    {
        string preview = Regex.Replace(message.Content, @"\s+", " ").Trim();
        if (preview.Length > 80)
            preview = preview[..77] + "...";
        int index = _smsGrid.Rows.Add(
            FormatSmsState(message),
            FormatSmsContact(message.Address),
            FormatSmsTimestamp(message),
            preview);
        DataGridViewRow row = _smsGrid.Rows[index];
        row.Tag = message;
        if (message.IsUnread)
            row.DefaultCellStyle.Font = _smsUnreadFont;
        if (string.Equals(message.Identity, selectedIdentity, StringComparison.Ordinal))
            selectedRow = row;
        if (string.Equals(message.Identity, firstVisibleIdentity, StringComparison.Ordinal))
            firstVisibleRow = row;
    }

    private void ShowSmsConversation(string address, string? selectedIdentity = null)
    {
        string normalized = SmsConversationBuilder.NormalizeAddress(
            address,
            _settings.CountryCode);
        if (!string.Equals(
                normalized,
                _activeSmsConversationAddress,
                StringComparison.Ordinal))
            _smsComposeInput.Clear();
        _smsNewConversation = false;
        _activeSmsConversationAddress = normalized;
        RenderSmsConversation(_activeSmsConversationAddress, selectedIdentity);

        DataGridViewRow? conversationRow = _smsGrid.Rows
            .Cast<DataGridViewRow>()
            .FirstOrDefault(row => row.Tag is SmsConversationRow conversation &&
                string.Equals(
                    SmsConversationBuilder.NormalizeAddress(
                        conversation.Address,
                        _settings.CountryCode),
                    _activeSmsConversationAddress,
                    StringComparison.Ordinal));
        if (conversationRow is not null)
        {
            _smsGrid.ClearSelection();
            _smsGrid.CurrentCell = conversationRow.Cells[1];
            conversationRow.Selected = true;
        }
    }

    private void RenderSmsConversation(string address, string? selectedIdentity = null)
    {
        string normalized = SmsConversationBuilder.NormalizeAddress(
            address,
            _settings.CountryCode);
        RouterSmsMessage[] messages = SmsConversationBuilder.MessagesForAddress(
                _smsMessages,
                normalized,
                _settings.CountryCode)
            .ToArray();
        if (messages.Length == 0)
        {
            ClearSmsReader();
            return;
        }

        _activeSmsConversationAddress = normalized;
        RouterSmsMessage selected = messages.FirstOrDefault(message =>
                string.Equals(message.Identity, selectedIdentity, StringComparison.Ordinal))
            ?? messages.FirstOrDefault(message =>
                string.Equals(message.Identity, _selectedSmsMessage?.Identity,
                    StringComparison.Ordinal))
            ?? messages[^1];
        _selectedSmsMessage = selected;
        _smsSender.Text = FormatSmsContact(selected.Address);
        _smsReceived.Text = FormatSmsTimestamp(selected);
        _smsRecipientInput.ReadOnly = true;
        SetSmsRecipientText(selected.Address);

        bool dark = IsDarkThemeActive();
        _smsThreadPanel.SuspendLayout();
        try
        {
            _smsThreadPanel.Controls.Clear();
            _smsThreadPanel.BackColor = dark
                ? Color.FromArgb(27, 32, 39)
                : Color.FromArgb(244, 247, 250);
            foreach (RouterSmsMessage message in messages)
                _smsThreadPanel.Controls.Add(CreateSmsBubbleRow(message, dark));
        }
        finally
        {
            _smsThreadPanel.ResumeLayout(performLayout: true);
        }
        ResizeSmsConversationRows();
        if (_smsThreadPanel.Controls.Count > 0)
            _smsThreadPanel.ScrollControlIntoView(
                _smsThreadPanel.Controls[_smsThreadPanel.Controls.Count - 1]);
        UpdateSmsActionButtons();
    }

    private Control CreateSmsBubbleRow(RouterSmsMessage message, bool dark)
    {
        bool outgoing = message.Folder is RouterSmsFolder.Sent or RouterSmsFolder.Draft;
        int rowWidth = Math.Max(260, _smsThreadPanel.ClientSize.Width - 28);
        int textWidth = Math.Max(180, (int)Math.Round(rowWidth * 0.61));
        string content = message.Folder == RouterSmsFolder.Inbox && message.IsUnread
            ? "Open this unread message"
            : message.Content;
        string state = message.Folder == RouterSmsFolder.Draft
            ? "Draft"
            : FormatSmsState(message);
        string bubbleText = content + Environment.NewLine + Environment.NewLine +
                            $"{state} • {FormatSmsTimestamp(message)}";
        Size measured = TextRenderer.MeasureText(
            bubbleText,
            Font,
            new Size(textWidth - 28, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        int rowHeight = Math.Clamp(measured.Height + 28, 60, 260);

        var row = new TableLayoutPanel
        {
            Width = rowWidth,
            Height = rowHeight,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 4, 0, 4),
            BackColor = _smsThreadPanel.BackColor,
            Tag = message
        };
        row.ColumnStyles.Add(new ColumnStyle(
            SizeType.Percent,
            outgoing ? 34F : 66F));
        row.ColumnStyles.Add(new ColumnStyle(
            SizeType.Percent,
            outgoing ? 66F : 34F));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var bubble = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = outgoing
                ? new Padding(10, 0, 0, 0)
                : new Padding(0, 0, 10, 0),
            Padding = new Padding(14, 10, 14, 8),
            BackColor = outgoing
                ? dark ? Color.FromArgb(37, 113, 168) : Color.FromArgb(24, 119, 242)
                : dark ? Color.FromArgb(48, 56, 67) : Color.White,
            BorderStyle = string.Equals(
                message.Identity,
                _selectedSmsMessage?.Identity,
                StringComparison.Ordinal)
                ? BorderStyle.FixedSingle
                : BorderStyle.None
        };
        var label = new Label
        {
            Text = bubbleText,
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = outgoing || dark
                ? Color.White
                : Color.FromArgb(28, 33, 39),
            Cursor = Cursors.Hand
        };
        string conversationAddress =
            SmsConversationBuilder.NormalizeAddress(
                message.Address,
                _settings.CountryCode);
        EventHandler open = async (_, _) =>
        {
            if (_smsBusy)
                return;
            _selectedSmsMessage = message;
            if (message.Folder == RouterSmsFolder.Inbox && message.IsUnread)
                await SetSmsUnreadAsync(message, unread: false, automatic: true);
            else if (_smsViewInput.SelectedIndex == SmsConversationsView)
                RenderSmsConversation(conversationAddress, message.Identity);
            else
                DisplaySmsMessage(message);
        };
        bubble.Cursor = Cursors.Hand;
        bubble.Click += open;
        label.Click += open;
        bubble.Controls.Add(label);
        row.Controls.Add(bubble, outgoing ? 1 : 0, 0);
        return row;
    }

    private void ResizeSmsConversationRows()
    {
        int width = Math.Max(260, _smsThreadPanel.ClientSize.Width - 28);
        foreach (Control row in _smsThreadPanel.Controls)
            row.Width = width;
    }

    private void UpdateSmsStatusSummary()
    {
        UpdateSmsStatusSummary(
            _smsMessages.Count(message => message.Folder == RouterSmsFolder.Inbox),
            _smsMessages.Count(message => message.Folder == RouterSmsFolder.Sent),
            _smsMessages.Count(message => message.Folder == RouterSmsFolder.Draft),
            _smsMessages.Count(message => message.IsUnread));
    }

    private void UpdateSmsStatusSummary(int inbox, int sent, int drafts, int unread)
    {
        _smsStatus.Text = _smsMessages.Count == 0
            ? "No SIM messages. Content stays in memory and is never written to logs."
            : $"{inbox} inbox • {sent} sent • {drafts} drafts • " +
              $"{unread} unread • automatic refresh every 30 minutes.";
        _smsStatus.ForeColor = unread > 0 ? Color.DarkGoldenrod : Color.DimGray;
    }

    private static string FormatSmsState(RouterSmsMessage message) =>
        message.Folder switch
        {
            RouterSmsFolder.Inbox when message.IsUnread => "Unread",
            RouterSmsFolder.Inbox => "Inbox",
            RouterSmsFolder.Sent => "Sent",
            _ => "Draft"
        };

    private string FormatSmsTimestamp(RouterSmsMessage message)
    {
        if (message.Timestamp.HasValue)
            return _clock.FormatWallClock(message.Timestamp.Value);
        return string.IsNullOrWhiteSpace(message.TimeText)
            ? "Not provided by the MR600"
            : message.TimeText;
    }

    private async Task OpenSelectedSmsAsync(bool markRead)
    {
        if (_smsBusy)
            return;
        if (_smsGrid.SelectedRows.Count == 0 ||
            _smsGrid.SelectedRows[0].Tag is not RouterSmsMessage message)
            return;
        _selectedSmsMessage = message;
        _activeSmsConversationAddress =
            SmsConversationBuilder.NormalizeAddress(
                message.Address,
                _settings.CountryCode);
        if (message.Folder == RouterSmsFolder.Inbox && message.IsUnread)
        {
            ShowSmsAwaitingReadConfirmation(message);
            if (!markRead)
                return;
            await SetSmsUnreadAsync(message, unread: false, automatic: true);
            return;
        }
        DisplaySmsMessage(message);
    }

    private void ShowSmsAwaitingReadConfirmation(RouterSmsMessage message)
    {
        _smsNewConversation = false;
        _selectedSmsMessage = message;
        _smsSender.Text = FormatSmsContact(message.Address);
        _smsReceived.Text = FormatSmsTimestamp(message);
        _smsRecipientInput.ReadOnly = true;
        SetSmsRecipientText(message.Address);
        if (_smsViewInput.SelectedIndex == SmsConversationsView)
            RenderSmsConversation(message.Address, message.Identity);
        else
            RenderSingleSmsMessage(message);
        UpdateSmsActionButtons();
    }

    private void DisplaySmsMessage(RouterSmsMessage message)
    {
        _smsNewConversation = false;
        _selectedSmsMessage = message;
        _smsSender.Text = FormatSmsContact(message.Address);
        _smsReceived.Text = FormatSmsTimestamp(message);
        _smsRecipientInput.ReadOnly = true;
        SetSmsRecipientText(message.Address);
        RenderSingleSmsMessage(message);
        if (message.Folder == RouterSmsFolder.Draft)
        {
            _smsComposeInput.Text = message.Content;
        }
        UpdateSmsActionButtons();
    }

    private void RenderSingleSmsMessage(RouterSmsMessage message)
    {
        bool dark = IsDarkThemeActive();
        _smsThreadPanel.SuspendLayout();
        try
        {
            _smsThreadPanel.Controls.Clear();
            _smsThreadPanel.BackColor = dark
                ? Color.FromArgb(27, 32, 39)
                : Color.FromArgb(244, 247, 250);
            _smsThreadPanel.Controls.Add(CreateSmsBubbleRow(message, dark));
        }
        finally
        {
            _smsThreadPanel.ResumeLayout(performLayout: true);
        }
        ResizeSmsConversationRows();
    }

    private async Task SetSelectedSmsUnreadAsync(bool unread, bool automatic)
    {
        RouterSmsMessage? message = GetSelectedSmsMessage();
        if (message is null)
        {
            if (!automatic)
            {
                MessageBox.Show(
                    "Select an Inbox message first.",
                    "MR600 SMS",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            return;
        }
        await SetSmsUnreadAsync(message, unread, automatic);
    }

    private async Task SetSmsUnreadAsync(
        RouterSmsMessage message,
        bool unread,
        bool automatic)
    {
        if (_smsBusy)
            return;
        if (message.Folder != RouterSmsFolder.Inbox)
        {
            if (!automatic)
            {
                MessageBox.Show(
                    "Read status is available only for Inbox messages.",
                    "MR600 SMS",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            return;
        }
        if (message.IsUnread == unread)
        {
            _smsStatus.Text = unread
                ? "The selected message is already unread."
                : "The selected message is already read.";
            _smsStatus.ForeColor = Color.DimGray;
            UpdateSmsActionButtons();
            return;
        }

        string action = unread ? "unread" : "read";
        SetSmsBusy(true, automatic
            ? "Opening message and updating read status on the MR600..."
            : $"Marking selected message {action} on the MR600...");
        AddLoggedEvent(new MonitorEvent
        {
            Kind = "SMS",
            Message = $"Mark-{action} attempt started (message content and number omitted)"
        });
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await _routerMonitor.SetSmsUnreadAsync(
                message.Stack,
                message.Index,
                message.PageNumber,
                unread,
                timeout.Token);
            message.IsUnread = unread;
            UpdateSmsRowState(message);
            if (_smsViewInput.SelectedIndex == SmsConversationsView)
            {
                _activeSmsConversationAddress =
                    SmsConversationBuilder.NormalizeAddress(
                        message.Address,
                        _settings.CountryCode);
                PopulateSmsGrid(message.Identity);
            }
            UpdateSmsStatusSummary();
            if (!unread &&
                _smsViewInput.SelectedIndex != SmsConversationsView)
                DisplaySmsMessage(message);
            _smsStatus.Text = unread
                ? "Message marked unread on the MR600."
                : "Message opened and marked read on the MR600.";
            _smsStatus.ForeColor = Color.FromArgb(25, 82, 45);
            AddLoggedEvent(new MonitorEvent
            {
                Kind = "SMS",
                Message = $"Message marked {action} on the MR600"
            });
        }
        catch (Exception ex)
        {
            if (!unread)
                ShowSmsAwaitingReadConfirmation(message);
            _smsStatus.Text = $"The message could not be marked {action}: " +
                              FriendlyUiError(ex);
            _smsStatus.ForeColor = Color.Firebrick;
            AddLoggedEvent(new MonitorEvent
            {
                Kind = "SMS ERROR",
                Message = $"Mark-{action} attempt: " + FriendlyUiError(ex)
            });
        }
        finally
        {
            SetSmsBusy(false);
            UpdateSmsActionButtons();
        }
    }

    private async Task DeleteSelectedSmsAsync()
    {
        RouterSmsMessage? message = GetSelectedSmsMessage();
        if (_smsBusy || message is null)
        {
            MessageBox.Show(
                "Select a message first.",
                "MR600 SMS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        string folder = FormatSmsState(message);
        if (MessageBox.Show(
                $"Delete the selected {folder.ToLowerInvariant()} message from the MR600?\r\n\r\n" +
                "This cannot be undone.",
                "Delete SIM message",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        SetSmsBusy(true, "Deleting selected message from the MR600...");
        AddLoggedEvent(new MonitorEvent
        {
            Kind = "SMS",
            Message = $"Delete {message.Folder} message attempt started (content and number omitted)"
        });
        bool deleted = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await _routerMonitor.DeleteSmsAsync(
                message.Folder,
                message.Stack,
                message.Index,
                message.PageNumber,
                timeout.Token);
            _smsMessages = _smsMessages
                .Where(item => !string.Equals(
                    item.Identity,
                    message.Identity,
                    StringComparison.Ordinal))
                .ToArray();
            _selectedSmsMessage = null;
            PopulateSmsGrid();
            UpdateSmsStatusSummary();
            _smsStatus.Text = "Message deleted from the MR600.";
            _smsStatus.ForeColor = Color.FromArgb(25, 82, 45);
            AddLoggedEvent(new MonitorEvent
            {
                Kind = "SMS",
                Message = $"{message.Folder} message deleted from the MR600"
            });
            deleted = true;
        }
        catch (Exception ex)
        {
            _smsStatus.Text = "The message was not deleted: " + FriendlyUiError(ex);
            _smsStatus.ForeColor = Color.Firebrick;
            AddLoggedEvent(new MonitorEvent
            {
                Kind = "SMS ERROR",
                Message = "Delete message attempt: " + FriendlyUiError(ex)
            });
        }
        finally
        {
            SetSmsBusy(false);
            UpdateSmsActionButtons();
        }
        if (deleted)
            await RefreshSmsTimelineAsync(showErrors: false);
    }

    private void UpdateSmsRowState(RouterSmsMessage message)
    {
        DataGridViewRow? row = _smsGrid.Rows
            .Cast<DataGridViewRow>()
            .FirstOrDefault(candidate =>
                candidate.Tag is RouterSmsMessage candidateMessage &&
                string.Equals(candidateMessage.Identity, message.Identity,
                    StringComparison.Ordinal));
        if (row is null)
            return;
        row.Cells["SmsState"].Value = FormatSmsState(message);
        row.DefaultCellStyle.Font = message.IsUnread ? _smsUnreadFont : Font;
    }

    private void ClearSmsReader()
    {
        _selectedSmsMessage = null;
        _smsSender.Text = "";
        _smsReceived.Text = "";
        _smsThreadPanel.Controls.Clear();
        UpdateSmsActionButtons();
    }

    private void UpdateSmsActionButtons()
    {
        RouterSmsMessage? selected = GetSelectedSmsMessage();
        bool inbox = selected?.Folder == RouterSmsFolder.Inbox;
        _smsReadButton.Enabled = !_smsBusy && inbox && selected?.IsUnread == true;
        _smsUnreadButton.Enabled = !_smsBusy && inbox && selected?.IsUnread == false;
        _smsDeleteButton.Enabled = !_smsBusy && selected is not null;
    }

    private void StartNewSms()
    {
        if (_smsViewInput.SelectedIndex != SmsConversationsView)
            _smsViewInput.SelectedIndex = SmsConversationsView;
        _smsNewConversation = true;
        _smsConversationInitialized = true;
        _activeSmsConversationAddress = null;
        _selectedSmsMessage = null;
        _smsGrid.ClearSelection();
        _smsGrid.CurrentCell = null;
        _smsSender.Text = "New SMS";
        _smsReceived.Text = "";
        _smsThreadPanel.Controls.Clear();
        _smsRecipientInput.ReadOnly = false;
        SetSmsRecipientText("");
        _smsComposeInput.Clear();
        UpdateSmsActionButtons();
        _smsRecipientInput.Focus();
    }

    private void SetSmsRecipientText(string value)
    {
        _settingSmsRecipient = true;
        try
        {
            _smsRecipientInput.Text = value;
        }
        finally
        {
            _settingSmsRecipient = false;
        }
    }

    private void TryJoinTypedSmsConversation()
    {
        if (!_smsNewConversation || _settingSmsRecipient || _smsBusy)
            return;

        string address = _smsRecipientInput.Text.Trim();
        if (!Regex.IsMatch(address, @"^\+?[\d\s().-]{7,25}$"))
            return;

        string normalized = SmsConversationBuilder.NormalizeAddress(
            address,
            _settings.CountryCode);
        RouterSmsMessage? existing = _smsMessages
            .Where(message => message.Folder is RouterSmsFolder.Inbox or
                RouterSmsFolder.Sent)
            .Where(message => string.Equals(
                SmsConversationBuilder.NormalizeAddress(
                    message.Address,
                    _settings.CountryCode),
                normalized,
                StringComparison.Ordinal))
            .OrderByDescending(message => message.Timestamp ?? DateTime.MinValue)
            .FirstOrDefault();
        if (existing is null)
            return;

        string draft = _smsComposeInput.Text;
        ShowSmsConversation(existing.Address, existing.Identity);
        _smsComposeInput.Text = draft;
        _smsComposeInput.Focus();
        _smsComposeInput.SelectionStart = _smsComposeInput.TextLength;
    }

    private async Task SendSmsAsync()
    {
        if (_smsBusy)
            return;
        if (!TryReadSmsComposition(out string recipient, out string content))
            return;
        if (MessageBox.Show(
                $"Send this SMS to {FormatSmsContact(recipient)}?",
                "Send SMS",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        SetSmsBusy(true, "Sending SMS; waiting for MR600 confirmation without a deadline...");
        AddLoggedEvent(new MonitorEvent
        {
            Kind = "SMS",
            Message = "Outgoing SMS attempt started (recipient and content omitted)"
        });
        bool refresh = false;
        try
        {
            _smsSendCancellation = new CancellationTokenSource();
            await _routerMonitor.SendSmsAsync(
                recipient,
                content,
                _smsSendCancellation.Token);
            _smsNewConversation = false;
            if (_smsViewInput.SelectedIndex != SmsConversationsView)
                _smsViewInput.SelectedIndex = SmsConversationsView;
            _activeSmsConversationAddress =
                SmsConversationBuilder.NormalizeAddress(
                    recipient,
                    _settings.CountryCode);
            _smsRecipientInput.ReadOnly = true;
            _smsComposeInput.Clear();
            _smsStatus.Text = "SMS sent successfully. No recipient or message content was logged.";
            _smsStatus.ForeColor = Color.FromArgb(25, 82, 45);
            AddLoggedEvent(new MonitorEvent
            {
                Kind = "SMS",
                Message = "Outgoing SMS confirmed by the MR600"
            });
            refresh = true;
        }
        catch (OperationCanceledException) when (_allowExit || IsDisposed || Disposing)
        {
            return;
        }
        catch (Exception ex)
        {
            _smsStatus.Text = "SMS was not sent: " + FriendlyUiError(ex);
            _smsStatus.ForeColor = Color.Firebrick;
            AddLoggedEvent(new MonitorEvent
            {
                Kind = "SMS ERROR",
                Message = "Outgoing SMS attempt: " + FriendlyUiError(ex)
            });
            MessageBox.Show(
                "The SMS was not sent.\r\n\r\n" + FriendlyUiError(ex),
                "MR600 SMS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            _smsSendCancellation?.Dispose();
            _smsSendCancellation = null;
            SetSmsBusy(false);
        }
        if (refresh)
            await RefreshSmsTimelineAsync(showErrors: false);
    }

    private async Task SaveSmsDraftAsync()
    {
        if (_smsBusy ||
            !TryReadSmsComposition(out string recipient, out string content))
            return;

        SetSmsBusy(true, "Saving draft on the MR600...");
        bool refresh = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _routerMonitor.SaveSmsDraftAsync(recipient, content, timeout.Token);
            _smsNewConversation = false;
            if (_smsViewInput.SelectedIndex != SmsDraftsView)
                _smsViewInput.SelectedIndex = SmsDraftsView;
            _activeSmsConversationAddress = null;
            _smsRecipientInput.ReadOnly = true;
            _smsComposeInput.Clear();
            _smsStatus.Text = "Draft saved on the MR600.";
            _smsStatus.ForeColor = Color.FromArgb(25, 82, 45);
            refresh = true;
        }
        catch (Exception ex)
        {
            _smsStatus.Text = "Draft was not saved: " + FriendlyUiError(ex);
            _smsStatus.ForeColor = Color.Firebrick;
            MessageBox.Show(
                "The draft was not saved.\r\n\r\n" + FriendlyUiError(ex),
                "MR600 SMS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            _nextAutomaticSmsRefreshUtc = DateTime.UtcNow.AddMinutes(30);
            SetSmsBusy(false);
        }
        if (refresh)
            await RefreshSmsTimelineAsync(showErrors: false);
    }

    private bool TryReadSmsComposition(out string recipient, out string content)
    {
        string enteredRecipient = _smsRecipientInput.Text.Trim();
        bool leadingPlus = enteredRecipient.StartsWith('+');
        string digits = Regex.Replace(enteredRecipient, @"\D", "");
        recipient = leadingPlus
            ? "+" + digits
            : digits;
        content = _smsComposeInput.Text;
        if (!Regex.IsMatch(enteredRecipient, @"^\+?[\d\s().-]{1,30}$") ||
            digits.Length is < 1 or > 20)
        {
            MessageBox.Show(
                "Phone number must contain 1 to 20 digits. Spaces, parentheses, " +
                "hyphens and an optional international prefix are accepted.",
                "MR600 SMS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }
        (int used, int maximum) = TpLinkMr600Provider.MeasureSms(content);
        if (string.IsNullOrWhiteSpace(content) || used > maximum)
        {
            MessageBox.Show(
                string.IsNullOrWhiteSpace(content)
                    ? "Enter an SMS message."
                    : $"This message exceeds the MR600 {maximum}-character limit for its encoding.",
                "MR600 SMS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }
        return true;
    }

    private string FormatSmsContact(string address)
    {
        string key = NormalizeSmsContactKey(address);
        return _settings.SmsContacts.TryGetValue(key, out string? name)
            ? $"{name} • {address}"
            : address;
    }

    private string NormalizeSmsContactKey(string address) =>
        SmsConversationBuilder.NormalizeAddress(address, _settings.CountryCode);

    private void SaveSmsContact()
    {
        RouterSmsMessage? selected = GetSelectedSmsMessage();
        string address = selected?.Address ?? _smsRecipientInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(address))
        {
            MessageBox.Show(
                "Select a message or enter a phone number first.",
                "SMS contact",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        string key = NormalizeSmsContactKey(address);
        _settings.SmsContacts.TryGetValue(key, out string? existingName);
        using var dialog = new Form
        {
            Text = "SMS contact name",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new Size(470, 165),
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            Font = Font
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(16)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var nameInput = new TextBox
        {
            Text = existingName ?? "",
            MaxLength = 80,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        layout.Controls.Add(new Label
        {
            Text = "Number",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = address,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 1, 0);
        layout.Controls.Add(new Label
        {
            Text = "Name",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);
        layout.Controls.Add(nameInput, 1, 1);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var save = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            Size = new Size(100, 34)
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Size = new Size(100, 34)
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 2);
        layout.SetColumnSpan(buttons, 2);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        string name = nameInput.Text.Trim();
        if (name.Length == 0)
            _settings.SmsContacts.Remove(key);
        else
            _settings.SmsContacts[key] = name;
        _settings.Normalize();
        _settings.Save();
        string? selectedIdentity = selected?.Identity;
        PopulateSmsGrid(selectedIdentity);
        _smsSender.Text = FormatSmsContact(address);
        _smsStatus.Text = name.Length == 0
            ? "Saved contact name removed."
            : $"Saved {name} for {address}.";
        _smsStatus.ForeColor = Color.FromArgb(25, 82, 45);
    }

    private void RefreshSmsLength()
    {
        (int used, int maximum) = TpLinkMr600Provider.MeasureSms(
            _smsComposeInput.Text);
        _smsLength.Text = $"{used} / {maximum}";
        _smsLength.ForeColor = used > maximum ? Color.Firebrick : Color.DimGray;
    }

    private void SetSmsBusy(bool busy, string? status = null)
    {
        _smsBusy = busy;
        _smsGrid.Enabled = !busy;
        _smsRefreshButton.Enabled = !busy;
        _smsDraftButton.Enabled = !busy;
        _smsContactButton.Enabled = !busy;
        _smsSendButton.Enabled = !busy;
        UpdateSmsActionButtons();
        if (status is not null)
        {
            _smsStatus.Text = status;
            _smsStatus.ForeColor = Color.DarkGoldenrod;
        }
    }

    private static string FriendlyUiError(Exception exception) => exception switch
    {
        OperationCanceledException => "The router did not respond before the timeout.",
        RouterConnectionException => exception.Message,
        HttpRequestException => "The router could not be reached on the local network.",
        _ => "The router operation failed."
    };

    private void CopySelectedCellLock()
    {
        if (_cellHistoryGrid.SelectedRows.Count == 0 ||
            _cellHistoryGrid.SelectedRows[0].Tag is not LteCellRecommendation selected)
        {
            MessageBox.Show(
                "Select a cell from the history first.",
                "LTE history",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        string values =
            $"Band: {selected.Band}{Environment.NewLine}" +
            $"EARFCN: {selected.Earfcn}{Environment.NewLine}" +
            $"PCI: {selected.Pci}{Environment.NewLine}" +
            $"CID: {selected.CellId ?? "not available — profile is not eligible"}";
        try
        {
            Clipboard.SetText(values);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Windows could not copy the values.\r\n\r\n" + ex.Message,
                "LTE history",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async Task ApplySelectedCellLockAsync(Button button)
    {
        if (_cellHistoryGrid.SelectedRows.Count == 0 ||
            _cellHistoryGrid.SelectedRows[0].Tag is not LteCellRecommendation selected)
        {
            MessageBox.Show(
                "Select a cell from the history first.",
                "LTE history",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        await ApplyRecommendationAsync(selected, button, "LTE history");
    }

    private void DeleteSelectedHistoryProfile()
    {
        if (_cellHistoryGrid.SelectedRows.Count == 0 ||
            _cellHistoryGrid.SelectedRows[0].Tag is not LteCellRecommendation selected)
        {
            MessageBox.Show(
                "Select a band/cell profile from LTE History first.",
                "LTE history",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        string identity =
            $"{selected.Band}, EARFCN {selected.Earfcn}, PCI {selected.Pci}" +
            (string.IsNullOrWhiteSpace(selected.CellId)
                ? ""
                : $", CID {selected.CellId}");
        if (MessageBox.Show(
                $"Delete this profile from every time period?\r\n\r\n{identity}\r\n\r\n" +
                "If it is currently active, live monitoring may observe and add it again.",
                "Delete LTE history profile",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        if (_cellHistory.DeleteProfile(selected.Key))
        {
            AddLoggedEvent(new MonitorEvent
            {
                Kind = "LTE HISTORY",
                Message = $"Deleted profile {identity}"
            });
            RefreshCellHistory(force: true);
        }
    }

    private RouterSmsMessage? GetSelectedSmsMessage() =>
        _selectedSmsMessage ??
        (_smsGrid.SelectedRows.Count > 0
            ? _smsGrid.SelectedRows[0].Tag as RouterSmsMessage
            : null);

    private async Task ApplyRecommendationAsync(
        LteCellRecommendation selected,
        Button button,
        string source)
    {
        if (!TryCreateLockTarget(selected, out RouterCellLockTarget? target, out string error))
        {
            MessageBox.Show(error, source,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        bool internetIsOnline = _engine.GetSnapshot().IsOnline;

        string lockDetails;
        if (target!.HasCellTarget)
        {
            string cid = target.CellId ?? "not available";
            lockDetails =
                $"EARFCN: {target.Earfcn}\r\n" +
                $"PCI: {target.Pci}\r\n" +
                $"CID: {cid}";
        }
        else
        {
            lockDetails =
                "Cell: automatic (this firmware does not expose live PCI in Auto mode)";
        }
        string validationText = internetIsOnline
            ? $"NetPulse will validate for {_settings.CellLockValidationSeconds} seconds and restore the previous router settings if internet or LTE does not recover."
            : "Internet is already offline. If the router accepts this lock, NetPulse will keep it so you can use it to recover service. Use Restore automatic if needed.";
        DialogResult answer = MessageBox.Show(
            $"Apply this measured MR600 profile?\r\n\r\n" +
            $"Band: {selected.Band}\r\n" +
            lockDetails + "\r\n\r\n" +
            "Mobile service may briefly disconnect. " + validationText,
            "Apply Cell Lock",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes)
            return;

        button.Enabled = false;
        try
        {
            await ApplyCellLockWithRollbackAsync(selected, target, automatic: false);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private async Task RestoreAutomaticCellSelectionAsync(Button button)
    {
        if (_cellLockBusy)
            return;
        if (MessageBox.Show(
                "Disable MR600 Cell Lock and return band selection to Auto?",
                "Restore automatic selection",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _cellLockBusy = true;
        button.Enabled = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await _routerMonitor.RestoreAutomaticSelectionAsync(timeout.Token);
            _displayedCellLockTarget = null;
            _settings.AutomaticCellLockEnabled = false;
            _settings.LastAutomaticCellLockKey = "";
            _settings.LastAutomaticCellLockUtc = null;
            _automaticCellLockInput.Checked = false;
            ClearPendingCellLock();
            RefreshCellHistory(force: true);
            AddCellLockEvent("MR600 Cell Lock disabled; band selection returned to Auto");
            MessageBox.Show(
                "The MR600 is using automatic band and cell selection.",
                "Restore automatic selection",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "The router settings could not be changed.\r\n\r\n" + ex.Message,
                "Restore automatic selection",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            button.Enabled = true;
            _cellLockBusy = false;
        }
    }

    private void CheckAutomaticCellLock()
    {
        DateTime now = DateTime.UtcNow;
        if (now < _nextAutomaticCellLockCheckUtc)
            return;
        _nextAutomaticCellLockCheckUtc = now.AddMinutes(1);

        if (!_settings.AutomaticCellLockEnabled ||
            !_settings.TpLinkRouterEnabled ||
            _cellLockBusy ||
            (_speedBusy && _speedTestManual) ||
            !_engine.GetSnapshot().IsOnline ||
            _settings.PendingCellLockRollback is not null)
            return;
        if (_speedBusy)
            CancelAutomaticSpeedTestForProfileChange("automatic LTE profile change");
        ResetAutomaticCellLockDailyCounter();
        if (!LteAutoLockPolicy.CanAttempt(_settings, now))
            return;

        IReadOnlyList<LteCellRecommendation> recommendations =
            _cellHistory.GetRecommendations();
        LteCellRecommendation? best = recommendations
            .FirstOrDefault(item =>
                item.HasRankingEvidence && item.IsEligible &&
                item.Confidence is "Medium" or "High");
        if (best is null ||
            !TryCreateLockTarget(best, out RouterCellLockTarget? target, out _))
            return;

        LteCellRecommendation? current = recommendations.FirstOrDefault(item =>
            string.Equals(
                item.Key,
                _settings.LastAutomaticCellLockKey,
                StringComparison.Ordinal));
        if (current is not null && current.Key != best.Key &&
            !LteAutoLockPolicy.IsMeaningfullyBetter(
                best,
                current,
                recommendations))
            return;

        _ = ApplyCellLockWithRollbackAsync(best, target!, automatic: true);
    }

    private void ResetAutomaticCellLockDailyCounter()
    {
        DateTime today = _clock.Now.Date;
        if (_settings.AutomaticCellLockCounterDate?.Date == today)
            return;
        _settings.AutomaticCellLockCounterDate = today;
        _settings.AutomaticCellLockChangesToday = 0;
        _settings.Save();
    }

    private void RegisterAutomaticCellLockChange(string targetKey)
    {
        ResetAutomaticCellLockDailyCounter();
        _settings.AutomaticCellLockChangesToday = Math.Min(
            _settings.AutomaticCellLockChangesToday + 1,
            _settings.AutomaticCellLockMaxChangesPerDay);
        _settings.LastAutomaticCellLockUtc = DateTime.UtcNow;
        _settings.LastAutomaticCellLockKey = targetKey;
        _settings.Save();
    }

    private async Task<CellLockApplyOutcome> ApplyCellLockWithRollbackAsync(
        LteCellRecommendation recommendation,
        RouterCellLockTarget target,
        bool automatic,
        bool showResult = true,
        CancellationToken cancellationToken = default)
    {
        if (_cellLockBusy)
            return CellLockApplyOutcome.NotApplied;
        if (_speedBusy && !_speedTestManual)
            CancelAutomaticSpeedTestForProfileChange("LTE profile change");
        _cellLockBusy = true;
        bool internetWasOnline = _engine.GetSnapshot().IsOnline;
        string profileKind = target.HasCellTarget ? "cell + band lock" : "band profile";
        RouterLockState? previousState = null;
        try
        {
            using (var changeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            {
                previousState = await _routerMonitor.ReadLockStateAsync(changeTimeout.Token);
                if (LockStateMatchesTarget(previousState, target))
                {
                    _displayedCellLockTarget = target.HasCellTarget ? target : null;
                    if (automatic)
                    {
                        _settings.LastAutomaticCellLockUtc = DateTime.UtcNow;
                        _settings.LastAutomaticCellLockKey = recommendation.Key;
                        _settings.Save();
                    }
                    AddCellLockEvent(
                        $"{recommendation.Band} {profileKind} already matches the selected target");
                    if (!automatic && showResult && !IsDisposed)
                    {
                        MessageBox.Show(
                            $"The MR600 already uses the selected {profileKind}.",
                            "Cell Lock active",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    return CellLockApplyOutcome.AlreadyActive;
                }
                _settings.PendingCellLockRollback = previousState;
                _settings.PendingCellLockTargetKey = recommendation.Key;
                _settings.PendingCellLockAppliedUtc = DateTime.UtcNow;
                if (automatic)
                    RegisterAutomaticCellLockChange(recommendation.Key);
                else
                {
                    _settings.LastAutomaticCellLockUtc = DateTime.UtcNow;
                    _settings.LastAutomaticCellLockKey = recommendation.Key;
                }
                _settings.Save();
                await _routerMonitor.ApplyCellAndBandLockAsync(target, changeTimeout.Token);
            }

            AddCellLockEvent(
                (automatic ? "Automatic" : "Manual") +
                $" MR600 {profileKind} applied for {recommendation.Band}" +
                (!automatic && !internetWasOnline
                    ? "; retained without Internet rollback because service was already offline"
                    : "; validating connectivity"));

            if (!automatic && !internetWasOnline)
            {
                _displayedCellLockTarget = target.HasCellTarget ? target : null;
                ClearPendingCellLock();
                _routerDetails.Text =
                    $"{recommendation.Band} Cell Lock accepted while Internet is offline.";
                if (showResult && !IsDisposed)
                {
                    MessageBox.Show(
                        $"The router accepted the selected {profileKind}.\r\n\r\n" +
                        "Internet was already offline, so the lock was kept without " +
                        "connectivity rollback. Use Restore automatic if needed.",
                        "Cell Lock active",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                return CellLockApplyOutcome.AppliedWithoutOnlineValidation;
            }
            _routerDetails.Text =
                $"Validating {recommendation.Band} Cell Lock for " +
                $"{_settings.CellLockValidationSeconds} seconds…";

            await Task.Delay(
                TimeSpan.FromSeconds(_settings.CellLockValidationSeconds),
                cancellationToken);
            MonitorSnapshot internet = _engine.GetSnapshot();
            RouterTelemetry router = _routerMonitor.GetSnapshot();
            using var verifyTimeout =
                new CancellationTokenSource(TimeSpan.FromSeconds(12));
            RouterLockState appliedState = await _routerMonitor.ReadLockStateAsync(
                verifyTimeout.Token);
            bool valid = internet.IsOnline &&
                         LockStateMatchesTarget(appliedState, target) &&
                         MatchesCellIdentity(router, target);

            if (!valid)
            {
                using var rollbackTimeout =
                    new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await _routerMonitor.RestoreLockStateAsync(
                    previousState,
                    rollbackTimeout.Token);
                ClearPendingCellLock();
                AddCellLockEvent(
                    "Cell Lock validation failed; previous MR600 settings restored");
                if (!automatic && showResult && !IsDisposed)
                {
                    MessageBox.Show(
                        "The selected lock did not restore stable internet and LTE within " +
                        $"{_settings.CellLockValidationSeconds} seconds. The previous MR600 " +
                        "settings were restored.",
                        "Cell Lock rolled back",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                return CellLockApplyOutcome.RolledBack;
            }

            ClearPendingCellLock();
            _displayedCellLockTarget = target.HasCellTarget ? target : null;
            AddCellLockEvent(
                $"{recommendation.Band} {profileKind} validated successfully" +
                (target.HasCellTarget
                    ? $" • CID {target.CellId} • PCI {target.Pci} • EARFCN {target.Earfcn}"
                    : ""));
            if (!automatic && showResult && !IsDisposed)
            {
                MessageBox.Show(
                    $"The selected MR600 {profileKind} is active and connectivity " +
                    "passed validation.",
                    "Cell Lock active",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            return CellLockApplyOutcome.Validated;
        }
        catch (Exception ex)
        {
            bool restored = false;
            if (previousState is not null)
            {
                try
                {
                    using var rollbackTimeout =
                        new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    await _routerMonitor.RestoreLockStateAsync(
                        previousState,
                        rollbackTimeout.Token);
                    ClearPendingCellLock();
                    restored = true;
                }
                catch
                {
                    // Keep the pending state so the next launch retries the rollback.
                }
            }

            AddCellLockEvent(
                restored
                    ? "Cell Lock change failed; previous MR600 settings restored"
                    : "Cell Lock change failed; automatic recovery remains pending");
            if (!automatic && showResult && !IsDisposed)
            {
                MessageBox.Show(
                    "The Cell Lock operation did not complete.\r\n\r\n" + ex.Message +
                    (restored
                        ? "\r\n\r\nThe previous router settings were restored."
                        : "\r\n\r\nNetPulse will retry recovery after the router reconnects."),
                    "Cell Lock",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            return restored
                ? CellLockApplyOutcome.RolledBack
                : CellLockApplyOutcome.RecoveryPending;
        }
        finally
        {
            _cellLockBusy = false;
        }
    }

    private async Task RecoverPendingCellLockAsync()
    {
        RouterLockState? pending = _settings.PendingCellLockRollback;
        if (pending is null || _cellLockBusy)
            return;

        _cellLockBusy = true;
        try
        {
            AddCellLockEvent(
                "An interrupted Cell Lock validation was found; recovery is pending");
            for (int attempt = 0; attempt < 30 && !IsDisposed; attempt++)
            {
                try
                {
                    using var timeout =
                        new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await _routerMonitor.RestoreLockStateAsync(pending, timeout.Token);
                    ClearPendingCellLock();
                    AddCellLockEvent(
                        "Previous MR600 settings restored after interrupted validation");
                    return;
                }
                catch (RouterConnectionException)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
                catch (OperationCanceledException)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
            AddCellLockEvent(
                "MR600 recovery is still pending; reconnect TP-Link monitoring to retry");
        }
        catch (Exception ex)
        {
            AddCellLockEvent("MR600 recovery is pending: " + ex.Message);
        }
        finally
        {
            _cellLockBusy = false;
        }
    }

    private void ClearPendingCellLock()
    {
        _settings.PendingCellLockRollback = null;
        _settings.PendingCellLockTargetKey = "";
        _settings.PendingCellLockAppliedUtc = null;
        _settings.Save();
    }

    private void AddCellLockEvent(string message)
    {
        var evt = new MonitorEvent { Kind = "CELL LOCK", Message = message };
        _logger.LogEvent(evt);
        AddEventToGrid(evt);
    }

    private static bool TryCreateLockTarget(
        LteCellRecommendation recommendation,
        out RouterCellLockTarget? target,
        out string error)
    {
        int[] bands = Regex.Matches(
                recommendation.Band,
                @"\bB(?<band>\d{1,3})\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(match => int.Parse(
                match.Groups["band"].Value,
                CultureInfo.InvariantCulture))
            .Distinct()
            .ToArray();
        if (bands.Length == 0 || bands.Any(band => band is < 1 or > 64))
        {
            target = null;
            error =
                "This history entry does not contain an MR600-compatible LTE band.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(recommendation.CellId))
        {
            target = null;
            error = "This profile has no CID and cannot be applied safely.";
            return false;
        }

        target = new RouterCellLockTarget
        {
            Bands = bands,
            Earfcn = IsNumericRadioValue(recommendation.Earfcn) &&
                     IsNumericRadioValue(recommendation.Pci)
                ? recommendation.Earfcn
                : "",
            Pci = IsNumericRadioValue(recommendation.Earfcn) &&
                  IsNumericRadioValue(recommendation.Pci)
                ? recommendation.Pci
                : "",
            CellId = IsNumericRadioValue(recommendation.Earfcn) &&
                     IsNumericRadioValue(recommendation.Pci)
                ? recommendation.CellId
                : null
        };
        error = "";
        return true;
    }

    private static bool MatchesTarget(
        RouterTelemetry telemetry,
        RouterCellLockTarget target)
    {
        if (!telemetry.IsConnected)
            return false;
        if (target.HasCellTarget)
        {
            if (!string.Equals(telemetry.Earfcn, target.Earfcn, StringComparison.Ordinal) ||
                !string.Equals(telemetry.Pci, target.Pci, StringComparison.Ordinal))
                return false;
            if (!string.IsNullOrWhiteSpace(target.CellId) &&
                !string.Equals(telemetry.CellId, target.CellId, StringComparison.Ordinal))
                return false;
        }

        int[] activeBands = Regex.Matches(
                telemetry.Band,
                @"\bB(?<band>\d{1,3})\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(match => int.Parse(
                match.Groups["band"].Value,
                CultureInfo.InvariantCulture))
            .ToArray();
        return activeBands.Length > 0 &&
               activeBands.All(target.Bands.Contains);
    }

    private static bool MatchesCellIdentity(
        RouterTelemetry telemetry,
        RouterCellLockTarget target)
    {
        if (!telemetry.IsConnected)
            return false;
        if (!target.HasCellTarget)
            return true;
        return string.Equals(telemetry.Earfcn, target.Earfcn, StringComparison.Ordinal) &&
               string.Equals(telemetry.Pci, target.Pci, StringComparison.Ordinal) &&
               string.Equals(telemetry.CellId, target.CellId,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool LockStateMatchesTarget(
        RouterLockState state,
        RouterCellLockTarget target)
    {
        if (!state.BandSelectionEnabled)
            return false;
        if (target.HasCellTarget)
        {
            if (!state.CellLockEnabled ||
                !string.Equals(state.Earfcn, target.Earfcn, StringComparison.Ordinal) ||
                !string.Equals(state.Pci, target.Pci, StringComparison.Ordinal))
                return false;
            if (!string.IsNullOrWhiteSpace(target.CellId) &&
                !string.Equals(state.CellId, target.CellId, StringComparison.Ordinal))
                return false;
        }
        else if (state.CellLockEnabled)
        {
            return false;
        }

        int low = 0;
        int high = 0;
        foreach (int band in target.Bands.Distinct())
        {
            if (band <= 32)
                low |= unchecked(1 << (band - 1));
            else
                high |= unchecked(1 << (band - 33));
        }
        return state.BandMaskLow == low && state.BandMaskHigh == high;
    }

    private static bool IsNumericRadioValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private void RefreshRouterDashboard(RouterTelemetry telemetry)
    {
        if (!IsLteConnectionView())
        {
            RefreshInternetConnectionDashboard();
            return;
        }

        SetConnectionMetricCaptions(
            ("Status", "ROUTER STATUS"), ("Isp", "ISP"),
            ("Network", "NETWORK TYPE"), ("Band", "LTE BAND"),
            ("Signal", "SIGNAL"), ("Rsrp", "RSRP"),
            ("Rsrq", "RSRQ"), ("Snr", "SNR"),
            ("Pci", "PCI"), ("Cell", "CELL ID"),
            ("Earfcn", "EARFCN"), ("Sim", "SIM STATUS"),
            ("Data", "DATA USED"), ("RouterUpload", "ROUTER UPLOAD"),
            ("RouterDownload", "ROUTER DOWNLOAD"), ("Updated", "LAST UPDATE"));

        RouterManagementState management = _routerMonitor.GetManagementState();
        _routerConnectionState.Text = RouterManagementLabel(management).ToUpperInvariant();
        _routerConnectionState.BackColor = management switch
        {
            RouterManagementState.Connected => Color.SeaGreen,
            RouterManagementState.Connecting or
            RouterManagementState.SlowResponse or
            RouterManagementState.Reconnecting or
            RouterManagementState.Busy => Color.DarkGoldenrod,
            RouterManagementState.Disabled or
            RouterManagementState.NotConfigured => Color.DimGray,
            _ => Color.Firebrick
        };

        string[] identityParts =
        [
            telemetry.Model,
            telemetry.HardwareVersion,
            telemetry.FirmwareVersion
        ];
        string versionDetails = string.Join(
            " • ",
            identityParts
                .Where(value => !string.IsNullOrWhiteSpace(value) && value != "Unknown")
                .Distinct(StringComparer.OrdinalIgnoreCase));
        if (versionDetails.Length == 0)
            versionDetails = "TP-Link router • protected local telemetry";
        MonitorSnapshot internet = _engine.GetSnapshot();
        RouterCellLockTarget? displayedLock = GetDisplayedCellLockTarget();
        string pathStates =
            $"Router: {RouterManagementLabel(management)}  •  " +
            $"LTE: {(telemetry.IsConnected ? "registered" : "not registered")}  •  " +
            $"Internet: {(internet.IsOnline ? "online" : "offline")}";
        _routerDetails.Text = "ROUTER LTE TELEMETRY  •  " +
            (string.IsNullOrWhiteSpace(telemetry.Error)
                ? versionDetails + "  •  " + pathStates
                : pathStates + "  •  " + telemetry.Error);
        if (displayedLock is not null)
            _routerDetails.Text +=
                $"  •  Cell Lock: CID {displayedLock.CellId}, " +
                $"PCI {displayedLock.Pci}, EARFCN {displayedLock.Earfcn}";

        _routerMetrics["Status"].Text =
            $"Router {RouterManagementLabel(management)} / LTE " +
            (telemetry.IsConnected ? "registered" : "not registered");
        _routerMetrics["Isp"].Text = DisplayValue(telemetry.Isp);
        _routerMetrics["Network"].Text = DisplayValue(telemetry.NetworkType);
        _routerMetrics["Band"].Text = DisplayValue(telemetry.Band);
        _routerMetrics["Signal"].Text = telemetry.SignalPercent.HasValue
            ? telemetry.SignalPercent.Value.ToString(CultureInfo.CurrentCulture) + "%"
            : "";
        _routerMetrics["Rsrp"].Text = FormatMeasurement(telemetry.RsrpDbm, "dBm");
        _routerMetrics["Rsrq"].Text = FormatMeasurement(telemetry.RsrqDb, "dB");
        _routerMetrics["Snr"].Text = FormatMeasurement(telemetry.SnrDb, "dB");
        _routerMetrics["Pci"].Text = DisplayValue(
            IsKnownRadioIdentity(telemetry.Pci) ? telemetry.Pci : displayedLock?.Pci);
        _routerMetrics["Cell"].Text = DisplayValue(
            IsKnownRadioIdentity(telemetry.CellId) ? telemetry.CellId : displayedLock?.CellId);
        _routerMetrics["Earfcn"].Text = DisplayValue(
            IsKnownRadioIdentity(telemetry.Earfcn) ? telemetry.Earfcn : displayedLock?.Earfcn);
        _routerMetrics["Sim"].Text = DisplayValue(telemetry.SimStatus);
        _routerMetrics["Data"].Text = FormatBytes(telemetry.TotalBytes);
        _routerMetrics["RouterUpload"].Text = FormatRate(telemetry.UploadBytesPerSecond);
        _routerMetrics["RouterDownload"].Text = FormatRate(telemetry.DownloadBytesPerSecond);
        _routerMetrics["Updated"].Text = telemetry.IsConnected
            ? _clock.FormatTime(telemetry.Timestamp)
            : "";
        SetRouterMetricTooltip("Status", "Router-management and LTE-registration state read from the router local API.");
        foreach (string key in new[] { "Isp", "Network", "Band", "Signal", "Rsrp", "Rsrq", "Snr", "Pci", "Cell", "Earfcn", "Sim", "Data", "RouterUpload", "RouterDownload", "Updated" })
            SetRouterMetricTooltip(key, "Router LTE telemetry read directly from the configured router local API; this is not inferred from PC ping or speed-test traffic.");
    }

    private RouterCellLockTarget? GetDisplayedCellLockTarget()
    {
        if (_bandDiscoveryActive)
            return null;
        if (_displayedCellLockTarget is not null)
            return _displayedCellLockTarget;
        if (string.IsNullOrWhiteSpace(_settings.LastAutomaticCellLockKey))
            return null;
        LteCellRecommendation? saved = _cellHistory.GetHistoryRecommendations()
            .FirstOrDefault(item => string.Equals(
                item.Key,
                _settings.LastAutomaticCellLockKey,
                StringComparison.Ordinal));
        if (saved is not null &&
            TryCreateLockTarget(saved, out RouterCellLockTarget? target, out _))
            _displayedCellLockTarget = target;
        return _displayedCellLockTarget;
    }

    private static bool IsKnownRadioIdentity(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value != "-" && value != "0";

    private static string RouterManagementLabel(RouterManagementState state) => state switch
    {
        RouterManagementState.NotConfigured => "not configured",
        RouterManagementState.SlowResponse => "slow response",
        RouterManagementState.AuthenticationRequired => "authentication required",
        RouterManagementState.Unreachable => "unreachable",
        _ => state.ToString().ToLowerInvariant()
    };

    private void RefreshInternetConnectionDashboard()
    {
        MonitorSnapshot snapshot = _engine.GetSnapshot();
        string access = GetAccessTechnologyLabel();
        bool dsl = _settings.ConnectionDetailsView == "Dsl";
        if (dsl)
        {
            SetConnectionMetricCaptions(
                ("Status", "INTERNET STATUS"), ("Isp", "ACCESS TECHNOLOGY"),
                ("Network", "DOWNLOAD TEST"), ("Band", "UPLOAD TEST"),
                ("Signal", "CURRENT PING"), ("Rsrp", "JITTER"),
                ("Rsrq", "PACKET LOSS"), ("Snr", "AVAILABILITY"),
                ("Pci", "DOWNSTREAM ATTENUATION"), ("Cell", "UPSTREAM ATTENUATION"),
                ("Earfcn", "DOWNSTREAM SNR MARGIN"), ("Sim", "UPSTREAM SNR MARGIN"),
                ("Data", "DEFAULT GATEWAY"), ("RouterUpload", "DNS LATENCY"),
                ("RouterDownload", "OUTAGES"), ("Updated", "TOTAL DOWNTIME"));
        }
        else
        {
            SetConnectionMetricCaptions(
                ("Status", "INTERNET STATUS"), ("Isp", "ACCESS TECHNOLOGY"),
                ("Network", "DOWNLOAD TEST"), ("Band", "UPLOAD TEST"),
                ("Signal", "CURRENT PING"), ("Rsrp", "JITTER"),
                ("Rsrq", "PACKET LOSS"), ("Snr", "AVAILABILITY"),
                ("Pci", "OPTICAL / BUILDING RX"),
                ("Cell", "OPTICAL / BUILDING TX"),
                ("Earfcn", "ONT / MDU STATUS"),
                ("Sim", "ACCESS LINK RATE"),
                ("Data", "DEFAULT GATEWAY"), ("RouterUpload", "DNS LATENCY"),
                ("RouterDownload", "OUTAGES"), ("Updated", "TOTAL DOWNTIME"));
        }

        string status = snapshot.IsPaused
            ? "Paused"
            : snapshot.IsOnline ? "Online" : "Offline";
        _routerConnectionState.Text = status.ToUpperInvariant();
        _routerConnectionState.BackColor = snapshot.IsPaused
            ? Color.DarkOrange
            : snapshot.IsOnline ? Color.SeaGreen : Color.Firebrick;
        _routerDetails.Text =
            $"PC → INTERNET / PC → ROUTER  •  {access} monitoring. " +
            "Line values require a compatible router or ONT provider.";

        _routerMetrics["Status"].Text = status;
        _routerMetrics["Isp"].Text = access;
        _routerMetrics["Network"].Text = _metrics["Download"].Text;
        _routerMetrics["Band"].Text = _metrics["Upload"].Text;
        _routerMetrics["Signal"].Text = snapshot.CurrentPingMs.HasValue
            ? snapshot.CurrentPingMs.Value.ToString(CultureInfo.CurrentCulture) + " ms"
            : "";
        bool hasSamples = snapshot.SuccessfulPings + snapshot.FailedPings > 0;
        _routerMetrics["Rsrp"].Text = snapshot.SuccessfulPings >= 2
            ? snapshot.JitterMs.ToString("0.#", CultureInfo.CurrentCulture) + " ms"
            : "";
        _routerMetrics["Rsrq"].Text = hasSamples
            ? snapshot.PacketLossPercent.ToString("0.#", CultureInfo.CurrentCulture) + "%"
            : "";
        _routerMetrics["Snr"].Text = hasSamples
            ? snapshot.AvailabilityPercent.ToString("0.###", CultureInfo.CurrentCulture) + "%"
            : "";
        _routerMetrics["Pci"].Text = "Router data required";
        _routerMetrics["Cell"].Text = "Router data required";
        _routerMetrics["Earfcn"].Text = dsl
            ? "Router data required"
            : "ONT data required";
        _routerMetrics["Sim"].Text = dsl
            ? "Router data required"
            : "ONT data required";
        _routerMetrics["Data"].Text = DisplayValue(_gatewayValue.Text);
        _routerMetrics["RouterUpload"].Text = DisplayValue(_dnsValue.Text);
        _routerMetrics["RouterDownload"].Text = snapshot.Outages.ToString(CultureInfo.CurrentCulture);
        _routerMetrics["Updated"].Text = FormatDuration(snapshot.TotalDowntime);
        foreach (string key in _routerMetricCards.Keys)
            SetRouterMetricTooltip(
                key,
                key is "Pci" or "Cell" or "Earfcn" or "Sim"
                    ? "This line-specific value is unavailable until a compatible router or ONT provider exposes it. NetPulse does not invent it."
                    : "General PC-to-Internet or PC-to-router measurement for the selected access profile. Local traffic, Wi-Fi/Ethernet and the ISP path may affect it.");
    }

    private bool IsLteConnectionView() =>
        _settings.ConnectionDetailsView == "Lte";

    private string GetAccessTechnologyLabel() =>
        _settings.ConnectionDetailsView switch
        {
            "Dsl" => "ADSL / VDSL",
            "Fiber" => "FTTB / FTTH",
            _ => "Mobile / LTE"
        };

    private void SetConnectionMetricCaptions(
        params (string Key, string Caption)[] captions)
    {
        foreach ((string key, string caption) in captions)
            _routerMetricCaptions[key].Text = caption;
    }

    private void SetRouterMetricTooltip(string key, string text)
    {
        if (!_routerMetricCards.TryGetValue(key, out Panel? card))
            return;
        _buttonTips.SetToolTip(card, text);
        foreach (Control child in card.Controls)
            _buttonTips.SetToolTip(child, text);
    }

    private async Task RunSpeedTestAsync(bool manual, string? automaticReason = null)
    {
        if (_speedBusy || _bandDiscoveryActive)
            return;

        _speedBusy = true;
        _speedTestManual = manual;
        _lastSpeedResult = null;
        _speedCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        _speedButton.Text = "Cancel speed test";
        RouterTelemetry speedTestCell = _routerMonitor.GetSnapshot();
        AddLoggedEvent(new MonitorEvent
        {
            Kind = "SPEED",
            Message =
                    $"Speed test started" +
                    (manual || string.IsNullOrWhiteSpace(automaticReason)
                        ? ""
                        : $" after {automaticReason}") +
                    $": {_settings.DownloadSampleMegabytes} MB down / " +
                    $"{_settings.UploadSampleMegabytes} MB up (180-second limit)"
        });

        try
        {
            SpeedTestResult result = await SpeedTestEngine.RunAsync(
                _settings.DownloadSampleMegabytes,
                _settings.UploadSampleMegabytes,
                _speedCancellation.Token);
            _lastSpeedResult = result;

            _metrics["Download"].Text = result.DownloadMbps.HasValue
                ? result.DownloadMbps.Value.ToString("0.00") + " Mbps"
                : "N/A";

            _metrics["Upload"].Text = result.UploadMbps.HasValue
                ? result.UploadMbps.Value.ToString("0.00") + " Mbps"
                : "N/A";

            _metrics["SpeedPing"].Text = result.LatencyMs.ToString("0.0") + " ms";
            _metrics["SpeedLoss"].Text =
                result.PacketLossPercent.ToString("0.0") + "%";

            _logger.LogSpeedTest(result);

            RouterTelemetry completedOnCell = _routerMonitor.GetSnapshot();
            if (IsSameLteCell(speedTestCell, completedOnCell) &&
                _cellHistory.RecordSpeedTest(speedTestCell, result))
            {
                RefreshCellHistory(force: true);
            }

            string message =
                $"Speed result ({result.Provider}" +
                (manual || string.IsNullOrWhiteSpace(automaticReason)
                    ? ""
                    : $", triggered by {automaticReason}") +
                "): " +
                $"{(result.DownloadMbps.HasValue ? result.DownloadMbps.Value.ToString("0.00") : "N/A")} Mbps down, " +
                $"{(result.UploadMbps.HasValue ? result.UploadMbps.Value.ToString("0.00") : "N/A")} Mbps up";

            if (!string.IsNullOrWhiteSpace(result.Warning))
                message += " — " + result.Warning;

            AddLoggedEvent(new MonitorEvent { Kind = "SPEED", Message = message });
        }
        catch (OperationCanceledException)
        {
            AddLoggedEvent(new MonitorEvent
            {
                Kind = "SPEED",
                Message = "Speed test cancelled or timed out"
            });
        }
        catch (Exception ex)
        {
            AddLoggedEvent(new MonitorEvent
            {
                Kind = "ERROR",
                Message = "Speed test failed: " + ex.Message
            });

            if (manual)
            {
                MessageBox.Show(
                    "The speed test did not complete.\r\n\r\n" + ex.Message +
                    "\r\n\r\nPing monitoring continues normally.",
                    "Speed test",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _speedCancellation.Dispose();
            _speedCancellation = null;
            _speedBusy = false;
            _speedTestManual = false;
            _speedButton.Text = "Run speed test now";
            _nextAutomaticSpeedTest = GetNextSpeedTime();
            RefreshDashboard();
        }
    }

    private void CheckAutomaticSpeedTest()
    {
        if (_speedBusy || _bandDiscoveryActive)
            return;

        MonitorSnapshot snapshot = _engine.GetSnapshot();
        if (!snapshot.IsOnline || snapshot.IsPaused)
            return;

        if (_automaticSpeedTests.TryTakeDue(
                DateTime.UtcNow,
                out AutomaticSpeedTestRequest? request))
        {
            _ = RunSpeedTestAsync(manual: false, request!.Reason);
            return;
        }

        if (_settings.SpeedTestIntervalMinutes <= 0 ||
            DateTime.Now < _nextAutomaticSpeedTest)
            return;

        _ = RunSpeedTestAsync(manual: false);
    }

    private void CheckPublicIpChange()
    {
        if (_bandDiscoveryActive || _publicIpCheckBusy ||
            DateTime.UtcNow < _nextPublicIpCheckUtc)
            return;

        MonitorSnapshot snapshot = _engine.GetSnapshot();
        if (!snapshot.IsOnline || snapshot.IsPaused)
            return;

        _publicIpCheckBusy = true;
        _nextPublicIpCheckUtc = DateTime.UtcNow.AddSeconds(15);
        _ = ProbePublicIpAsync();
    }

    private async Task ProbePublicIpAsync()
    {
        try
        {
            string? address = await PublicIpProbe.ReadAsync(CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(address))
            {
                _lastPublicIp = address;
                _automaticSpeedTests.ObservePublicIp(address, DateTime.UtcNow);
            }
        }
        finally
        {
            _publicIpCheckBusy = false;
        }
    }

    private DateTime GetNextSpeedTime()
    {
        return _settings.SpeedTestIntervalMinutes <= 0
            ? DateTime.MaxValue
            : DateTime.Now.AddMinutes(_settings.SpeedTestIntervalMinutes);
    }

    private string FormatNextSpeedTest()
    {
        DateTime? eventDueUtc = _automaticSpeedTests.DueUtc;
        if (eventDueUtc.HasValue)
            return "after connection stabilizes";
        if (_settings.SpeedTestIntervalMinutes <= 0)
            return "on connection changes";
        return _clock.FormatTime(_nextAutomaticSpeedTest);
    }

    private void AddEventToGrid(MonitorEvent evt)
    {
        _eventHistory.Insert(0, evt);
        if (_eventHistory.Count > 5000)
            _eventHistory.RemoveRange(5000, _eventHistory.Count - 5000);
        if (EventMatchesFilters(evt))
            _eventsGrid.Rows.Insert(
                0,
                _clock.FormatDisplay(evt.Timestamp),
                evt.Kind,
                evt.Message);
        while (_eventsGrid.Rows.Count > 1000)
            _eventsGrid.Rows.RemoveAt(_eventsGrid.Rows.Count - 1);
    }

    private void RefreshEventGrid()
    {
        if (_eventsGrid.Columns.Count == 0)
            return;
        _eventsGrid.SuspendLayout();
        try
        {
            _eventsGrid.Rows.Clear();
            foreach (MonitorEvent evt in _eventHistory.Where(EventMatchesFilters).Take(1000))
            {
                _eventsGrid.Rows.Add(
                    _clock.FormatDisplay(evt.Timestamp),
                    evt.Kind,
                    evt.Message);
            }
        }
        finally
        {
            _eventsGrid.ResumeLayout();
        }
    }

    private void CancelAutomaticSpeedTestForProfileChange(string reason)
    {
        if (!_speedBusy || _speedTestManual || _speedCancellation is null)
            return;
        _speedCancellation.Cancel();
        AddLoggedEvent(new MonitorEvent
        {
            Kind = "SPEED",
            Message = $"Scheduled speed test cancelled for {reason}; LTE controls remain available"
        });
    }

    private async Task RunBandCellDiscoveryAsync(int? singleBand = null)
    {
        if (_speedBusy && !_speedTestManual)
            CancelAutomaticSpeedTestForProfileChange("band and cell discovery");
        if (_cellLockBusy || (_speedBusy && _speedTestManual) ||
            _experimentCancellation is not null ||
            !_settings.TpLinkRouterEnabled ||
            _settings.PendingCellLockRollback is not null)
        {
            MessageBox.Show(
                "Connect TP-Link monitoring and wait for the current router, " +
                "speed-test, experiment, or recovery operation to finish.",
                "Band & Cell Discovery",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        RouterTelemetry initialRouter = _routerMonitor.GetSnapshot();
        if (!initialRouter.IsConnected)
        {
            MessageBox.Show(
                "The router is not currently reporting an LTE connection.",
                "Band & Cell Discovery",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        LteBandScanPlan plan = LteBandDiscovery.CreatePlan(
            initialRouter,
            _settings.CountryCode,
            _cellHistory.GetHistoryRecommendations().Select(item => item.Band));
        if (singleBand is int requestedBand)
            plan = new LteBandScanPlan([requestedBand], plan.RouterProfile,
                "user-selected single-band serving-cell scan", false);
        if (plan.Bands.Count == 0)
        {
            MessageBox.Show(
                "NetPulse does not have a verified radio-band profile for this " +
                "router revision, and it has not observed any LTE bands yet. " +
                "No speculative band locks will be sent.",
                "Band & Cell Discovery",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        const int secondsPerBand = 30;
        const int completeIdentityWaitSeconds = 75;
        string bands = string.Join(", ", plan.Bands.Select(item => "B" + item));
        string coverage = plan.IsComplete
            ? "This is the verified complete band plan for the detected router profile."
            : "Only bands already observed on this unverified router profile will be scanned.";
        int firstStageMinutes = (int)Math.Ceiling(
            plan.Bands.Count * secondsPerBand / 60D);
        if (MessageBox.Show(
                $"Run the complete three-stage scan for {plan.Bands.Count} bands " +
                $"({bands})? Stage 1 needs at least {firstStageMinutes} minutes; " +
                "the total duration depends on how many real cells and aggregation " +
                $"sets are found.\r\n\r\n{coverage}\r\n\r\n" +
                "1. Lock each band alone and wait for a complete PCell " +
                "EARFCN/PCI/CID identity.\r\n" +
                "2. Lock each discovered PCell while all serving bands are " +
                "available, and record every aggregation set the modem creates.\r\n" +
                "3. Reapply and measure every unique PCell + ordered band set.\r\n\r\n" +
                "Brief internet interruptions are expected. Results are saved to " +
                "LTE History only with a complete CID, and the exact previous " +
                "router state is restored when finished or cancelled.",
                "Band & Cell Discovery",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        RouterLockState? originalState = null;
        string originalKey = _settings.LastAutomaticCellLockKey;
        DateTime? originalLockUtc = _settings.LastAutomaticCellLockUtc;
        var results = new List<LteBandCellObservation>();
        string scanId = DateTime.UtcNow.ToString(
            "yyyyMMdd-HHmmss'Z'",
            CultureInfo.InvariantCulture);
        bool restored = true;
        bool cancelled = false;
        _bandDiscoveryCancellation = new CancellationTokenSource();
        CancellationToken token = _bandDiscoveryCancellation.Token;
        _cellLockBusy = true;
        _bandDiscoveryActive = true;
        _bandDiscoveryButton.Text = "Cancel discovery";
        SetLteProfileMutationEnabled(false);
        _bandDiscoveryProgress.Visible = true;
        _speedButton.Enabled = false;
        try
        {
            using (var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                readTimeout.CancelAfter(TimeSpan.FromSeconds(20));
                originalState = await _routerMonitor.ReadLockStateAsync(readTimeout.Token);
            }

            _settings.PendingCellLockRollback = originalState;
            _settings.PendingCellLockTargetKey = "band-cell-discovery";
            _settings.PendingCellLockAppliedUtc = DateTime.UtcNow;
            _settings.Save();
            restored = false;
            AddLoggedEvent(new MonitorEvent
            {
                Kind = "LTE DISCOVERY",
                Message = $"Band & Cell Discovery started: {bands}"
            });

            for (int index = 0; index < plan.Bands.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                int band = plan.Bands[index];
                _bandDiscoveryStatus.Text =
                    $"Stage 1/3 • scanning B{band} alone • " +
                    $"{index + 1}/{plan.Bands.Count}";
                var target = new RouterCellLockTarget
                {
                    Bands = [band],
                    Earfcn = "",
                    Pci = ""
                };

                try
                {
                    using var changeTimeout =
                        CancellationTokenSource.CreateLinkedTokenSource(token);
                    changeTimeout.CancelAfter(TimeSpan.FromSeconds(20));
                    await _routerMonitor.ApplyCellAndBandLockAsync(
                        target,
                        changeTimeout.Token);
                    var bandResults = new List<LteBandCellObservation>();
                    await CollectBandDiscoverySamplesAsync(
                        band,
                        index + 1,
                        plan.Bands.Count,
                        secondsPerBand,
                        bandResults,
                        token,
                        waitForCompleteIdentity: true,
                        maximumSeconds: completeIdentityWaitSeconds,
                        stageLabel: "Stage 1/3");
                    foreach (LteBandCellObservation observation in bandResults)
                        AccumulateDiscoveryObservation(results, observation);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    results.Add(LteBandCellObservation.NotObserved(
                        band,
                        "Band change failed: " + FriendlyUiError(ex)));
                }

                if (!results.Any(item => item.RequestedBand == band))
                    results.Add(LteBandCellObservation.NotObserved(band));
                int identities = results.Count(item =>
                    item.RequestedBand == band && item.Samples > 0 &&
                    item.HasCompleteIdentity);
                bool servingBandSeen = results.Any(item =>
                    item.RequestedBand == band && item.Samples > 0);
                AddLoggedEvent(new MonitorEvent
                {
                    Kind = "LTE DISCOVERY",
                    Message = identities > 0
                        ? $"Stage 1/3 • B{band}: {identities} complete PCell " +
                          "identity record(s) observed"
                        : servingBandSeen
                            ? $"Stage 1/3 • B{band}: serving band observed but " +
                              "complete EARFCN/PCI/CID was not exposed"
                            : $"Stage 1/3 • B{band}: no serving cell observed"
                });
            }

            LteBandCellObservation[] discoveredCells = results
                .Where(item => item.Samples > 0 && item.HasCompleteIdentity)
                .GroupBy(item => string.Join('|', item.RequestedBand,
                    item.Earfcn, item.Pci, item.CellId),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();

            if (discoveredCells.Length == 0)
            {
                _bandDiscoveryStatus.Text =
                    "Stage 1/3 found no complete PCell identity; " +
                    "Stages 2 and 3 cannot run";
                AddLoggedEvent(new MonitorEvent
                {
                    Kind = "LTE DISCOVERY ERROR",
                    Message = "Stage 1/3 observed no complete EARFCN/PCI/CID; " +
                              "Stages 2 and 3 were not executed"
                });
            }
            else
            {
                int[] availableBands = discoveredCells
                    .Select(item => item.RequestedBand)
                    .Distinct()
                    .Order()
                    .ToArray();
                var stageTwoSets = new List<LteBandCellObservation>();
                for (int index = 0; index < discoveredCells.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    LteBandCellObservation cell = discoveredCells[index];
                    _bandDiscoveryStatus.Text =
                        $"Stage 2/3 • scanning aggregation sets for CID " +
                        $"{cell.CellId} • {index + 1}/{discoveredCells.Length}";
                    var target = new RouterCellLockTarget
                    {
                        Bands = availableBands,
                        Earfcn = cell.Earfcn,
                        Pci = cell.Pci,
                        CellId = cell.CellId
                    };
                    try
                    {
                        using var changeTimeout =
                            CancellationTokenSource.CreateLinkedTokenSource(token);
                        changeTimeout.CancelAfter(TimeSpan.FromSeconds(20));
                        await _routerMonitor.ApplyCellAndBandLockAsync(
                            target, changeTimeout.Token);
                        var trialResults = new List<LteBandCellObservation>();
                        await CollectBandDiscoverySamplesAsync(
                            cell.RequestedBand,
                            index + 1,
                            discoveredCells.Length,
                            secondsPerBand,
                            trialResults,
                            token,
                            requireSingleBand: false,
                            waitForCompleteIdentity: true,
                            maximumSeconds: completeIdentityWaitSeconds,
                            requiredIdentity: cell,
                            stageLabel: "Stage 2/3");
                        foreach (LteBandCellObservation observation in trialResults)
                        {
                            AccumulateDiscoveryObservation(stageTwoSets, observation);
                            AccumulateDiscoveryObservation(results, observation);
                        }
                        int setsFound = trialResults
                            .Where(item => item.HasCompleteIdentity)
                            .Select(item => item.IdentityKey)
                            .Distinct(StringComparer.Ordinal)
                            .Count();
                        AddLoggedEvent(new MonitorEvent
                        {
                            Kind = "LTE DISCOVERY",
                            Message = setsFound > 0
                                ? $"Stage 2/3 • CID {cell.CellId}: " +
                                  $"{setsFound} serving aggregation set(s) observed"
                                : $"Stage 2/3 • CID {cell.CellId}: no matching " +
                                  "serving aggregation set observed"
                        });
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        AddLoggedEvent(new MonitorEvent
                        {
                            Kind = "LTE DISCOVERY ERROR",
                            Message = $"Stage 2/3 • CID {cell.CellId}: " +
                                      FriendlyUiError(ex)
                        });
                    }
                }

                LteBandCellObservation[] servingSets = stageTwoSets
                    .Where(item => item.Samples > 0 && item.HasCompleteIdentity)
                    .GroupBy(item => item.IdentityKey, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToArray();
                if (servingSets.Length == 0)
                {
                    _bandDiscoveryStatus.Text =
                        "Stage 2/3 found no aggregation set; Stage 3 cannot run";
                    AddLoggedEvent(new MonitorEvent
                    {
                        Kind = "LTE DISCOVERY ERROR",
                        Message = "Stage 2/3 found no complete cell-qualified " +
                                  "aggregation set; Stage 3 was not executed"
                    });
                }
                for (int index = 0; index < servingSets.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    LteBandCellObservation set = servingSets[index];
                    int[] setBands = LteBandDiscovery
                        .ExtractBands(set.ServingProfile)
                        .ToArray();
                    if (setBands.Length == 0)
                        continue;
                    _bandDiscoveryStatus.Text =
                        $"Stage 3/3 • measuring {set.ServingProfile} / CID " +
                        $"{set.CellId} • {index + 1}/{servingSets.Length}";
                    var target = new RouterCellLockTarget
                    {
                        Bands = setBands,
                        Earfcn = set.Earfcn,
                        Pci = set.Pci,
                        CellId = set.CellId
                    };
                    try
                    {
                        using var changeTimeout =
                            CancellationTokenSource.CreateLinkedTokenSource(token);
                        changeTimeout.CancelAfter(TimeSpan.FromSeconds(20));
                        await _routerMonitor.ApplyCellAndBandLockAsync(
                            target, changeTimeout.Token);
                        var measuredResults = new List<LteBandCellObservation>();
                        await CollectBandDiscoverySamplesAsync(
                            set.RequestedBand,
                            index + 1,
                            servingSets.Length,
                            secondsPerBand,
                            measuredResults,
                            token,
                            requireSingleBand: false,
                            waitForCompleteIdentity: true,
                            maximumSeconds: completeIdentityWaitSeconds,
                            requiredIdentity: set,
                            recordHistory: true,
                            stageLabel: "Stage 3/3");
                        foreach (LteBandCellObservation observation in measuredResults)
                            AccumulateDiscoveryObservation(results, observation);
                        AddLoggedEvent(new MonitorEvent
                        {
                            Kind = "LTE DISCOVERY",
                            Message = measuredResults.Any(item => item.HasCompleteIdentity)
                                ? $"Stage 3/3 • measured {set.ServingProfile}, " +
                                  $"CID {set.CellId}"
                                : $"Stage 3/3 • {set.ServingProfile}, CID " +
                                  $"{set.CellId}: complete identity not confirmed"
                        });
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        AddLoggedEvent(new MonitorEvent
                        {
                            Kind = "LTE DISCOVERY ERROR",
                            Message = $"Stage 3/3 • {set.ServingProfile}, CID " +
                                      $"{set.CellId}: {FriendlyUiError(ex)}"
                        });
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            AddLoggedEvent(new MonitorEvent
            {
                Kind = "LTE DISCOVERY",
                Message = "Band & Cell Discovery cancelled; restoring router state"
            });
        }
        catch (Exception ex)
        {
            AddLoggedEvent(new MonitorEvent
            {
                Kind = "LTE DISCOVERY ERROR",
                Message = FriendlyUiError(ex)
            });
            if (!_bandDiscoveryExitPending && !IsDisposed)
            {
                MessageBox.Show(
                    "Discovery stopped safely. The original router state will be " +
                    "restored.\r\n\r\n" + FriendlyUiError(ex),
                    "Band & Cell Discovery",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _bandDiscoveryStatus.Text = "Restoring the previous router state...";
            if (originalState is not null)
            {
                try
                {
                    using var restoreTimeout =
                        new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await _routerMonitor.RestoreLockStateAsync(
                        originalState,
                        restoreTimeout.Token);
                    ClearPendingCellLock();
                    restored = true;
                }
                catch (Exception ex)
                {
                    AddLoggedEvent(new MonitorEvent
                    {
                        Kind = "LTE DISCOVERY ERROR",
                        Message = "Original router state requires recovery: " +
                                  FriendlyUiError(ex)
                    });
                }
            }

            _settings.LastAutomaticCellLockKey = originalKey;
            _settings.LastAutomaticCellLockUtc = originalLockUtc;
            _settings.Save();
            int lockReadyCandidates = 0;
            foreach (LteBandCellObservation observation in results
                         .GroupBy(item => item.IdentityKey, StringComparer.Ordinal)
                         .Select(group => group.First()))
            {
                _logger.LogBandDiscovery(scanId, initialRouter, observation);
                if (observation.Samples > 0 &&
                    _cellHistory.AddDiscoveryCandidate(
                        observation.ServingProfile,
                        observation.Earfcn,
                        observation.Pci,
                        observation.CellId))
                    lockReadyCandidates++;
            }
            if (lockReadyCandidates > 0)
            {
                AddLoggedEvent(new MonitorEvent
                {
                    Kind = "LTE DISCOVERY",
                    Message = $"{lockReadyCandidates} lock-ready candidate " +
                              "identity record(s) added to LTE History"
                });
            }
            _bandDiscoveryActive = false;
            _cellLockBusy = false;
            _bandDiscoveryCancellation.Dispose();
            _bandDiscoveryCancellation = null;
            _bandDiscoveryButton.Text = "Scan bands & cells";
            _bandDiscoveryButton.Enabled = true;
            _bandDiscoveryProgress.Visible = false;
            SetLteProfileMutationEnabled(true);
            _speedButton.Enabled = true;
            _bandDiscoveryStatus.Text = restored
                ? lockReadyCandidates > 0
                    ? $"Automatic discovery is idle • {lockReadyCandidates} " +
                      "complete candidate(s) saved • previous router state restored"
                    : "Automatic discovery is idle • no complete CID candidate " +
                      "was saved • previous router state restored"
                : "Automatic discovery ended • router recovery is pending";
            RefreshCellHistory(force: true);
        }

        AddLoggedEvent(new MonitorEvent
        {
            Kind = "LTE DISCOVERY",
            Message = restored
                ? $"Band & Cell Discovery {(cancelled ? "cancelled" : "completed")}; " +
                  "previous router state restored"
                : "Band & Cell Discovery ended; router recovery remains pending"
        });
        if (results.Count > 0 && !_bandDiscoveryExitPending && !IsDisposed)
        {
            NetPulseTheme theme = Enum.TryParse(
                    _settings.Theme,
                    out NetPulseTheme parsed)
                ? parsed
                : NetPulseTheme.System;
            using var resultDialog = new BandDiscoveryResultsForm(
                plan,
                results,
                _logger.BandDiscoveryPath,
                theme);
            resultDialog.ShowDialog(this);
        }
    }

    private void SetLteProfileMutationEnabled(bool enabled)
    {
        foreach (Control control in _lteProfileMutationControls)
            control.Enabled = enabled;
        if (!enabled)
            _smartApplyButton.Enabled = false;
    }

    private async Task CollectBandDiscoverySamplesAsync(
        int band,
        int position,
        int totalBands,
        int seconds,
        List<LteBandCellObservation> results,
        CancellationToken cancellationToken,
        bool requireSingleBand = true,
        bool waitForCompleteIdentity = false,
        int? maximumSeconds = null,
        LteBandCellObservation? requiredIdentity = null,
        bool recordHistory = false,
        string stageLabel = "Discovery")
    {
        DateTime startedUtc = DateTime.UtcNow;
        DateTime minimumDeadline = startedUtc.AddSeconds(seconds);
        DateTime hardDeadline = startedUtc.AddSeconds(
            Math.Max(seconds, maximumSeconds ?? seconds));
        bool sawServingProfile = false;
        string stableIdentityKey = "";
        int stableIdentitySamples = 0;
        // Ignore the first snapshots after the write; they can still describe
        // the previous serving profile while the modem is re-registering.
        await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
        while (DateTime.UtcNow < hardDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTime displayedDeadline = DateTime.UtcNow < minimumDeadline
                ? minimumDeadline
                : hardDeadline;
            int remaining = Math.Max(0, (int)Math.Ceiling(
                (displayedDeadline - DateTime.UtcNow).TotalSeconds));
            _bandDiscoveryStatus.Text =
                $"{stageLabel} • B{band} • {position}/{totalBands} • " +
                $"{remaining}s remaining";
            RouterTelemetry sample = _routerMonitor.GetSnapshot();
            if (LteBandDiscovery.TryReadServingCell(
                    band,
                    sample,
                    out LteBandCellObservation? observation,
                    requireSingleBand) &&
                observation is not null)
            {
                sawServingProfile = true;
                if (requiredIdentity is not null)
                {
                    observation = LteBandDiscovery.RetainLockedPcellIdentity(
                        requiredIdentity,
                        observation);
                }
                if (requiredIdentity is null ||
                    MatchesDiscoveryIdentity(requiredIdentity, observation))
                {
                    AccumulateDiscoveryObservation(results, observation);
                    if (observation.HasCompleteIdentity)
                    {
                        if (string.Equals(stableIdentityKey,
                                observation.IdentityKey,
                                StringComparison.Ordinal))
                            stableIdentitySamples++;
                        else
                        {
                            stableIdentityKey = observation.IdentityKey;
                            stableIdentitySamples = 1;
                        }
                    if (recordHistory)
                    {
                        _cellHistory.RecordTelemetry(
                            WithDiscoveryIdentity(sample, observation));
                    }
                    }
                    else
                    {
                        stableIdentityKey = "";
                        stableIdentitySamples = 0;
                    }
                }
                else
                {
                    stableIdentityKey = "";
                    stableIdentitySamples = 0;
                }
            }
            if (DateTime.UtcNow >= minimumDeadline &&
                (!waitForCompleteIdentity || !sawServingProfile ||
                 stableIdentitySamples >= 3))
                break;
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private static bool MatchesDiscoveryIdentity(
        LteBandCellObservation expected,
        LteBandCellObservation observed) =>
        observed.HasCompleteIdentity &&
        string.Equals(expected.Earfcn, observed.Earfcn,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.Pci, observed.Pci,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.CellId, observed.CellId,
            StringComparison.OrdinalIgnoreCase);

    private static RouterTelemetry WithDiscoveryIdentity(
        RouterTelemetry sample,
        LteBandCellObservation observation) => new()
    {
        Timestamp = sample.Timestamp,
        IsConnected = sample.IsConnected,
        Status = sample.Status,
        ProviderName = sample.ProviderName,
        Model = sample.Model,
        Isp = sample.Isp,
        NetworkType = sample.NetworkType,
        Band = sample.Band,
        PrimaryBand = sample.PrimaryBand,
        SimStatus = sample.SimStatus,
        SignalPercent = sample.SignalPercent,
        RsrpDbm = sample.RsrpDbm,
        RsrqDb = sample.RsrqDb,
        SnrDb = sample.SnrDb,
        RssiDbm = sample.RssiDbm,
        Pci = observation.Pci,
        CellId = observation.CellId,
        Earfcn = observation.Earfcn,
        UnreadSmsCount = sample.UnreadSmsCount,
        TotalBytes = sample.TotalBytes,
        UploadBytesPerSecond = sample.UploadBytesPerSecond,
        DownloadBytesPerSecond = sample.DownloadBytesPerSecond,
        HardwareVersion = sample.HardwareVersion,
        FirmwareVersion = sample.FirmwareVersion,
        Error = sample.Error
    };

    private static void AccumulateDiscoveryObservation(
        List<LteBandCellObservation> results,
        LteBandCellObservation sample)
    {
        int exactIndex = results.FindIndex(item =>
            string.Equals(item.IdentityKey, sample.IdentityKey, StringComparison.Ordinal));
        if (exactIndex >= 0)
        {
            results[exactIndex] = results[exactIndex].Merge(sample);
            return;
        }

        int[] compatible = results
            .Select((item, index) => (item, index))
            .Where(pair => pair.item.RequestedBand == sample.RequestedBand &&
                           pair.item.ServingProfile == sample.ServingProfile &&
                           CompatibleRadioValue(pair.item.Earfcn, sample.Earfcn) &&
                           CompatibleRadioValue(pair.item.Pci, sample.Pci) &&
                           CompatibleRadioValue(pair.item.CellId, sample.CellId))
            .Select(pair => pair.index)
            .ToArray();
        if (compatible.Length == 1)
        {
            int index = compatible[0];
            LteBandCellObservation previous = results[index];
            results[index] = previous with
            {
                Earfcn = MoreCompleteRadioValue(previous.Earfcn, sample.Earfcn),
                Pci = MoreCompleteRadioValue(previous.Pci, sample.Pci),
                CellId = MoreCompleteRadioValue(previous.CellId, sample.CellId),
                RsrpDbm = sample.RsrpDbm ?? previous.RsrpDbm,
                RsrqDb = sample.RsrqDb ?? previous.RsrqDb,
                SnrDb = sample.SnrDb ?? previous.SnrDb,
                LastSeen = sample.LastSeen,
                Samples = previous.Samples + 1,
                Status = sample.Status
            };
            return;
        }

        results.Add(sample);
    }

    private static bool CompatibleRadioValue(string left, string right) =>
        left == "-" || right == "-" ||
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string MoreCompleteRadioValue(string left, string right) =>
        left == "-" && right != "-" ? right : left;

    private async Task RunCellExperimentAsync()
    {
        if (_speedBusy && !_speedTestManual)
            CancelAutomaticSpeedTestForProfileChange("controlled LTE experiment");
        if (_cellLockBusy || (_speedBusy && _speedTestManual) ||
            !_settings.TpLinkRouterEnabled ||
            !_engine.GetSnapshot().IsOnline)
        {
            MessageBox.Show(
                "Controlled comparison needs an online baseline, connected TP-Link " +
                "monitoring, and no manual speed test or router change in progress. " +
                "Manual Cell Lock remains available while Internet is offline.",
                "Controlled Cell Experiment",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var candidates = new List<(LteCellRecommendation Recommendation, RouterCellLockTarget Target)>();
        foreach (LteCellRecommendation recommendation in _cellHistory.GetRecommendations()
                     .Where(LteCellHistoryStore.IsVisibleToUser))
        {
            if (TryCreateLockTarget(recommendation, out RouterCellLockTarget? target, out _) &&
                target is not null)
                candidates.Add((recommendation, target));
        }
        if (candidates.Count == 0)
        {
            MessageBox.Show(
                "No lock-ready candidates were found. Run Scan bands & cells first, " +
                "or save a complete PCell profile with CID, EARFCN and PCI.",
                "Controlled Cell Experiment",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        int minutes = _settings.CellExperimentMinutesPerProfile;
        string shortlist = string.Join(Environment.NewLine,
            candidates.Take(12).Select((item, index) =>
                $"{index + 1}. {item.Recommendation.Band} • " +
                $"EARFCN {item.Recommendation.Earfcn} • PCI {item.Recommendation.Pci} • " +
                $"CID {item.Recommendation.CellId}"));
        if (candidates.Count > 12)
            shortlist += $"{Environment.NewLine}…and {candidates.Count - 12} more saved candidates";
        if (MessageBox.Show(
                $"Test all {candidates.Count} lock-ready profiles for approximately " +
                $"{minutes} minutes each ({candidates.Count * minutes} minutes total)?\r\n\r\n" +
                $"{shortlist}\r\n\r\n" +
                "Candidates that are still awaiting normal usage are included. Every result " +
                "is saved in the official-time period in which that test finishes. A failed " +
                "validation is rolled back, and the original router state is restored between " +
                "profiles and when the experiment ends.",
                "Controlled Cell Experiment",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        RouterLockState? originalState = null;
        string originalKey = _settings.LastAutomaticCellLockKey;
        DateTime? originalLockUtc = _settings.LastAutomaticCellLockUtc;
        LteCellRecommendation? winner = null;
        var successfulKeys = new HashSet<string>(StringComparer.Ordinal);
        _experimentCancellation = new CancellationTokenSource();
        CancellationToken token = _experimentCancellation.Token;
        _experimentButton.Text = "Cancel experiment";
        SetLteProfileMutationEnabled(false);
        _experimentButton.Enabled = true;
        _speedButton.Enabled = false;
        try
        {
            using (var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                readTimeout.CancelAfter(TimeSpan.FromSeconds(20));
                originalState = await _routerMonitor.ReadLockStateAsync(readTimeout.Token);
            }
            AddLoggedEvent(new MonitorEvent
            {
                Kind = "LTE EXPERIMENT",
                Message = $"Controlled comparison started for {candidates.Count} profiles"
            });

            foreach (((LteCellRecommendation recommendation, RouterCellLockTarget target), int index)
                     in candidates.Select((item, index) => (item, index)))
            {
                token.ThrowIfCancellationRequested();
                if (index > 0 && originalState is not null &&
                    !await RestoreExperimentBaselineAsync(originalState, token))
                    throw new InvalidOperationException(
                        "The original router state could not be restored before the next candidate.");

                int testPeriod = LteCellHistoryStore.GetTimePeriod(_clock.Now.DateTime);
                _experimentStatus.Text =
                    $"Testing {index + 1}/{candidates.Count}: {recommendation.Band} • " +
                    LteCellHistoryStore.GetTimePeriodLabel(testPeriod);
                CellLockApplyOutcome applyOutcome = await ApplyCellLockWithRollbackAsync(
                    recommendation,
                    target,
                    automatic: false,
                    showResult: false,
                    cancellationToken: token);
                token.ThrowIfCancellationRequested();

                bool stable = (applyOutcome is CellLockApplyOutcome.AlreadyActive or
                                  CellLockApplyOutcome.Validated) &&
                              _engine.GetSnapshot().IsOnline &&
                              MatchesTarget(_routerMonitor.GetSnapshot(), target);
                bool rolledBack = applyOutcome == CellLockApplyOutcome.RolledBack;
                if (!stable)
                {
                    _cellHistory.RecordControlledTest(
                        recommendation.Key,
                        succeeded: false,
                        rolledBack,
                        DateTime.UtcNow);
                    AddLoggedEvent(new MonitorEvent
                    {
                        Kind = "LTE EXPERIMENT",
                        Message = $"{recommendation.Band} • CID {recommendation.CellId} " +
                                  "failed connectivity validation" +
                                  (rolledBack ? " and was rolled back" : "")
                    });
                    RefreshCellHistory(force: true);
                    continue;
                }

                TimeSpan remaining = TimeSpan.FromMinutes(minutes) -
                                     TimeSpan.FromSeconds(_settings.CellLockValidationSeconds);
                if (remaining > TimeSpan.Zero)
                    stable = await ObserveControlledProfileAsync(target, remaining, token);
                if (stable)
                {
                    await RunSpeedTestAsync(
                        manual: false,
                        automaticReason: $"controlled experiment on {recommendation.Band}");
                    stable = _engine.GetSnapshot().IsOnline &&
                             MatchesTarget(_routerMonitor.GetSnapshot(), target);
                }
                token.ThrowIfCancellationRequested();

                if (!stable && originalState is not null)
                    rolledBack = await RestoreExperimentBaselineAsync(originalState, token);
                _cellHistory.RecordControlledTest(
                    recommendation.Key,
                    succeeded: stable,
                    rolledBack,
                    DateTime.UtcNow);
                if (stable)
                    successfulKeys.Add(recommendation.Key);
                AddLoggedEvent(new MonitorEvent
                {
                    Kind = "LTE EXPERIMENT",
                    Message = stable
                        ? $"{recommendation.Band} • CID {recommendation.CellId} passed controlled validation"
                        : $"{recommendation.Band} • CID {recommendation.CellId} failed during observation" +
                          (rolledBack ? " and was rolled back" : "")
                });
                RefreshCellHistory(force: true);
            }

            IReadOnlySet<string> testedKeys = candidates
                .Select(item => item.Recommendation.Key)
                .ToHashSet(StringComparer.Ordinal);
            IReadOnlyList<CellExperimentResult> results = CellExperimentEvaluator.Rank(
                _cellHistory.GetRecommendations()
                    .Where(item => testedKeys.Contains(item.Key) &&
                                   successfulKeys.Contains(item.Key)));
            winner = results.FirstOrDefault()?.Recommendation;
            if (winner is not null)
            {
                AddLoggedEvent(new MonitorEvent
                {
                    Kind = "LTE EXPERIMENT",
                    Message = $"Controlled comparison winner: {winner.Band}, " +
                              $"EARFCN {winner.Earfcn}"
                });
            }
        }
        catch (OperationCanceledException)
        {
            AddLoggedEvent(new MonitorEvent
            {
                Kind = "LTE EXPERIMENT",
                Message = "Controlled comparison cancelled by the user"
            });
        }
        catch (Exception ex)
        {
            AddLoggedEvent(new MonitorEvent
            {
                Kind = "LTE EXPERIMENT ERROR",
                Message = FriendlyUiError(ex)
            });
            MessageBox.Show(
                "The experiment stopped safely.\r\n\r\n" + FriendlyUiError(ex),
                "Controlled Cell Experiment",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            if (originalState is not null)
            {
                try
                {
                    using var restoreTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                    await _routerMonitor.RestoreLockStateAsync(originalState, restoreTimeout.Token);
                    ClearPendingCellLock();
                }
                catch (Exception ex)
                {
                    AddLoggedEvent(new MonitorEvent
                    {
                        Kind = "LTE EXPERIMENT ERROR",
                        Message = "Original router state requires recovery: " +
                                  FriendlyUiError(ex)
                    });
                }
            }
            _settings.LastAutomaticCellLockKey = originalKey;
            _settings.LastAutomaticCellLockUtc = originalLockUtc;
            _settings.Save();
            _experimentCancellation.Dispose();
            _experimentCancellation = null;
            _experimentButton.Text = "Run controlled";
            SetLteProfileMutationEnabled(true);
            _speedButton.Enabled = true;
            _experimentStatus.Text = "Experiment mode is idle";
            RefreshCellHistory(force: true);
        }

        if (winner is not null && MessageBox.Show(
                $"The measured winner is {winner.Band}, EARFCN {winner.Earfcn}. " +
                "The original router state has been restored.\r\n\r\nApply the winner " +
                "now with the normal rollback validation?",
                "Controlled Cell Experiment",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes)
        {
            await ApplyRecommendationAsync(winner, _experimentButton,
                "Controlled Cell Experiment");
        }
    }

    private bool EventMatchesFilters(MonitorEvent evt)
    {
        string selected = _eventFilterInput.SelectedItem?.ToString() ?? "All";
        bool category = selected switch
        {
            "Connectivity" => evt.Kind is "ONLINE" or "OFFLINE" or "OUTAGE" or "IP",
            "LTE" => evt.Kind.Contains("LTE", StringComparison.OrdinalIgnoreCase) ||
                     evt.Kind.Contains("CELL", StringComparison.OrdinalIgnoreCase),
            "Speed tests" => evt.Kind.Contains("SPEED", StringComparison.OrdinalIgnoreCase),
            "SMS" => evt.Kind.Contains("SMS", StringComparison.OrdinalIgnoreCase),
            "System" => evt.Kind is "INFO" or "ERROR" or "DIAGNOSTIC",
            _ => true
        };
        string term = _eventSearchInput.Text.Trim();
        return category && (term.Length == 0 ||
            evt.Kind.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            evt.Message.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private void ShowOperationalNotification(MonitorEvent evt)
    {
        if (!_trayIcon.Visible)
            return;
        _trayIcon.ShowBalloonTip(
            4000,
            evt.Kind.Equals("OFFLINE", StringComparison.OrdinalIgnoreCase)
                ? "Connection lost"
                : "Connection restored",
            evt.Message,
            evt.Kind.Equals("OFFLINE", StringComparison.OrdinalIgnoreCase)
                ? ToolTipIcon.Warning
                : ToolTipIcon.Info);
    }

    private void LoadSettingsIntoControls()
    {
        _targetInput.Text = _settings.PingTarget;
        _pingIntervalInput.Value = _settings.PingIntervalSeconds;
        _failureInput.Value = _settings.FailuresForOutage;
        _speedIntervalInput.Value = _settings.SpeedTestIntervalMinutes;
        _startupInput.Checked = _settings.StartWithWindows;
        _trayInput.Checked = _settings.MinimizeToTray;
        _routerEnabledInput.Checked = _settings.TpLinkRouterEnabled;
        _automaticCellLockInput.Checked = _settings.AutomaticCellLockEnabled;
        _themeInput.SelectedItem = _settings.Theme;
        _dashboardLayoutInput.SelectedItem = _settings.DashboardLayout;
        _healthSummaryInput.Checked = _settings.ShowHealthSummary;
        _smartRecommendationInput.Checked = _settings.ShowSmartRecommendation;
        _updateCheckInput.Checked = _settings.CheckForUpdates;
        _experimentMinutesInput.Value = _settings.CellExperimentMinutesPerProfile;
        _routerAddressInput.Text = _settings.TpLinkRouterAddress;
        RefreshCellHistory(force: true);
        _routerSetupButton.Text = string.IsNullOrWhiteSpace(_routerPassword)
            ? "Add protected password..."
            : "Update protected password...";
        _regionalSetupButton.Text =
            $"{_clock.CountryName} ({_clock.CountryCode}) • {_clock.TimeZone.Id}";
        _connectionViewInput.SelectedIndex = _settings.ConnectionDetailsView switch
        {
            "Dsl" => 1,
            "Fiber" => 2,
            _ => 0
        };
        ApplyAppearanceSettings();
    }

    private void ApplyAppearanceSettings()
    {
        NetPulseTheme theme = Enum.TryParse(_settings.Theme, out NetPulseTheme parsed)
            ? parsed
            : NetPulseTheme.System;
        AppThemeManager.Apply(this, theme);
        RefreshThemeDependentContent();
        ApplyDashboardLayout();
        RefreshUpdateStatus();
    }

    private bool IsDarkThemeActive()
    {
        NetPulseTheme theme = Enum.TryParse(
                _themeInput.SelectedItem?.ToString(),
                out NetPulseTheme selected)
            ? selected
            : Enum.TryParse(_settings.Theme, out NetPulseTheme saved)
                ? saved
                : NetPulseTheme.System;
        return AppThemeManager.IsDark(theme);
    }

    private NetPulseTheme CurrentTheme() => Enum.TryParse(
            _themeInput.SelectedItem?.ToString(),
            out NetPulseTheme selected)
        ? selected
        : Enum.TryParse(_settings.Theme, out NetPulseTheme saved)
            ? saved
            : NetPulseTheme.System;

    private void RefreshThemeDependentContent()
    {
        RefreshCellHistory(force: true);
        if (_smsGrid.IsHandleCreated)
            PopulateSmsGrid(_selectedSmsMessage?.Identity);
    }

    private void ApplyDashboardLayout()
    {
        string layout = _dashboardLayoutInput.SelectedItem?.ToString()
                        ?? _settings.DashboardLayout;
        bool accessIsLte = IsLteConnectionView();
        bool simple = accessIsLte && layout is "LTE Simple" or "Simple";
        bool lte = accessIsLte && layout == "LTE Advanced";
        bool troubleshooting = layout == "ISP troubleshooting";
        _healthCard.Visible = _healthSummaryInput.Checked ||
                              (!IsHandleCreated && _settings.ShowHealthSummary);
        _smartCard.Visible = accessIsLte && (lte || simple) &&
                             (_smartRecommendationInput.Checked ||
                              (!IsHandleCreated && _settings.ShowSmartRecommendation));
        _updatesCard.Visible = true;
        if (_dashboardExperienceGrid.ColumnStyles.Count == 3)
        {
            _dashboardExperienceGrid.ColumnStyles[0].Width =
                _smartCard.Visible ? 27F : 50F;
            _dashboardExperienceGrid.ColumnStyles[1].Width =
                _smartCard.Visible ? 42F : 0F;
            _dashboardExperienceGrid.ColumnStyles[2].Width =
                _smartCard.Visible ? 31F : 50F;
        }

        ConfigureDashboardMetricGrid(simple);

        if (troubleshooting)
            _diagnosticsSummary.Text =
                "ISP view: gateway, DNS, IP, latency, loss and outages stay visible live.";
    }

    private void ConfigureDashboardMetricGrid(bool simple)
    {
        bool lte = IsLteConnectionView();
        int columns = simple ? 3 : 4;
        const int rows = 8;

        _dashboardMetricGrid.SuspendLayout();
        try
        {
            _dashboardMetricGrid.Controls.Clear();
            _dashboardMetricGrid.ColumnStyles.Clear();
            _dashboardMetricGrid.RowStyles.Clear();
            _dashboardMetricGrid.ColumnCount = columns;
            _dashboardMetricGrid.RowCount = rows;
            for (int column = 0; column < columns; column++)
                _dashboardMetricGrid.ColumnStyles.Add(
                    new ColumnStyle(SizeType.Percent, 100F / columns));
            for (int row = 0; row < rows; row++)
            {
                bool sectionHeader = row is 0 or 3 or 6;
                _dashboardMetricGrid.RowStyles.Add(sectionHeader
                    ? new RowStyle(SizeType.Absolute, 23F)
                    : new RowStyle(SizeType.Percent, 20F));
            }

            foreach (Panel card in _metricCards.Values)
                card.Visible = false;

            AddDashboardSectionHeader(
                "CURRENT CONNECTION",
                lte
                    ? "Router LTE identity plus PC-to-Internet measurements. Hover each value to see its source."
                    : $"{GetAccessTechnologyLabel()} identity plus PC-to-Internet measurements. Hover each value to see its source.",
                0,
                columns);
            AddDashboardSectionHeader(
                "SESSION SINCE OPEN / RESET",
                "PC-to-Internet averages and totals accumulated since NetPulse opened or Reset session was pressed.",
                3,
                columns);
            AddDashboardSectionHeader(
                "LAST PC SPEED TEST",
                "External test run by this PC; these results are not router telemetry or live traffic throughput.",
                6,
                columns);

            void Place(string key, int column, int row)
            {
                Panel card = _metricCards[key];
                card.Visible = true;
                _dashboardMetricGrid.Controls.Add(card, column, row);
            }

            if (simple)
            {
                Place(lte ? "CurrentLteSet" : "AccessType", 0, 1);
                Place("CurrentIp", 1, 1);
                Place("ConnectionStable", 2, 1);
                Place("Ping", 0, 2);
                Place("Loss", 1, 2);
                Place("ConnectionOutages", 2, 2);

                Place("SessionAveragePing", 0, 4);
                Place("SessionAverageJitter", 1, 4);
                Place("SessionAverageLoss", 2, 4);
                Place("SuccessFail", 0, 5);
                Place("RunTime", 1, 5);
                Place("Availability", 2, 5);

                Place("Download", 0, 7);
                Place("Upload", 1, 7);
                Place("SpeedPing", 2, 7);
            }
            else
            {
                Place(lte ? "CurrentLteSet" : "AccessType", 0, 1);
                Place("CurrentIp", 1, 1);
                Place("ConnectionStable", 2, 1);
                Place("ConnectionOutages", 3, 1);
                Place("Ping", 0, 2);
                Place("Jitter", 1, 2);
                Place("Loss", 2, 2);

                Place("SessionAveragePing", 0, 4);
                Place("SessionAverageJitter", 1, 4);
                Place("SessionAverageLoss", 2, 4);
                Place("RunTime", 3, 4);
                Place("SuccessFail", 0, 5);
                Place("Downtime", 1, 5);
                Place("Availability", 2, 5);
                Place("Outages", 3, 5);

                Place("Download", 0, 7);
                Place("Upload", 1, 7);
                Place("SpeedPing", 2, 7);
                Place("SpeedLoss", 3, 7);
            }
        }
        finally
        {
            _dashboardMetricGrid.ResumeLayout(performLayout: true);
        }
    }

    private static string CurrentApplicationVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "Unknown";

    private void RefreshUpdateStatus()
    {
        _updateStatus.Text =
            $"Version {CurrentApplicationVersion} • © 2026 CosmicOlorin";
    }

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        _updateButton.Enabled = false;
        _updateButton.Text = "Checking...";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            UpdateCheckResult result = await UpdateChecker.CheckAsync(timeout.Token);
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _settings.Save();
            if (result.UpdateAvailable)
            {
                _availableUpdate = result;
                _updateButton.Text = $"Install {result.LatestVersion}";
                _updateStatus.Text = result.Message + Environment.NewLine +
                    $"Source: {result.Source}. Installation keeps this executable path and Windows identity.";
                if (interactive)
                    await InstallAvailableUpdateAsync(result);
            }
            else
            {
                _availableUpdate = null;
                _updateButton.Text = "Check for updates";
                RefreshUpdateStatus();
                if (interactive)
                    MessageBox.Show(result.Message, "NetPulse update",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            _updateButton.Text = "Check for updates";
            if (interactive)
            {
                MessageBox.Show(
                    "The update check could not complete. No file was downloaded.\r\n\r\n" +
                    FriendlyUiError(ex),
                    "NetPulse update",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _updateButton.Enabled = true;
        }
    }

    private async Task InstallAvailableUpdateAsync(UpdateCheckResult update)
    {
        DialogResult answer = MessageBox.Show(
            $"Download and install NetPulse {update.LatestVersion} now?\r\n\r\n" +
            "NetPulse will close, replace the application at the same path, and restart automatically. Your settings, taskbar pin, startup entry, and tray identity stay unchanged.",
            "Install NetPulse update",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);
        if (answer != DialogResult.Yes)
            return;

        _updateButton.Enabled = false;
        _updateButton.Text = "Downloading 0%";
        _updateStatus.Text = $"Preparing NetPulse {update.LatestVersion}…";
        try
        {
            var progress = new Progress<int>(percent =>
            {
                _updateButton.Text = $"Downloading {percent}%";
                _updateStatus.Text =
                    $"Downloading and verifying NetPulse {update.LatestVersion}… {percent}%";
            });
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
            await ApplicationUpdater.StageAndLaunchAsync(update, progress, timeout.Token);
            _updateStatus.Text = "Update verified. Restarting NetPulse at the same path…";
            _allowExit = true;
            _trayIcon.Visible = false;
            Close();
        }
        catch (Exception ex)
        {
            _updateButton.Enabled = true;
            _updateButton.Text = $"Install {update.LatestVersion}";
            _updateStatus.Text = "The update was not installed; the current version is unchanged.";
            MessageBox.Show(
                FriendlyUiError(ex),
                "NetPulse update",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ShowTroubleshootingAssessment(TroubleshootingAssessment assessment)
    {
        string findings = string.Join(Environment.NewLine,
            assessment.Findings.Select(item => "• " + item));
        string actions = string.Join(Environment.NewLine,
            assessment.Actions.Select(item => "• " + item));
        _troubleshootingSummary.Text =
            $"{assessment.Headline}\r\n{assessment.Findings[0]}";
        AddLoggedEvent(new MonitorEvent
        {
            Kind = "DIAGNOSTIC",
            Message = assessment.Headline
        });
        MessageBox.Show(
            assessment.Headline + "\r\n\r\nFindings\r\n" + findings +
            "\r\n\r\nSuggested next steps\r\n" + actions,
            "Guided troubleshooting",
            MessageBoxButtons.OK,
            assessment.Severity == "Critical"
                ? MessageBoxIcon.Warning
                : MessageBoxIcon.Information);
    }

    private void AddLoggedEvent(MonitorEvent evt)
    {
        _logger.LogEvent(evt);
        AddEventToGrid(evt);
    }

    private async Task SaveSettingsAsync()
    {
        bool automaticLockRequested = _automaticCellLockInput.Checked;
        if (automaticLockRequested && !_routerEnabledInput.Checked)
        {
            MessageBox.Show(
                "Enable TP-Link router live monitoring before automatic Cell Lock.",
                "Automatic Cell Lock",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            automaticLockRequested = false;
            _automaticCellLockInput.Checked = false;
        }
        if (automaticLockRequested && !_settings.AutomaticCellLockEnabled)
        {
            DialogResult answer = MessageBox.Show(
                "Automatic Cell Lock can briefly interrupt mobile service. NetPulse will " +
                "use only medium/high-confidence time-of-day history, wait 90 seconds after a change, " +
                "and restore the previous band and cell settings if internet or LTE does " +
                "not recover. Keep NetPulse running during validation.\r\n\r\nEnable it?",
                "Enable automatic Cell Lock",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                automaticLockRequested = false;
                _automaticCellLockInput.Checked = false;
            }
        }

        _settings.PingTarget = _targetInput.Text;
        _settings.PingIntervalSeconds = (int)_pingIntervalInput.Value;
        _settings.FailuresForOutage = (int)_failureInput.Value;
        _settings.SpeedTestIntervalMinutes = (int)_speedIntervalInput.Value;
        _settings.DownloadSampleMegabytes = 20;
        _settings.UploadSampleMegabytes = 5;
        _settings.StartWithWindows = _startupInput.Checked;
        _settings.MinimizeToTray = _trayInput.Checked;
        _settings.TpLinkRouterEnabled = _routerEnabledInput.Checked;
        _settings.TpLinkRouterAddress = _routerAddressInput.Text;
        _settings.AutomaticCellLockEnabled = automaticLockRequested;
        _settings.Theme = _themeInput.SelectedItem?.ToString() ?? "System";
        _settings.DashboardLayout =
            _dashboardLayoutInput.SelectedItem?.ToString() ?? "LTE Advanced";
        _settings.ShowHealthSummary = _healthSummaryInput.Checked;
        _settings.ShowSmartRecommendation = _smartRecommendationInput.Checked;
        _settings.CheckForUpdates = _updateCheckInput.Checked;
        _settings.CellExperimentMinutesPerProfile = (int)_experimentMinutesInput.Value;
        _settings.RouterSetupCompleted = true;
        _settings.Normalize();
        _settings.Save();

        try
        {
            StartupManager.SetEnabled(_settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Settings were saved, but Windows startup could not be changed.\r\n\r\n" +
                ex.Message,
                "Startup setting",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        _engine.UpdateSettings(_settings);
        _nextAutomaticSpeedTest = GetNextSpeedTime();
        _routerAddressInput.Text = _settings.TpLinkRouterAddress;
        RefreshCompanionButtonText();
        ApplyAppearanceSettings();

        using (var restartTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
        {
            try
            {
                await _routerMonitor.RestartAsync(
                    _settings,
                    _routerPassword,
                    restartTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                RefreshRouterDashboard(new RouterTelemetry
                {
                    Status = "Restart pending",
                    Error = "The previous router request is still finishing. Reconnect in a moment."
                });
            }
        }

        MessageBox.Show(
            "Settings saved.",
            "NetPulse Monitor",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private string ReadProtectedRouterPassword()
    {
        if (!_settings.RememberTpLinkPassword)
            return "";
        try
        {
            return WindowsCredentialStore.ReadPassword() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private void ConfigureRegionalSettings(bool firstRun)
    {
        using var dialog = new RegionalSetupForm(_settings, firstRun, CurrentTheme());
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _settings.CountryCode = dialog.CountryCode;
        _settings.CountryCultureName = dialog.CountryCultureName;
        _settings.OfficialTimeZoneId = dialog.OfficialTimeZoneId;
        _settings.RegionalSetupCompleted = true;
        _settings.Normalize();
        _settings.Save();

        _clock = new OfficialClock(_settings);
        _logger.SetOfficialClock(_clock);
        _cellHistory.SetOfficialTimeZone(_clock.TimeZone);
        LoadSettingsIntoControls();
        RefreshDashboard();
        RefreshRouterDashboard(_routerMonitor.GetSnapshot());
        RefreshCellHistory(force: true);

        if (!firstRun)
        {
            MessageBox.Show(
                "Country and official time settings were saved. Future UI, CSV and " +
                "ISP-evidence timestamps use this selection; the Windows clock was not changed.",
                "Official timestamps",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private async Task ConfigureRouterAsync(bool firstRun)
    {
        using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        try
        {
            await _routerMonitor.StopAsync(stopTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show(
                "NetPulse is still releasing the previous TP-Link session. " +
                "Wait a few seconds, then open router setup again.",
                "TP-Link setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        RefreshRouterDashboard(new RouterTelemetry
        {
            Status = "Setup open",
            Error = "Live telemetry is paused while the TP-Link connection is tested."
        });

        using var dialog = new RouterSetupForm(
            _settings, _routerPassword, firstRun, CurrentTheme());
        DialogResult result = dialog.ShowDialog(this);
        if (result != DialogResult.OK)
        {
            if (firstRun)
            {
                _settings.RouterSetupCompleted = true;
                _settings.TpLinkRouterEnabled = false;
                _settings.AutomaticCellLockEnabled = false;
                _settings.ConnectionDetailsView = "Fiber";
                _settings.Save();
                LoadSettingsIntoControls();
            }
            await RestartRouterAfterSetupAsync();
            return;
        }

        string password = dialog.Password;
        _settings.TpLinkRouterEnabled = dialog.MonitoringEnabled;
        if (!_settings.TpLinkRouterEnabled)
            _settings.AutomaticCellLockEnabled = false;
        _settings.TpLinkRouterAddress = dialog.RouterAddress;
        _settings.RememberTpLinkPassword = dialog.RememberPassword;
        _settings.RouterSetupCompleted = true;
        _settings.ConnectionDetailsView = _settings.TpLinkRouterEnabled
            ? "Lte"
            : _settings.ConnectionDetailsView == "Lte"
                ? "Fiber"
                : _settings.ConnectionDetailsView;
        _settings.Normalize();

        try
        {
            if (_settings.RememberTpLinkPassword && _settings.TpLinkRouterEnabled)
                WindowsCredentialStore.SavePassword(password);
            else
                WindowsCredentialStore.DeletePassword();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "The router settings were saved, but Windows could not protect the password. " +
                "It will be used only until NetPulse Monitor closes.\r\n\r\n" + ex.Message,
                "Protected password",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _settings.RememberTpLinkPassword = false;
        }

        _routerPassword = _settings.TpLinkRouterEnabled ? password : "";
        _settings.Save();
        LoadSettingsIntoControls();

        await RestartRouterAfterSetupAsync();
    }

    private async Task RestartRouterAfterSetupAsync()
    {
        using var restartTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        try
        {
            await _routerMonitor.RestartAsync(
                _settings,
                _routerPassword,
                restartTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            RefreshRouterDashboard(new RouterTelemetry
            {
                Status = "Restart pending",
                Error = "The previous router request is still finishing. Reconnect in a moment."
            });
        }
    }

    private static string DisplayValue(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim() == "-" ? "" : value;

    private static string FormatMeasurement(double? value, string unit) =>
        value.HasValue
            ? value.Value.ToString("0.#", CultureInfo.CurrentCulture) + " " + unit
            : "";

    private static string FormatBytes(long? bytes)
    {
        if (!bytes.HasValue || bytes.Value < 0)
            return "";
        return FormatScaled(bytes.Value, "B");
    }

    private static string FormatRate(long? bytesPerSecond)
    {
        if (!bytesPerSecond.HasValue || bytesPerSecond.Value < 0)
            return "";
        return FormatScaled(bytesPerSecond.Value, "B/s");
    }

    private static string FormatLinkSpeed(long? bitsPerSecond)
    {
        if (!bitsPerSecond.HasValue || bitsPerSecond.Value <= 0)
            return "";
        double megabits = bitsPerSecond.Value / 1_000_000D;
        return megabits >= 1000
            ? (megabits / 1000D).ToString("0.##", CultureInfo.CurrentCulture) + " Gbps"
            : megabits.ToString("0.##", CultureInfo.CurrentCulture) + " Mbps";
    }

    private static string FormatMbps(double? value) =>
        value.HasValue
            ? value.Value.ToString("0.00", CultureInfo.CurrentCulture) + " Mbps"
            : "-";

    private static string FormatHistoryPing(double? value) =>
        value.HasValue
            ? value.Value.ToString("0.#", CultureInfo.CurrentCulture) + " ms"
            : "-";

    private static string FormatEstimatedCellLoad(double? value) =>
        value.HasValue
            ? value.Value.ToString("0", CultureInfo.CurrentCulture) + "% est."
            : "-";

    private static string FormatCompactDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
            return $"{(int)duration.TotalDays}d {duration.Hours:00}h";
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes:00}m";
        return $"{Math.Max(0, duration.Minutes)}m {duration.Seconds:00}s";
    }

    private static bool IsSameLteCell(
        RouterTelemetry started,
        RouterTelemetry completed)
    {
        if (!started.IsConnected || !completed.IsConnected)
            return false;
        if (!string.Equals(started.Band, completed.Band, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(started.Earfcn, completed.Earfcn, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(started.Pci, completed.Pci, StringComparison.OrdinalIgnoreCase))
            return false;

        bool bothHaveCellId = !string.IsNullOrWhiteSpace(started.CellId) &&
                              started.CellId != "-" &&
                              !string.IsNullOrWhiteSpace(completed.CellId) &&
                              completed.CellId != "-";
        return !bothHaveCellId ||
               string.Equals(
                   started.CellId,
                   completed.CellId,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatScaled(long value, string baseUnit)
    {
        string[] prefixes = ["", "K", "M", "G", "T"];
        double scaled = value;
        int index = 0;
        while (scaled >= 1024 && index < prefixes.Length - 1)
        {
            scaled /= 1024;
            index++;
        }
        string number = index == 0
            ? scaled.ToString("0", CultureInfo.CurrentCulture)
            : scaled.ToString("0.##", CultureInfo.CurrentCulture);
        return number + " " + prefixes[index] + baseUnit;
    }

    private void ConfigureTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Pause / Resume", null, (_, _) =>
        {
            bool pause = !_engine.IsPaused;
            _engine.SetPaused(pause);
            _pauseButton.Text = pause ? "Resume monitoring" : "Pause monitoring";
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _allowExit = true;
            Close();
        });

        _trayIcon.Icon = Icon;
        _trayIcon.Text = "NetPulse Monitor";
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.Visible = true;
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        _trayIcon.BalloonTipClicked += (_, _) =>
            ShowSmsTab(_activeSmsNotificationIdentity);
        _smsNotificationTimer.Interval = 6500;
        _smsNotificationTimer.Tick += (_, _) => ShowNextSmsNotification();
    }

    private void HandleUnreadSmsCount(int? unreadCount)
    {
        if (!unreadCount.HasValue)
            return;
        int count = unreadCount.Value;
        bool increased = _lastRouterUnreadSmsCount >= 0 &&
                         count > _lastRouterUnreadSmsCount;
        _lastRouterUnreadSmsCount = count;
        if (_tabs.SelectedTab?.Text == "SMS" && increased)
            _ = RefreshSmsTimelineAsync(showErrors: false);
    }

    private void QueueUnreadSmsNotifications(
        IReadOnlyList<RouterSmsMessage> messages)
    {
        IReadOnlyList<string> newIdentities = _unreadSmsAlerts.FindNew(
            messages
                .Where(message =>
                    message.Folder == RouterSmsFolder.Inbox && message.IsUnread)
                .OrderBy(message => message.Timestamp ?? DateTime.MinValue)
                .Select(message => message.Identity));
        foreach (string identity in newIdentities)
            _smsNotificationQueue.Enqueue(identity);
        if (!_smsNotificationTimer.Enabled)
            ShowNextSmsNotification();
    }

    private void ShowNextSmsNotification()
    {
        while (_smsNotificationQueue.Count > 0)
        {
            string identity = _smsNotificationQueue.Dequeue();
            if (!_smsMessages.Any(message =>
                    message.Identity == identity && message.IsUnread))
                continue;

            _activeSmsNotificationIdentity = identity;
            _trayIcon.ShowBalloonTip(
                5000,
                "Unread SIM message",
                "You have an unread SIM message. Click to open it in NetPulse.",
                ToolTipIcon.Info);
            _smsNotificationTimer.Start();
            return;
        }

        _activeSmsNotificationIdentity = null;
        _smsNotificationTimer.Stop();
    }

    private void CheckAutomaticSmsRefresh()
    {
        if (_smsBusy || DateTime.UtcNow < _nextAutomaticSmsRefreshUtc ||
            !_settings.TpLinkRouterEnabled ||
            !_routerMonitor.GetSnapshot().IsConnected)
            return;

        // Set the next due time before starting so a slow router cannot queue
        // overlapping refresh operations from the one-second UI timer.
        _nextAutomaticSmsRefreshUtc = DateTime.UtcNow.AddMinutes(30);
        _ = RefreshSmsTimelineAsync(showErrors: false);
    }

    private void CheckAutomaticDiagnostics()
    {
        if (_diagnosticsBusy || DateTime.UtcNow < _nextAutomaticDiagnosticsUtc)
            return;

        // Gateway and DNS checks are deliberately slower than the main ping loop.
        // They run asynchronously and never queue more than one operation.
        _nextAutomaticDiagnosticsUtc = DateTime.UtcNow.AddSeconds(30);
        _ = RefreshDiagnosticsAsync(showErrors: false);
    }

    private async Task RefreshDiagnosticsAsync(bool showErrors)
    {
        if (_diagnosticsBusy)
            return;
        _diagnosticsBusy = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            DiagnosticResult result = await NetworkDiagnostics.RunAsync(cts.Token);
            if (IsDisposed)
                return;
            _lastDiagnosticResult = result;
            _gatewayValue.Text = result.Gateway;
            _gatewayPingValue.Text = result.GatewayPing;
            _dnsValue.Text = result.DnsLookup;
            _ipv4Value.Text = result.IPv4;
            _ipv6Value.Text = result.IPv6;
            _diagnosticsSummary.Text =
                $"Gateway {result.Gateway} ({CompactDiagnosticValue(result.GatewayPing)})   •   " +
                $"DNS {CompactDiagnosticValue(result.DnsLookup)}   •   " +
                $"IPv4 {FormatAvailability(result.IPv4)}   •   " +
                $"IPv6 {FormatAvailability(result.IPv6)}   •   updates every 30 s";
            _buttonTips.SetToolTip(
                _diagnosticsSummary,
                $"Local router gateway: {result.Gateway} ({result.GatewayPing}). " +
                $"DNS lookup: {result.DnsLookup}. IPv4: {result.IPv4}. IPv6: {result.IPv6}. " +
                "These are local-network reachability checks, not an LTE route trace.");
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
                _diagnosticsSummary.Text = "Local network checks temporarily unavailable; retrying automatically.";
            if (showErrors && !IsDisposed)
            {
                MessageBox.Show(ex.Message, "Diagnostics",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _nextAutomaticDiagnosticsUtc = DateTime.UtcNow.AddSeconds(30);
            _diagnosticsBusy = false;
        }
    }

    private static string CompactDiagnosticValue(string value) =>
        value.Length <= 22 ? value : "Unavailable";

    private static string FormatAvailability(string value) =>
        string.Equals(value, "Available", StringComparison.OrdinalIgnoreCase)
            ? "available"
            : "not detected";

    private void ShowSmsTab(string? messageIdentity = null)
    {
        RestoreFromTray();
        TabPage? sms = _tabs.TabPages
            .Cast<TabPage>()
            .FirstOrDefault(page => page.Text == "SMS");
        if (sms is not null)
            _tabs.SelectedTab = sms;
        if (string.IsNullOrWhiteSpace(messageIdentity))
            return;

        RouterSmsMessage? target = _smsMessages.FirstOrDefault(message =>
            string.Equals(message.Identity, messageIdentity, StringComparison.Ordinal));
        if (target is not null)
        {
            if (_smsViewInput.SelectedIndex != SmsConversationsView)
                _smsViewInput.SelectedIndex = SmsConversationsView;
            _activeSmsConversationAddress =
                SmsConversationBuilder.NormalizeAddress(
                    target.Address,
                    _settings.CountryCode);
            _selectedSmsMessage = target;
            PopulateSmsGrid(messageIdentity);
            ShowSmsConversation(target.Address, messageIdentity);
            return;
        }

        DataGridViewRow? row = _smsGrid.Rows
            .Cast<DataGridViewRow>()
            .FirstOrDefault(candidate =>
                candidate.Tag is RouterSmsMessage message &&
                message.Identity == messageIdentity);
        if (row is null)
            return;
        _smsGrid.ClearSelection();
        _smsGrid.CurrentCell = row.Cells[0];
        row.Selected = true;
        _smsGrid.FirstDisplayedScrollingRowIndex = row.Index;
    }

    private CompanionSnapshot CreateCompanionSnapshot()
    {
        MonitorSnapshot internet = _engine.GetSnapshot();
        RouterTelemetry router = _routerMonitor.GetSnapshot();
        return new CompanionSnapshot(
            DateTime.UtcNow,
            internet.IsOnline,
            internet.IsPaused,
            internet.CurrentPingMs.HasValue
                ? (int)Math.Clamp(internet.CurrentPingMs.Value, int.MinValue, int.MaxValue)
                : null,
            internet.JitterMs,
            internet.PacketLossPercent,
            internet.AvailabilityPercent,
            internet.Outages,
            RouterManagementLabel(_routerMonitor.GetManagementState()),
            router.IsConnected,
            router.NetworkType,
            router.Band,
            router.PrimaryBand,
            router.Earfcn,
            router.Pci,
            router.CellId,
            router.RsrpDbm,
            router.RsrqDb,
            router.SnrDb,
            router.UploadBytesPerSecond,
            router.DownloadBytesPerSecond,
            router.UnreadSmsCount);
    }

    private string GetOrCreateCompanionSecret()
    {
        string? secret = WindowsCredentialStore.ReadCompanionSecret();
        if (!string.IsNullOrWhiteSpace(secret))
            return secret;
        secret = CompanionService.CreatePairingSecret();
        WindowsCredentialStore.SaveCompanionSecret(secret);
        return secret;
    }

    private async Task ConfigureCompanionAsync()
    {
        string secret;
        try
        {
            secret = GetOrCreateCompanionSecret();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Windows could not open the protected mobile pairing key.\r\n\r\n" + ex.Message,
                "Mobile Companion",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new CompanionSetupForm(_settings, secret, CurrentTheme());
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            WindowsCredentialStore.SaveCompanionSecret(dialog.PairingSecret);
            _settings.CompanionEnabled = dialog.CompanionEnabled;
            _settings.CompanionPort = dialog.CompanionPort;
            _settings.Normalize();
            _settings.Save();
            await RestartCompanionServiceAsync(showErrors: true);
            RefreshCompanionButtonText();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "The mobile companion setting could not be applied.\r\n\r\n" + ex.Message,
                "Mobile Companion",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async Task RestartCompanionServiceAsync(bool showErrors)
    {
        await _companionService.StopAsync();
        if (!_settings.CompanionEnabled)
            return;
        try
        {
            _companionService.Start(_settings.CompanionPort, GetOrCreateCompanionSecret());
            AddLoggedEvent(new MonitorEvent
            {
                Kind = "COMPANION",
                Message = $"Mobile companion listening on the local network at port {_settings.CompanionPort}"
            });
        }
        catch (Exception ex)
        {
            AddLoggedEvent(new MonitorEvent
            {
                Kind = "COMPANION",
                Message = "Mobile companion could not start: " + ex.Message
            });
            if (showErrors)
                throw;
        }
    }

    private void RefreshCompanionButtonText() =>
        _companionSetupButton.Text = _settings.CompanionEnabled
            ? $"Enabled on LAN port {_settings.CompanionPort}..."
            : "Configure persistent phone pairing...";

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;

        if (!_trayHintShown)
        {
            _trayIcon.ShowBalloonTip(
                2000,
                "NetPulse Monitor",
                "Monitoring continues in the system tray.",
                ToolTipIcon.Info);
            _trayHintShown = true;
        }
    }

    internal void RestoreFromExternalLaunch()
    {
        if (IsDisposed)
            return;
        if (!IsHandleCreated)
        {
            _externalActivationPending = true;
            return;
        }
        if (InvokeRequired)
        {
            if (IsHandleCreated)
                BeginInvoke(RestoreFromExternalLaunch);
            return;
        }
        RestoreFromTray();
    }

    private async Task<bool> ObserveControlledProfileAsync(
        RouterCellLockTarget target,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        DateTime endUtc = DateTime.UtcNow + duration;
        int consecutiveFailures = 0;
        while (DateTime.UtcNow < endUtc)
        {
            TimeSpan wait = endUtc - DateTime.UtcNow;
            if (wait <= TimeSpan.Zero)
                break;
            await Task.Delay(
                wait > TimeSpan.FromSeconds(2) ? TimeSpan.FromSeconds(2) : wait,
                cancellationToken);
            bool valid = _engine.GetSnapshot().IsOnline &&
                         MatchesTarget(_routerMonitor.GetSnapshot(), target);
            consecutiveFailures = valid ? 0 : consecutiveFailures + 1;
            if (consecutiveFailures >= 3)
                return false;
        }
        return true;
    }

    private async Task<bool> RestoreExperimentBaselineAsync(
        RouterLockState originalState,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            await _routerMonitor.RestoreLockStateAsync(originalState, timeout.Token);
            ClearPendingCellLock();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AddLoggedEvent(new MonitorEvent
            {
                Kind = "LTE EXPERIMENT ERROR",
                Message = "Could not restore the controlled-test baseline: " +
                          FriendlyUiError(ex)
            });
            return false;
        }
    }

    private void AddDashboardSectionHeader(
        string text,
        string tooltip,
        int row,
        int columns)
    {
        var header = new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = false,
            Margin = new Padding(7, 1, 5, 0),
            Padding = Padding.Empty,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            ForeColor = IsDarkThemeActive()
                ? Color.FromArgb(112, 190, 237)
                : Color.FromArgb(27, 96, 145)
        };
        _buttonTips.SetToolTip(header, tooltip);
        _dashboardMetricGrid.Controls.Add(header, 0, row);
        _dashboardMetricGrid.SetColumnSpan(header, columns);
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
        // Windows can refuse foreground activation after another process was
        // launched. Briefly toggling TopMost reliably surfaces the existing
        // window without leaving it above other applications.
        TopMost = true;
        TopMost = false;
        Focus();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_allowExit && _settings.MinimizeToTray &&
            e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        if (_bandDiscoveryCancellation is not null)
        {
            e.Cancel = true;
            if (!_bandDiscoveryExitPending)
            {
                _bandDiscoveryExitPending = true;
                _bandDiscoveryCancellation.Cancel();
                _ = CloseAfterBandDiscoveryRestoreAsync();
            }
            return;
        }

        _allowExit = true;
        _uiTimer.Stop();
        _smsNotificationTimer.Stop();
        _smsNotificationTimer.Dispose();
        _speedCancellation?.Cancel();
        _smsSendCancellation?.Cancel();
        _experimentCancellation?.Cancel();
        _bandDiscoveryCancellation?.Cancel();
        _engine.Dispose();
        _routerMonitor.Dispose();
        try
        {
            _companionService.StopAsync().Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
        _cellHistory.Dispose();
        _buttonTips.Dispose();
        _smsUnreadFont.Dispose();
        _cellGroupFont.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }

    private async Task CloseAfterBandDiscoveryRestoreAsync()
    {
        try
        {
            if (_bandDiscoveryTask is not null)
                await _bandDiscoveryTask;
        }
        finally
        {
            if (!IsDisposed && IsHandleCreated)
            {
                _allowExit = true;
                BeginInvoke(new Action(Close));
            }
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return "";
        if (duration.TotalDays >= 1)
            return $"{(int)duration.TotalDays} d {duration.Hours} h {duration.Minutes} min";
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours} h {duration.Minutes} min {duration.Seconds} s";
        if (duration.TotalMinutes >= 1)
            return $"{(int)duration.TotalMinutes} min {duration.Seconds} s";
        return $"{Math.Max(1, (int)duration.TotalSeconds)} s";
    }

    private sealed record SmsConversationRow(string Address);

    private enum CellHistoryRowStyle
    {
        Eligible,
        Ineligible,
        UserAdded,
        Active,
        Recommended,
        Group
    }

    private enum CellLockApplyOutcome
    {
        NotApplied,
        AlreadyActive,
        Validated,
        AppliedWithoutOnlineValidation,
        RolledBack,
        RecoveryPending
    }

    private sealed record CellHistoryDisplayRow(
        string StructureKey,
        object Tag,
        object?[] Values,
        CellHistoryRowStyle Style,
        double? TestFailureRatePercent);

    private sealed record ObservedCellLockOption(
        string Label,
        LteCellRecommendation? Profile)
    {
        public override string ToString() => Label;
    }
    private sealed record CellHistoryScrollAnchor(
        int RowIndex,
        string? RecommendationKey);
}
