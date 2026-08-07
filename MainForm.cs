using System.Diagnostics;

using System.Globalization;
using System.Text.RegularExpressions;

namespace NetPulseMonitor;

internal sealed class MainForm : Form
{
    private readonly CsvLogger _logger = new();
    private readonly LteCellHistoryStore _cellHistory = new();
    private AppSettings _settings = AppSettings.Load();
    private MonitorEngine _engine;
    private RouterMonitor _routerMonitor;
    private string _routerPassword = "";

    private readonly Dictionary<string, Label> _metrics = new();
    private readonly Dictionary<string, Label> _routerMetrics = new();
    private readonly Dictionary<string, Label> _routerMetricCaptions = new();
    private readonly PingChartControl _chart = new();
    private readonly DataGridView _eventsGrid = new();
    private readonly DataGridView _cellHistoryGrid = new();
    private readonly DataGridView _smsGrid = new();
    private readonly TabControl _tabs = new();
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
    private readonly AutoFitLabel _routerConnectionState = new();
    private readonly Label _routerDetails = new();
    private readonly ComboBox _connectionViewInput = new();
    private readonly ComboBox _localLinkInput = new();
    private readonly TextBox _manualBandsInput = new();
    private readonly TextBox _manualEarfcnInput = new();
    private readonly TextBox _manualPciInput = new();
    private readonly TextBox _manualCidInput = new();
    private readonly Label _manualLockStatus = new();
    private readonly Label _smsStatus = new();
    private readonly Label _smsSender = new();
    private readonly Label _smsReceived = new();
    private readonly TextBox _smsMessageView = new();
    private readonly TextBox _smsRecipientInput = new();
    private readonly TextBox _smsComposeInput = new();
    private readonly Label _smsLength = new();
    private readonly Button _smsRefreshButton = new();
    private readonly Button _smsReplyButton = new();
    private readonly Button _smsSendButton = new();

    private readonly Label _gatewayValue = new();
    private readonly Label _gatewayPingValue = new();
    private readonly Label _dnsValue = new();
    private readonly Label _ipv4Value = new();
    private readonly Label _ipv6Value = new();

    private CancellationTokenSource? _speedCancellation;
    private bool _speedBusy;
    private bool _allowExit;
    private DateTime _nextAutomaticSpeedTest;
    private bool _trayHintShown;
    private long _lastCellHistoryRevision = -1;
    private int _lastCellHistoryPeriod = -1;
    private bool _cellLockBusy;
    private DateTime _nextAutomaticCellLockCheckUtc = DateTime.MinValue;
    private DateTime _nextPublicIpCheckUtc = DateTime.MinValue;
    private bool _publicIpCheckBusy;
    private bool _smsBusy;
    private int _lastUnreadSmsCount = -1;
    private string _cellHistorySortColumn = "Rank";
    private bool _cellHistorySortAscending = true;
    private readonly HashSet<string> _collapsedCellGroups =
        new(StringComparer.Ordinal);
    private IReadOnlyList<RouterSmsMessage> _smsMessages = [];

    public MainForm()
    {
        _settings.Normalize();
        _routerPassword = ReadProtectedRouterPassword();
        _engine = CreateEngine();
        _routerMonitor = CreateRouterMonitor();

        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "NetPulse Monitor";
        StartPosition = FormStartPosition.CenterScreen;
        ApplyScreenRelativeWindowSize();
        BackColor = Color.FromArgb(244, 247, 250);
        Font = new Font("Segoe UI", 9F);
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath)
               ?? SystemIcons.Application;

        BuildInterface();
        ConfigureTray();
        LoadSettingsIntoControls();

        _uiTimer.Interval = 1000;
        _uiTimer.Tick += (_, _) =>
        {
            RefreshDashboard();
            RefreshRouterDashboard(_routerMonitor.GetSnapshot());
            RefreshCellHistory();
            CheckAutomaticSpeedTest();
            CheckPublicIpChange();
            CheckAutomaticCellLock();
        };

        Shown += async (_, _) =>
        {
            if (!_settings.RouterSetupCompleted)
                await ConfigureRouterAsync(firstRun: true);
            _nextAutomaticSpeedTest = GetNextSpeedTime();
            _engine.Start();
            _routerMonitor.Start();
            _uiTimer.Start();
            RefreshDashboard();
            RefreshRouterDashboard(_routerMonitor.GetSnapshot());
            RefreshCellHistory(force: true);
            _ = RecoverPendingCellLockAsync();
        };

        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized && _settings.MinimizeToTray)
                HideToTray();
        };

        FormClosing += OnFormClosing;
    }

    private MonitorEngine CreateEngine()
    {
        var engine = new MonitorEngine(_settings, _logger);

        engine.SampleRecorded += sample =>
        {
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(new Action(() => _chart.AddSample(sample)));
        };

        engine.EventOccurred += evt =>
        {
            if (evt.Kind.Equals("OFFLINE", StringComparison.OrdinalIgnoreCase))
            {
                _cellHistory.RecordConfirmedOutage(evt.Timestamp);
                _automaticSpeedTests.ObserveOutage();
            }
            else if (evt.Kind.Equals("ONLINE", StringComparison.OrdinalIgnoreCase))
            {
                _automaticSpeedTests.ObserveRecovery(DateTime.UtcNow);
            }
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(new Action(() => AddEventToGrid(evt)));
        };

        return engine;
    }

    private RouterMonitor CreateRouterMonitor()
    {
        var monitor = new RouterMonitor(_settings, _logger, _routerPassword);
        monitor.TelemetryUpdated += telemetry =>
        {
            _cellHistory.RecordTelemetry(telemetry);
            _automaticSpeedTests.ObserveRouterTelemetry(telemetry, DateTime.UtcNow);
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(new Action(() =>
                {
                    RefreshRouterDashboard(telemetry);
                    HandleUnreadSmsCount(telemetry.UnreadSmsCount);
                }));
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
            Text = "Continuous LTE and internet connection monitoring",
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
        _tabs.TabPages.Add(BuildConnectionDetailsTab());
        _tabs.TabPages.Add(BuildLteHistoryTab());
        _tabs.TabPages.Add(BuildManualCellLockTab());
        _tabs.TabPages.Add(BuildSmsTab());
        _tabs.TabPages.Add(BuildEventsTab());
        _tabs.TabPages.Add(BuildDiagnosticsTab());
        _tabs.TabPages.Add(BuildSettingsTab());
        _tabs.SelectedIndexChanged += async (_, _) =>
        {
            if (_tabs.SelectedTab?.Text == "SMS")
                await RefreshSmsInboxAsync(showErrors: false);
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
        Rectangle working = Screen.PrimaryScreen?.WorkingArea ??
                            new Rectangle(0, 0, 1280, 800);
        int width = Math.Min(working.Width, Math.Max(1050,
            (int)Math.Round(working.Width * 0.94)));
        int height = Math.Min(working.Height, Math.Max(720,
            (int)Math.Round(working.Height * 0.94)));
        MinimumSize = new Size(width, height);
        Size = MinimumSize;
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
            RowCount = 3,
            ColumnCount = 1
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 270));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));

        var metricGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 3
        };

        for (int i = 0; i < 4; i++)
            metricGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        for (int i = 0; i < 3; i++)
            metricGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333F));

        AddMetric(metricGrid, 0, 0, "CURRENT PING", "Ping");
        AddMetric(metricGrid, 1, 0, "JITTER", "Jitter");
        AddMetric(metricGrid, 2, 0, "PACKET LOSS", "Loss");
        AddMetric(metricGrid, 3, 0, "SUCCESS / FAIL", "SuccessFail");

        AddMetric(metricGrid, 0, 1, "RUN TIME", "RunTime");
        AddMetric(metricGrid, 1, 1, "TOTAL DOWNTIME", "Downtime");
        AddMetric(metricGrid, 2, 1, "AVAILABILITY", "Availability");
        AddMetric(metricGrid, 3, 1, "OUTAGES", "Outages");

        AddMetric(metricGrid, 0, 2, "DOWNLOAD", "Download");
        AddMetric(metricGrid, 1, 2, "UPLOAD", "Upload");
        AddMetric(metricGrid, 2, 2, "SPEEDTEST PING", "SpeedPing");
        AddMetric(metricGrid, 3, 2, "SPEEDTEST LOSS", "SpeedLoss");

        _chart.Dock = DockStyle.Fill;
        _chart.Margin = new Padding(4);
        _chart.BackColor = Color.White;

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

        layout.Controls.Add(metricGrid, 0, 0);
        layout.Controls.Add(_chart, 0, 1);
        layout.Controls.Add(controls, 0, 2);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildConnectionDetailsTab()
    {
        var page = new TabPage("Connection details")
        {
            BackColor = Color.FromArgb(244, 247, 250),
            Padding = new Padding(12)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));

        var statusPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.White,
            Padding = new Padding(12, 8, 12, 8),
            Margin = new Padding(5)
        };
        statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
        statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        statusPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _routerConnectionState.Text = "NOT CONFIGURED";
        _routerConnectionState.Dock = DockStyle.Fill;
        _routerConnectionState.TextAlign = ContentAlignment.MiddleCenter;
        _routerConnectionState.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        _routerConnectionState.MaximumFontSize = 10F;
        _routerConnectionState.ForeColor = Color.White;
        _routerConnectionState.BackColor = Color.DimGray;

        _routerDetails.Text = "Configure a TP-Link MR600 to show live LTE information.";
        _routerDetails.Dock = DockStyle.Fill;
        _routerDetails.TextAlign = ContentAlignment.MiddleLeft;
        _routerDetails.AutoEllipsis = true;
        _routerDetails.Padding = new Padding(14, 0, 0, 0);

        statusPanel.Controls.Add(_routerConnectionState, 0, 0);
        statusPanel.Controls.Add(_routerDetails, 1, 0);

        var selectors = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(8, 0, 0, 0)
        };
        selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        selectors.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        selectors.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var accessLabel = new Label
        {
            Text = "Access",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var localLinkLabel = new Label
        {
            Text = "PC link",
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
            RefreshRouterDashboard(_routerMonitor.GetSnapshot());
        };

        _localLinkInput.DropDownStyle = ComboBoxStyle.DropDownList;
        _localLinkInput.Dock = DockStyle.Fill;
        _localLinkInput.Items.AddRange(["Auto detect", "Wi-Fi", "Ethernet"]);
        _localLinkInput.SelectedIndexChanged += (_, _) =>
        {
            if (_localLinkInput.SelectedIndex < 0)
                return;
            _settings.LocalLinkView = _localLinkInput.SelectedIndex switch
            {
                1 => "Wifi",
                2 => "Ethernet",
                _ => "Auto"
            };
            if (IsHandleCreated)
                _settings.Save();
            RefreshRouterDashboard(_routerMonitor.GetSnapshot());
        };
        selectors.Controls.Add(accessLabel, 0, 0);
        selectors.Controls.Add(_connectionViewInput, 1, 0);
        selectors.Controls.Add(localLinkLabel, 0, 1);
        selectors.Controls.Add(_localLinkInput, 1, 1);
        statusPanel.Controls.Add(selectors, 2, 0);

        var metricGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 4
        };
        for (int column = 0; column < 4; column++)
            metricGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        for (int row = 0; row < 4; row++)
            metricGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 25));

        AddRouterMetric(metricGrid, 0, 0, "ROUTER STATUS", "Status");
        AddRouterMetric(metricGrid, 1, 0, "ISP", "Isp");
        AddRouterMetric(metricGrid, 2, 0, "NETWORK TYPE", "Network");
        AddRouterMetric(metricGrid, 3, 0, "LTE BAND", "Band");
        AddRouterMetric(metricGrid, 0, 1, "SIGNAL", "Signal");
        AddRouterMetric(metricGrid, 1, 1, "RSRP", "Rsrp");
        AddRouterMetric(metricGrid, 2, 1, "RSRQ", "Rsrq");
        AddRouterMetric(metricGrid, 3, 1, "SNR", "Snr");
        AddRouterMetric(metricGrid, 0, 2, "PCI", "Pci");
        AddRouterMetric(metricGrid, 1, 2, "CELL ID", "Cell");
        AddRouterMetric(metricGrid, 2, 2, "EARFCN", "Earfcn");
        AddRouterMetric(metricGrid, 3, 2, "SIM STATUS", "Sim");
        AddRouterMetric(metricGrid, 0, 3, "DATA USED", "Data");
        AddRouterMetric(metricGrid, 1, 3, "ROUTER UPLOAD", "RouterUpload");
        AddRouterMetric(metricGrid, 2, 3, "ROUTER DOWNLOAD", "RouterDownload");
        AddRouterMetric(metricGrid, 3, 3, "LAST UPDATE", "Updated");

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(5, 9, 5, 4)
        };
        var configureButton = new Button
        {
            Text = "Configure TP-Link router",
            Size = new Size(190, 38)
        };
        configureButton.Click += async (_, _) =>
            await ConfigureRouterAsync(firstRun: false);
        var refreshButton = new Button
        {
            Text = "Reconnect now",
            Size = new Size(145, 38)
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
        var privacyLabel = new Label
        {
            Text = "One-second telemetry • router changes require explicit opt-in • identifiers excluded from diagnostics",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(14, 11, 0, 0)
        };
        controls.Controls.Add(configureButton);
        controls.Controls.Add(refreshButton);
        controls.Controls.Add(privacyLabel);

        layout.Controls.Add(statusPanel, 0, 0);
        layout.Controls.Add(metricGrid, 0, 1);
        layout.Controls.Add(controls, 0, 2);
        page.Controls.Add(layout);
        return page;
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
            Text = "Time-aware cell and band recommendation",
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
        AddCellHistoryColumn("Period", "Period", 12);
        AddCellHistoryColumn("Band", "Band", 8);
        AddCellHistoryColumn("Earfcn", "EARFCN", 9);
        AddCellHistoryColumn("Pci", "PCI", 7);
        AddCellHistoryColumn("Cid", "CID", 11);
        AddCellHistoryColumn("Time", "Seen", 11);
        AddCellHistoryColumn("Usage", "Use %", 11);
        AddCellHistoryColumn("Weight", "Time wt.", 10);
        AddCellHistoryColumn("Drops", "Drops P/A", 10);
        AddCellHistoryColumn("DropRate", "Drop/h", 10);
        AddCellHistoryColumn("Down", "Down", 11);
        AddCellHistoryColumn("Up", "Up", 10);
        AddCellHistoryColumn("Confidence", "Confidence", 11);
        _cellHistoryGrid.ColumnHeaderMouseClick += (_, args) =>
            SortCellHistoryByColumn(args.ColumnIndex);
        _cellHistoryGrid.CellClick += (_, args) =>
        {
            if (args.RowIndex < 0 ||
                _cellHistoryGrid.Rows[args.RowIndex].Tag is not CellHistoryGroupRow group)
                return;
            if (!_collapsedCellGroups.Add(group.Key))
                _collapsedCellGroups.Remove(group.Key);
            RefreshCellHistory(force: true);
        };

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(5, 10, 5, 4)
        };
        var applyButton = new Button
        {
            Text = "Apply selected lock...",
            Size = new Size(175, 40)
        };
        applyButton.Click += async (_, _) =>
            await ApplySelectedCellLockAsync(applyButton);

        var automaticButton = new Button
        {
            Text = "Restore automatic selection",
            Size = new Size(205, 40)
        };
        automaticButton.Click += async (_, _) =>
            await RestoreAutomaticCellSelectionAsync(automaticButton);

        var copyButton = new Button
        {
            Text = "Copy selected lock values",
            Size = new Size(205, 40)
        };
        copyButton.Click += (_, _) => CopySelectedCellLock();

        var openRouterButton = new Button
        {
            Text = "Open MR600 Cell Lock",
            Size = new Size(190, 40)
        };
        openRouterButton.Click += (_, _) => OpenRouterCellLockPage();

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

        _cellAutoStatus.Text =
            "Adaptive auto is off • 30-minute dwell • daily limit • 90-second rollback validation";
        _cellAutoStatus.AutoSize = true;
        _cellAutoStatus.ForeColor = Color.DimGray;
        _cellAutoStatus.Margin = new Padding(14, 12, 0, 0);
        controls.Controls.Add(applyButton);
        controls.Controls.Add(automaticButton);
        controls.Controls.Add(copyButton);
        controls.Controls.Add(openRouterButton);
        controls.Controls.Add(clearButton);
        controls.Controls.Add(_cellAutoStatus);

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
        column.HeaderCell.ToolTipText = name switch
        {
            "Usage" => "Share of observed data traffic; falls back to connection time when unavailable",
            "Weight" => "How strongly the current time period influences the all-time baseline",
            "Drops" => "Confirmed disconnections in this period / across all periods",
            "DropRate" => "Time-weighted confirmed disconnections per connected hour",
            "Down" => "Time-weighted average speed-test download",
            "Up" => "Time-weighted average speed-test upload",
            "Cid" => "Optional; used only when the router reports it",
            _ => header
        };
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
            Text = "Manual MR600 Cell Lock\r\nEnter a known primary-cell profile. " +
                   "CID is optional; EARFCN and PCI are required. Saving adds the profile " +
                   "to LTE history without inventing measurements. Applying always asks " +
                   "for confirmation and keeps automatic rollback protection.",
            Font = new Font("Segoe UI", 10F),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            BackColor = Color.White,
            Padding = new Padding(22, 16, 22, 16),
            Margin = new Padding(5)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        for (int row = 0; row < 5; row++)
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        _manualBandsInput.PlaceholderText = "B3 or B3 + B20";
        _manualEarfcnInput.PlaceholderText = "Primary EARFCN";
        _manualPciInput.PlaceholderText = "0-512";
        _manualCidInput.PlaceholderText = "Optional Cell ID";
        AddManualLockField(fields, 0, "LTE band profile", _manualBandsInput);
        AddManualLockField(fields, 1, "Primary EARFCN", _manualEarfcnInput);
        AddManualLockField(fields, 2, "PCI", _manualPciInput);
        AddManualLockField(fields, 3, "CID (optional)", _manualCidInput);
        _manualLockStatus.Dock = DockStyle.Fill;
        _manualLockStatus.TextAlign = ContentAlignment.MiddleLeft;
        _manualLockStatus.ForeColor = Color.DimGray;
        _manualLockStatus.Text =
            "Profiles with the same PCell and EARFCN keep known PCI/CID across carrier aggregation changes.";
        fields.Controls.Add(_manualLockStatus, 0, 4);
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
        var apply = new Button { Text = "Save and apply lock...", Size = new Size(190, 40) };
        apply.Click += async (_, _) => await ApplyManualCellLockAsync(apply);
        var restore = new Button { Text = "Restore automatic selection", Size = new Size(210, 40) };
        restore.Click += async (_, _) => await RestoreAutomaticCellSelectionAsync(restore);
        controls.Controls.Add(save);
        controls.Controls.Add(apply);
        controls.Controls.Add(restore);

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
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));

        _smsStatus.Dock = DockStyle.Fill;
        _smsStatus.BackColor = Color.White;
        _smsStatus.Padding = new Padding(14, 0, 14, 0);
        _smsStatus.TextAlign = ContentAlignment.MiddleLeft;
        _smsStatus.Text = "Connect TP-Link monitoring to read and send SIM messages.";
        _smsStatus.ForeColor = Color.DimGray;

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(5)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));

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
        _smsGrid.Columns.Add("SmsState", "Status");
        _smsGrid.Columns.Add("SmsFrom", "From");
        _smsGrid.Columns.Add("SmsReceived", "Received");
        _smsGrid.Columns.Add("SmsPreview", "Message");
        _smsGrid.Columns[0].FillWeight = 13;
        _smsGrid.Columns[1].FillWeight = 22;
        _smsGrid.Columns[2].FillWeight = 25;
        _smsGrid.Columns[3].FillWeight = 40;
        _smsGrid.SelectionChanged += async (_, _) => await OpenSelectedSmsAsync();

        var reader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            BackColor = Color.White,
            Padding = new Padding(16, 12, 16, 12)
        };
        reader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        reader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        reader.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        reader.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        reader.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        reader.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        reader.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        reader.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        reader.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        AddSmsReadLabel(reader, 0, "From", _smsSender);
        AddSmsReadLabel(reader, 1, "Received", _smsReceived);
        _smsMessageView.Dock = DockStyle.Fill;
        _smsMessageView.Multiline = true;
        _smsMessageView.ReadOnly = true;
        _smsMessageView.ScrollBars = ScrollBars.Vertical;
        _smsMessageView.BackColor = Color.White;
        reader.Controls.Add(_smsMessageView, 0, 2);
        reader.SetColumnSpan(_smsMessageView, 2);
        reader.Controls.Add(new Label
        {
            Text = "To",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        }, 0, 3);
        _smsRecipientInput.Dock = DockStyle.Fill;
        _smsRecipientInput.PlaceholderText = "+30...";
        _smsRecipientInput.Margin = new Padding(3, 6, 3, 6);
        reader.Controls.Add(_smsRecipientInput, 1, 3);
        _smsComposeInput.Dock = DockStyle.Fill;
        _smsComposeInput.Multiline = true;
        _smsComposeInput.ScrollBars = ScrollBars.Vertical;
        _smsComposeInput.MaxLength = 765;
        _smsComposeInput.PlaceholderText = "Write a new message or select Reply";
        _smsComposeInput.TextChanged += (_, _) => RefreshSmsLength();
        reader.Controls.Add(_smsComposeInput, 0, 4);
        reader.SetColumnSpan(_smsComposeInput, 2);
        _smsLength.Dock = DockStyle.Fill;
        _smsLength.TextAlign = ContentAlignment.MiddleRight;
        _smsLength.ForeColor = Color.DimGray;
        reader.Controls.Add(_smsLength, 0, 5);
        reader.SetColumnSpan(_smsLength, 2);
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
        _smsRefreshButton.Text = "Refresh inbox";
        _smsRefreshButton.Size = new Size(145, 40);
        _smsRefreshButton.Click += async (_, _) => await RefreshSmsInboxAsync(showErrors: true);
        _smsReplyButton.Text = "Reply";
        _smsReplyButton.Size = new Size(110, 40);
        _smsReplyButton.Click += (_, _) => ReplyToSelectedSms();
        var newButton = new Button { Text = "New message", Size = new Size(140, 40) };
        newButton.Click += (_, _) => StartNewSms();
        _smsSendButton.Text = "Send SMS...";
        _smsSendButton.Size = new Size(135, 40);
        _smsSendButton.Click += async (_, _) => await SendSmsAsync();
        controls.Controls.Add(_smsRefreshButton);
        controls.Controls.Add(_smsReplyButton);
        controls.Controls.Add(newButton);
        controls.Controls.Add(_smsSendButton);

        layout.Controls.Add(_smsStatus, 0, 0);
        layout.Controls.Add(content, 0, 1);
        layout.Controls.Add(controls, 0, 2);
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
        var page = new TabPage("Events");

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

        page.Controls.Add(_eventsGrid);
        return page;
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
            Dock = DockStyle.Top,
            Height = 300,
            ColumnCount = 2,
            RowCount = 6,
            BackColor = Color.White,
            Padding = new Padding(15)
        };

        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));

        AddDiagnosticRow(grid, 0, "Default gateway", _gatewayValue);
        AddDiagnosticRow(grid, 1, "Gateway latency", _gatewayPingValue);
        AddDiagnosticRow(grid, 2, "DNS lookup latency", _dnsValue);
        AddDiagnosticRow(grid, 3, "IPv4", _ipv4Value);
        AddDiagnosticRow(grid, 4, "IPv6", _ipv6Value);

        var runButton = new Button
        {
            Text = "Run diagnostics",
            Dock = DockStyle.Fill,
            Height = 42
        };

        runButton.Click += async (_, _) =>
        {
            runButton.Enabled = false;
            runButton.Text = "Running…";

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                DiagnosticResult result = await NetworkDiagnostics.RunAsync(cts.Token);
                _gatewayValue.Text = result.Gateway;
                _gatewayPingValue.Text = result.GatewayPing;
                _dnsValue.Text = result.DnsLookup;
                _ipv4Value.Text = result.IPv4;
                _ipv6Value.Text = result.IPv6;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Diagnostics",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                runButton.Enabled = true;
                runButton.Text = "Run diagnostics";
            }
        };

        grid.Controls.Add(runButton, 0, 5);
        grid.SetColumnSpan(runButton, 2);

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
            "Monitoring and speed tests", 5);
        TableLayoutPanel integration = CreateSettingsSection(
            "Windows and TP-Link", 6);

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

        AddSettingRow(integration, 0, "Start with Windows", _startupInput);
        AddSettingRow(integration, 1, "Minimize to system tray", _trayInput);
        AddSettingRow(integration, 2, "TP-Link MR600 live monitoring", _routerEnabledInput);
        AddSettingRow(integration, 3, "TP-Link router address", _routerAddressInput);

        _routerSetupButton.Text = "Configure protected password...";
        _routerSetupButton.Click += async (_, _) =>
            await ConfigureRouterAsync(firstRun: false);
        AddSettingRow(integration, 4, "TP-Link local credentials", _routerSetupButton);
        _automaticCellLockInput.Text =
            "Allow guarded time-aware cell + band optimization";
        _automaticCellLockInput.AutoSize = false;
        _automaticCellLockInput.AutoEllipsis = true;
        AddSettingRow(integration, 5, "TP-Link LTE optimization", _automaticCellLockInput);

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

    private static TableLayoutPanel CreateSettingsSection(string heading, int fieldRows)
    {
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = fieldRows + 1,
            BackColor = Color.White,
            Margin = new Padding(5),
            Padding = new Padding(14, 10, 14, 10)
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        for (int row = 0; row < fieldRows; row++)
            section.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / fieldRows));

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
            Margin = new Padding(5),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        var captionLabel = new Label
        {
            Text = caption,
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 26,
            ForeColor = Color.DimGray,
            Font = new Font("Segoe UI", 8.5F),
            TextAlign = ContentAlignment.BottomLeft,
            Padding = new Padding(12, 0, 8, 0)
        };

        var valueLabel = new AutoFitLabel
        {
            Text = "",
            Dock = DockStyle.Fill,
            MaximumFontSize = 16F,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 8, 0)
        };

        _metrics[key] = valueLabel;
        card.Controls.Add(valueLabel);
        card.Controls.Add(captionLabel);
        grid.Controls.Add(card, column, row);
    }

    private void AddRouterMetric(
        TableLayoutPanel grid, int column, int row, string caption, string key)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(5),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        var captionLabel = new Label
        {
            Text = caption,
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 25,
            ForeColor = Color.DimGray,
            Font = new Font("Segoe UI", 8.5F),
            TextAlign = ContentAlignment.BottomLeft,
            Padding = new Padding(12, 0, 8, 0)
        };
        var valueLabel = new AutoFitLabel
        {
            Text = "",
            Dock = DockStyle.Fill,
            MaximumFontSize = 14F,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 8, 0)
        };
        _routerMetrics[key] = valueLabel;
        _routerMetricCaptions[key] = captionLabel;
        card.Controls.Add(valueLabel);
        card.Controls.Add(captionLabel);
        grid.Controls.Add(card, column, row);
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

        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(3, 7, 3, 7);

        grid.Controls.Add(label, 0, row + 1);
        grid.Controls.Add(control, 1, row + 1);
    }

    private void RefreshDashboard()
    {
        MonitorSnapshot snapshot = _engine.GetSnapshot();
        bool hasSamples = snapshot.SuccessfulPings + snapshot.FailedPings > 0;

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

        _metrics["Ping"].Text =
            snapshot.CurrentPingMs.HasValue ? snapshot.CurrentPingMs + " ms" : "";
        _metrics["Jitter"].Text = snapshot.SuccessfulPings >= 2
            ? snapshot.JitterMs.ToString("0.#") + " ms"
            : "";
        _metrics["Loss"].Text = hasSamples
            ? snapshot.PacketLossPercent.ToString("0.#") + "%"
            : "";
        _metrics["SuccessFail"].Text =
            hasSamples ? snapshot.SuccessfulPings + " / " + snapshot.FailedPings : "";
        _metrics["RunTime"].Text = hasSamples ? FormatDuration(snapshot.RunTime) : "";
        _metrics["Downtime"].Text = snapshot.TotalDowntime > TimeSpan.Zero
            ? FormatDuration(snapshot.TotalDowntime)
            : "";
        _metrics["Availability"].Text = hasSamples
            ? snapshot.AvailabilityPercent.ToString("0.###") + "%"
            : "";
        _metrics["Outages"].Text = snapshot.Outages > 0
            ? snapshot.Outages.ToString(CultureInfo.CurrentCulture)
            : "";

        _footer.Text =
            $"Target: {_settings.PingTarget}   •   " +
            $"Next automatic speed test: {FormatNextSpeedTest()}   •   " +
            $"Logs: {_logger.LogFolder}";
    }

    private void RefreshCellHistory(bool force = false)
    {
        long revision = _cellHistory.Revision;
        int period = LteCellHistoryStore.GetTimePeriod(DateTime.Now);
        if (!force && revision == _lastCellHistoryRevision &&
            period == _lastCellHistoryPeriod)
            return;
        _lastCellHistoryRevision = revision;
        _lastCellHistoryPeriod = period;

        string? selectedKey = _cellHistoryGrid.SelectedRows.Count > 0
            ? (_cellHistoryGrid.SelectedRows[0].Tag as LteCellRecommendation)?.Key
            : null;
        IReadOnlyList<LteCellRecommendation> recommendations =
            _cellHistory.GetRecommendations();

        _cellHistoryGrid.Rows.Clear();
        int eligibleRank = 0;
        var ranks = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (LteCellRecommendation item in recommendations)
            ranks[item.Key] = item.IsEligible ? (++eligibleRank).ToString() : "-";

        IReadOnlyList<LteCellRecommendation> sorted =
            SortCellHistory(recommendations).ToArray();
        foreach (IGrouping<string, LteCellRecommendation> group in sorted.GroupBy(
                     GetCellHistoryGroupKey,
                     StringComparer.Ordinal))
        {
            LteCellRecommendation[] profiles = group.ToArray();
            if (profiles.Length > 1)
            {
                bool collapsed = _collapsedCellGroups.Contains(group.Key);
                LteCellRecommendation cell = profiles[0];
                int groupRowIndex = _cellHistoryGrid.Rows.Add(
                    collapsed ? "▶" : "▼",
                    "PCell group",
                    $"{cell.PrimaryBand} ({profiles.Length} profiles)",
                    cell.Earfcn,
                    cell.Pci,
                    cell.CellId ?? "-",
                    "", "", "", "", "", "", "", "");
                DataGridViewRow groupRow = _cellHistoryGrid.Rows[groupRowIndex];
                groupRow.Tag = new CellHistoryGroupRow(group.Key);
                groupRow.DefaultCellStyle.BackColor = Color.FromArgb(225, 235, 244);
                groupRow.DefaultCellStyle.ForeColor = Color.FromArgb(25, 70, 105);
                groupRow.DefaultCellStyle.Font = _cellGroupFont;
                if (collapsed)
                    continue;
            }

            foreach (LteCellRecommendation item in profiles)
                AddCellHistoryRow(item, ranks[item.Key], selectedKey);
        }

        LteCellRecommendation? best = recommendations.FirstOrDefault(item => item.IsEligible);
        if (best is not null)
        {
            string cid = string.IsNullOrWhiteSpace(best.CellId) || best.CellId == "-"
                ? "CID optional"
                : $"CID {best.CellId}";
            string radioTarget = best.Pci == "-"
                ? $"EARFCN {best.Earfcn}, PCI not exposed (band-only optimization)"
                : $"EARFCN {best.Earfcn}, PCI {best.Pci}";
            _cellSuggestion.Text =
                $"{best.TimePeriod}: {best.Band}, {radioTarget}, {cid}. " +
                $"Current-period evidence weight {best.TimeEvidenceWeightPercent:0}% " +
                $"({best.UsageSharePercent:0.#}% observed {best.UsageBasis} share). " +
                $"Reliability first: {best.DisconnectionsPerHour:0.00} confirmed drops/h; then " +
                $"{FormatMbps(best.AverageDownloadMbps)} down and " +
                $"{FormatMbps(best.AverageUploadMbps)} up. Confidence: {best.Confidence}.";
            _cellSuggestion.ForeColor = Color.FromArgb(25, 82, 45);
        }
        else if (recommendations.Count > 0)
        {
            _cellSuggestion.Text =
                "Collecting evidence: each recommendation needs at least 10 connected minutes " +
                "and one speed test on the same band profile and primary EARFCN. " +
                "PCI and CID are used when the firmware exposes them.";
            _cellSuggestion.ForeColor = Color.DarkGoldenrod;
        }
        else
        {
            _cellSuggestion.Text =
                "Waiting for LTE observations. PCI and CID are used only when the firmware exposes them.";
            _cellSuggestion.ForeColor = Color.DimGray;
        }

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

    private void AddCellHistoryRow(
        LteCellRecommendation item,
        string rank,
        string? selectedKey)
    {
        int rowIndex = _cellHistoryGrid.Rows.Add(
            rank,
            item.TimePeriod,
            item.Band,
            item.Earfcn,
            item.Pci,
            item.CellId ?? "-",
            FormatCompactDuration(item.PeriodConnectedTime),
            $"{item.UsageSharePercent:0.#}% {item.UsageBasis}",
            $"{item.TimeEvidenceWeightPercent:0}%",
            $"{item.PeriodDisconnections} / {item.Disconnections}",
            item.DisconnectionsPerHour.ToString("0.00", CultureInfo.CurrentCulture),
            FormatMbps(item.AverageDownloadMbps),
            FormatMbps(item.AverageUploadMbps),
            item.Confidence);
        DataGridViewRow row = _cellHistoryGrid.Rows[rowIndex];
        row.Tag = item;
        row.Cells["Band"].Style.Padding = new Padding(10, 0, 0, 0);
        if (!item.IsEligible)
            row.DefaultCellStyle.ForeColor = Color.DimGray;
        if (item.UserAdded)
            row.DefaultCellStyle.BackColor = Color.FromArgb(250, 247, 232);
        if (string.Equals(item.Key, selectedKey, StringComparison.Ordinal))
            row.Selected = true;
    }

    private static string GetCellHistoryGroupKey(LteCellRecommendation item) =>
        string.Join(
            "|",
            item.PrimaryBand.ToUpperInvariant(),
            item.Earfcn,
            item.Pci,
            item.CellId ?? "*");

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
            "Period" => OrderCellHistory(source, item => item.TimePeriod),
            "Band" => OrderCellHistory(source, item => item.Band),
            "Earfcn" => OrderCellHistory(source, item => NumericSort(item.Earfcn)),
            "Pci" => OrderCellHistory(source, item => NumericSort(item.Pci)),
            "Cid" => OrderCellHistory(source, item => NumericSort(item.CellId)),
            "Time" => OrderCellHistory(source, item => item.PeriodConnectedTime),
            "Usage" => OrderCellHistory(source, item => item.UsageSharePercent),
            "Weight" => OrderCellHistory(source, item => item.TimeEvidenceWeightPercent),
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
        if (!_engine.GetSnapshot().IsOnline)
        {
            MessageBox.Show(
                "Internet monitoring is currently offline. Wait for a stable connection " +
                "so rollback validation has a valid baseline.",
                "Manual Cell Lock",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        string cidText = cid is null ? "not used (optional)" : cid;
        if (MessageBox.Show(
                $"Save and apply this MR600 primary-cell lock?\r\n\r\n" +
                $"Band profile: {band}\r\nEARFCN: {earfcn}\r\nPCI: {pci}\r\nCID: {cidText}\r\n\r\n" +
                "Mobile service may briefly disconnect. NetPulse will restore the " +
                "previous settings if connectivity validation fails.",
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
        cid = string.IsNullOrWhiteSpace(_manualCidInput.Text) ||
              _manualCidInput.Text.Trim() == "0"
            ? null
            : _manualCidInput.Text.Trim();
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
        if (cid is not null && !uint.TryParse(cid, NumberStyles.None,
                CultureInfo.InvariantCulture, out _))
        {
            error = "CID is optional; when supplied it must contain digits only.";
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

    private async Task RefreshSmsInboxAsync(bool showErrors)
    {
        if (_smsBusy || IsDisposed)
            return;
        SetSmsBusy(true, "Refreshing SIM inbox...");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            _smsMessages = await _routerMonitor.ReadSmsInboxAsync(timeout.Token);
            string? selectedStack = _smsGrid.SelectedRows.Count > 0
                ? (_smsGrid.SelectedRows[0].Tag as RouterSmsMessage)?.Stack
                : null;
            _smsGrid.Rows.Clear();
            foreach (RouterSmsMessage message in _smsMessages)
            {
                string preview = Regex.Replace(message.Content, @"\s+", " ").Trim();
                if (preview.Length > 80)
                    preview = preview[..77] + "...";
                int index = _smsGrid.Rows.Add(
                    message.IsUnread ? "Unread" : "Read",
                    message.From,
                    message.ReceivedTime,
                    preview);
                DataGridViewRow row = _smsGrid.Rows[index];
                row.Tag = message;
                if (message.IsUnread)
                    row.DefaultCellStyle.Font = _smsUnreadFont;
                if (message.Stack == selectedStack)
                    row.Selected = true;
            }
            int unread = _smsMessages.Count(message => message.IsUnread);
            _smsStatus.Text = _smsMessages.Count == 0
                ? "SIM inbox is empty. Message content stays in memory and is never written to logs."
                : $"{_smsMessages.Count} messages • {unread} unread • content is never written to logs.";
            _smsStatus.ForeColor = unread > 0 ? Color.DarkGoldenrod : Color.DimGray;
        }
        catch (Exception ex)
        {
            _smsStatus.Text = "SIM inbox unavailable: " + FriendlyUiError(ex);
            _smsStatus.ForeColor = Color.Firebrick;
            if (showErrors)
            {
                MessageBox.Show(
                    "The SIM inbox could not be refreshed.\r\n\r\n" + FriendlyUiError(ex),
                    "MR600 SMS",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        finally
        {
            SetSmsBusy(false);
        }
    }

    private async Task OpenSelectedSmsAsync()
    {
        if (_smsBusy || _smsGrid.SelectedRows.Count == 0 ||
            _smsGrid.SelectedRows[0].Tag is not RouterSmsMessage message)
            return;
        _smsSender.Text = message.From;
        _smsReceived.Text = message.ReceivedTime;
        _smsMessageView.Text = message.Content;
        _smsReplyButton.Enabled = true;
        if (!message.IsUnread)
            return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _routerMonitor.MarkSmsReadAsync(message.Stack, timeout.Token);
            message.IsUnread = false;
            DataGridViewRow row = _smsGrid.SelectedRows[0];
            row.Cells["SmsState"].Value = "Read";
            row.DefaultCellStyle.Font = Font;
        }
        catch (Exception ex)
        {
            _smsStatus.Text = "Message opened, but read status could not be updated: " +
                              FriendlyUiError(ex);
            _smsStatus.ForeColor = Color.Firebrick;
        }
    }

    private void ReplyToSelectedSms()
    {
        if (_smsGrid.SelectedRows.Count == 0 ||
            _smsGrid.SelectedRows[0].Tag is not RouterSmsMessage message)
        {
            MessageBox.Show("Select an inbox message first.", "MR600 SMS",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _smsRecipientInput.Text = message.From;
        _smsComposeInput.Clear();
        _smsComposeInput.Focus();
    }

    private void StartNewSms()
    {
        _smsRecipientInput.Clear();
        _smsComposeInput.Clear();
        _smsRecipientInput.Focus();
    }

    private async Task SendSmsAsync()
    {
        if (_smsBusy)
            return;
        string recipient = _smsRecipientInput.Text.Trim();
        string content = _smsComposeInput.Text;
        if (!Regex.IsMatch(recipient, @"^\+?\d{1,20}$") || recipient.Length > 20)
        {
            MessageBox.Show(
                "Phone number must contain 1 to 20 digits, with an optional leading +.",
                "MR600 SMS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
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
            return;
        }
        if (MessageBox.Show(
                $"Send this SMS to {recipient}?",
                "Send SMS",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        SetSmsBusy(true, "Sending SMS through the MR600...");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _routerMonitor.SendSmsAsync(recipient, content, timeout.Token);
            _smsRecipientInput.Clear();
            _smsComposeInput.Clear();
            _smsStatus.Text = "SMS sent successfully. No recipient or message content was logged.";
            _smsStatus.ForeColor = Color.FromArgb(25, 82, 45);
        }
        catch (Exception ex)
        {
            _smsStatus.Text = "SMS was not sent: " + FriendlyUiError(ex);
            _smsStatus.ForeColor = Color.Firebrick;
            MessageBox.Show(
                "The SMS was not sent.\r\n\r\n" + FriendlyUiError(ex),
                "MR600 SMS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            SetSmsBusy(false);
        }
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
        _smsRefreshButton.Enabled = !busy;
        _smsReplyButton.Enabled = !busy && _smsGrid.SelectedRows.Count > 0;
        _smsSendButton.Enabled = !busy;
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
            $"CID (optional): {selected.CellId ?? "not available"}";
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
        if (!TryCreateLockTarget(selected, out RouterCellLockTarget? target, out string error))
        {
            MessageBox.Show(error, "LTE history",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!_engine.GetSnapshot().IsOnline)
        {
            MessageBox.Show(
                "Internet monitoring is currently offline. Wait for a stable connection " +
                "before applying a lock so the rollback check has a valid baseline.",
                "Apply Cell Lock",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        string lockDetails;
        if (target!.HasCellTarget)
        {
            string cid = target.CellId is null ? "not used (optional)" : target.CellId;
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
        DialogResult answer = MessageBox.Show(
            $"Apply this measured MR600 profile?\r\n\r\n" +
            $"Band: {selected.Band}\r\n" +
            lockDetails + "\r\n\r\n" +
            "Mobile service may briefly disconnect. NetPulse will validate for " +
            $"{_settings.CellLockValidationSeconds} seconds and restore the previous " +
            "router settings if internet or LTE does not recover.",
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
            _speedBusy ||
            !_engine.GetSnapshot().IsOnline ||
            _settings.PendingCellLockRollback is not null)
            return;
        ResetAutomaticCellLockDailyCounter();
        if (!LteAutoLockPolicy.CanAttempt(_settings, now))
            return;

        IReadOnlyList<LteCellRecommendation> recommendations =
            _cellHistory.GetRecommendations();
        LteCellRecommendation? best = recommendations
            .FirstOrDefault(item =>
                item.IsEligible && item.Confidence is "Medium" or "High");
        if (best is null ||
            !TryCreateLockTarget(best, out RouterCellLockTarget? target, out _))
            return;

        LteCellRecommendation? current = recommendations.FirstOrDefault(item =>
            string.Equals(
                item.Key,
                _settings.LastAutomaticCellLockKey,
                StringComparison.Ordinal));
        if (current is not null && current.Key != best.Key &&
            !LteAutoLockPolicy.IsMeaningfullyBetter(best, current))
            return;

        _ = ApplyCellLockWithRollbackAsync(best, target!, automatic: true);
    }

    private void ResetAutomaticCellLockDailyCounter()
    {
        DateTime today = DateTime.Today;
        if (_settings.AutomaticCellLockCounterDate?.ToLocalTime().Date == today)
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

    private async Task ApplyCellLockWithRollbackAsync(
        LteCellRecommendation recommendation,
        RouterCellLockTarget target,
        bool automatic)
    {
        if (_cellLockBusy)
            return;
        _cellLockBusy = true;
        string profileKind = target.HasCellTarget ? "cell + band lock" : "band profile";
        RouterLockState? previousState = null;
        try
        {
            using (var changeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            {
                previousState = await _routerMonitor.ReadLockStateAsync(changeTimeout.Token);
                if (LockStateMatchesTarget(previousState, target))
                {
                    if (automatic)
                    {
                        _settings.LastAutomaticCellLockUtc = DateTime.UtcNow;
                        _settings.LastAutomaticCellLockKey = recommendation.Key;
                        _settings.Save();
                    }
                    AddCellLockEvent(
                        $"{recommendation.Band} {profileKind} already matches the selected target");
                    if (!automatic && !IsDisposed)
                    {
                        MessageBox.Show(
                            $"The MR600 already uses the selected {profileKind}.",
                            "Cell Lock active",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    return;
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
                $" MR600 {profileKind} applied for {recommendation.Band}; validating connectivity");
            _routerDetails.Text =
                $"Validating {recommendation.Band} Cell Lock for " +
                $"{_settings.CellLockValidationSeconds} seconds…";

            await Task.Delay(TimeSpan.FromSeconds(_settings.CellLockValidationSeconds));
            MonitorSnapshot internet = _engine.GetSnapshot();
            RouterTelemetry router = _routerMonitor.GetSnapshot();
            bool valid = internet.IsOnline && MatchesTarget(router, target);

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
                if (!automatic && !IsDisposed)
                {
                    MessageBox.Show(
                        "The selected lock did not restore stable internet and LTE within " +
                        $"{_settings.CellLockValidationSeconds} seconds. The previous MR600 " +
                        "settings were restored.",
                        "Cell Lock rolled back",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                return;
            }

            ClearPendingCellLock();
            AddCellLockEvent(
                $"{recommendation.Band} {profileKind} validated successfully");
            if (!automatic && !IsDisposed)
            {
                MessageBox.Show(
                    $"The selected MR600 {profileKind} is active and connectivity " +
                    "passed validation.",
                    "Cell Lock active",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
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
            if (!automatic && !IsDisposed)
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

    private void OpenRouterCellLockPage()
    {
        try
        {
            Uri baseUri = new(_settings.TpLinkRouterAddress, UriKind.Absolute);
            Uri cellLockUri = new(baseUri, "main/ltecelllock.htm");
            Process.Start(new ProcessStartInfo(cellLockUri.ToString())
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "The router Cell Lock page could not be opened.\r\n\r\n" + ex.Message,
                "TP-Link Cell Lock",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

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

        _routerConnectionState.Text = telemetry.Status.ToUpperInvariant();
        _routerConnectionState.BackColor = telemetry.IsConnected
            ? Color.SeaGreen
            : telemetry.Status.Contains("connect", StringComparison.OrdinalIgnoreCase)
                ? Color.DarkGoldenrod
                : telemetry.Status.Equals("Disabled", StringComparison.OrdinalIgnoreCase)
                    ? Color.DimGray
                    : Color.Firebrick;

        string versionDetails = telemetry.HardwareVersion != "Unknown" ||
                                telemetry.FirmwareVersion != "Unknown"
            ? $"{telemetry.HardwareVersion} • {telemetry.FirmwareVersion}"
            : "TP-Link Archer MR600 protected local telemetry";
        _routerDetails.Text = string.IsNullOrWhiteSpace(telemetry.Error)
            ? versionDetails
            : telemetry.Error;

        _routerMetrics["Status"].Text = DisplayValue(telemetry.Status);
        _routerMetrics["Isp"].Text = DisplayValue(telemetry.Isp);
        _routerMetrics["Network"].Text = DisplayValue(telemetry.NetworkType);
        _routerMetrics["Band"].Text = DisplayValue(telemetry.Band);
        _routerMetrics["Signal"].Text = telemetry.SignalPercent.HasValue
            ? telemetry.SignalPercent.Value.ToString(CultureInfo.CurrentCulture) + "%"
            : "";
        _routerMetrics["Rsrp"].Text = FormatMeasurement(telemetry.RsrpDbm, "dBm");
        _routerMetrics["Rsrq"].Text = FormatMeasurement(telemetry.RsrqDb, "dB");
        _routerMetrics["Snr"].Text = FormatMeasurement(telemetry.SnrDb, "dB");
        _routerMetrics["Pci"].Text = DisplayValue(telemetry.Pci);
        _routerMetrics["Cell"].Text = DisplayValue(telemetry.CellId);
        _routerMetrics["Earfcn"].Text = DisplayValue(telemetry.Earfcn);
        _routerMetrics["Sim"].Text = DisplayValue(telemetry.SimStatus);
        _routerMetrics["Data"].Text = FormatBytes(telemetry.TotalBytes);
        _routerMetrics["RouterUpload"].Text = FormatRate(telemetry.UploadBytesPerSecond);
        _routerMetrics["RouterDownload"].Text = FormatRate(telemetry.DownloadBytesPerSecond);
        _routerMetrics["Updated"].Text = telemetry.IsConnected
            ? telemetry.Timestamp.ToString("HH:mm:ss", CultureInfo.CurrentCulture)
            : "";
    }

    private void RefreshInternetConnectionDashboard()
    {
        MonitorSnapshot snapshot = _engine.GetSnapshot();
        string access = GetAccessTechnologyLabel();
        bool dsl = _settings.ConnectionDetailsView == "Dsl";
        LocalLinkInfo detectedLink = LocalNetworkInfo.ReadActiveLink();
        string selectedLink = _settings.LocalLinkView switch
        {
            "Wifi" => "Wi-Fi",
            "Ethernet" => "Ethernet",
            _ => detectedLink.Kind
        };

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
                ("RouterDownload", "PC LINK"), ("Updated", "NEGOTIATED LINK SPEED"));
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
                ("RouterDownload", "PC LINK"), ("Updated", "NEGOTIATED LINK SPEED"));
        }

        string status = snapshot.IsPaused
            ? "Paused"
            : snapshot.IsOnline ? "Online" : "Offline";
        _routerConnectionState.Text = status.ToUpperInvariant();
        _routerConnectionState.BackColor = snapshot.IsPaused
            ? Color.DarkOrange
            : snapshot.IsOnline ? Color.SeaGreen : Color.Firebrick;
        _routerDetails.Text =
            $"{access} access • {selectedLink} PC link" +
            (detectedLink.AdapterName == "-" ? "" : $" • {detectedLink.AdapterName}") +
            ". Line values require a compatible router or ONT provider.";

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
        _routerMetrics["RouterDownload"].Text = selectedLink;
        _routerMetrics["Updated"].Text = FormatLinkSpeed(detectedLink.SpeedBitsPerSecond);
    }

    private bool IsLteConnectionView() =>
        _connectionViewInput.SelectedIndex == 0;

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

    private async Task RunSpeedTestAsync(bool manual, string? automaticReason = null)
    {
        if (_speedBusy)
            return;

        _speedBusy = true;
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
            _speedButton.Text = "Run speed test now";
            _nextAutomaticSpeedTest = GetNextSpeedTime();
            RefreshDashboard();
        }
    }

    private void CheckAutomaticSpeedTest()
    {
        if (_speedBusy)
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
        if (_publicIpCheckBusy || DateTime.UtcNow < _nextPublicIpCheckUtc)
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
                _automaticSpeedTests.ObservePublicIp(address, DateTime.UtcNow);
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
        return _nextAutomaticSpeedTest.ToString("HH:mm:ss");
    }

    private void AddEventToGrid(MonitorEvent evt)
    {
        _eventsGrid.Rows.Insert(
            0,
            evt.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
            evt.Kind,
            evt.Message);

        while (_eventsGrid.Rows.Count > 500)
            _eventsGrid.Rows.RemoveAt(_eventsGrid.Rows.Count - 1);
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
        _routerAddressInput.Text = _settings.TpLinkRouterAddress;
        RefreshCellHistory(force: true);
        _routerSetupButton.Text = string.IsNullOrWhiteSpace(_routerPassword)
            ? "Add protected password..."
            : "Update protected password...";
        _connectionViewInput.SelectedIndex = _settings.ConnectionDetailsView switch
        {
            "Dsl" => 1,
            "Fiber" => 2,
            _ => 0
        };
        _localLinkInput.SelectedIndex = _settings.LocalLinkView switch
        {
            "Wifi" => 1,
            "Ethernet" => 2,
            _ => 0
        };
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
                "Enable TP-Link MR600 live monitoring before automatic Cell Lock.",
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

        using var dialog = new RouterSetupForm(_settings, _routerPassword, firstRun);
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
        _trayIcon.BalloonTipClicked += (_, _) => ShowSmsTab();
    }

    private void HandleUnreadSmsCount(int? unreadCount)
    {
        if (!unreadCount.HasValue)
            return;
        int count = unreadCount.Value;
        bool increased = count > _lastUnreadSmsCount;
        bool firstKnown = _lastUnreadSmsCount < 0;
        _lastUnreadSmsCount = count;
        if (count > 0 && (increased || firstKnown))
        {
            _trayIcon.ShowBalloonTip(
                5000,
                "Unread SIM message",
                count == 1
                    ? "You have 1 unread SMS. Click to open the inbox."
                    : $"You have {count} unread SMS messages. Click to open the inbox.",
                ToolTipIcon.Info);
        }
        if (_tabs.SelectedTab?.Text == "SMS" && increased)
            _ = RefreshSmsInboxAsync(showErrors: false);
    }

    private void ShowSmsTab()
    {
        RestoreFromTray();
        TabPage? sms = _tabs.TabPages
            .Cast<TabPage>()
            .FirstOrDefault(page => page.Text == "SMS");
        if (sms is not null)
            _tabs.SelectedTab = sms;
    }

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

    private void RestoreFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
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

        _allowExit = true;
        _uiTimer.Stop();
        _speedCancellation?.Cancel();
        _engine.Dispose();
        _routerMonitor.Dispose();
        _cellHistory.Dispose();
        _smsUnreadFont.Dispose();
        _cellGroupFont.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
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

    private sealed record CellHistoryGroupRow(string Key);
}
