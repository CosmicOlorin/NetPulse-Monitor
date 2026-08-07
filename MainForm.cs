using System.Diagnostics;

namespace NetPulseMonitor;

internal sealed class MainForm : Form
{
    private readonly CsvLogger _logger = new();
    private AppSettings _settings = AppSettings.Load();
    private MonitorEngine _engine;

    private readonly Dictionary<string, Label> _metrics = new();
    private readonly PingChartControl _chart = new();
    private readonly DataGridView _eventsGrid = new();
    private readonly Label _statusBadge = new();
    private readonly Label _footer = new();
    private readonly Button _speedButton = new();
    private readonly Button _pauseButton = new();
    private readonly NotifyIcon _trayIcon = new();
    private readonly System.Windows.Forms.Timer _uiTimer = new();

    private readonly TextBox _targetInput = new();
    private readonly NumericUpDown _pingIntervalInput = new();
    private readonly NumericUpDown _failureInput = new();
    private readonly NumericUpDown _speedIntervalInput = new();
    private readonly NumericUpDown _downloadSizeInput = new();
    private readonly NumericUpDown _uploadSizeInput = new();
    private readonly CheckBox _startupInput = new();
    private readonly CheckBox _trayInput = new();

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

    public MainForm()
    {
        _settings.Normalize();
        _engine = CreateEngine();

        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "NetPulse Monitor";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1050, 720);
        Size = new Size(1260, 810);
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
            CheckAutomaticSpeedTest();
        };

        Shown += (_, _) =>
        {
            _nextAutomaticSpeedTest = GetNextSpeedTime();
            _engine.Start();
            _uiTimer.Start();
            RefreshDashboard();
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
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(new Action(() => AddEventToGrid(evt)));
        };

        return engine;
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

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(16, 7)
        };

        tabs.TabPages.Add(BuildDashboardTab());
        tabs.TabPages.Add(BuildEventsTab());
        tabs.TabPages.Add(BuildDiagnosticsTab());
        tabs.TabPages.Add(BuildSettingsTab());

        Controls.Add(tabs);
        tabs.BringToFront();

        var footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 38,
            BackColor = Color.White
        };

        _footer.AutoSize = false;
        _footer.Dock = DockStyle.Fill;
        _footer.ForeColor = Color.DimGray;
        _footer.Padding = new Padding(10, 0, 10, 0);
        _footer.TextAlign = ContentAlignment.MiddleLeft;
        _footer.Text = "Logs: " + _logger.LogFolder;
        footerPanel.Controls.Add(_footer);
        Controls.Add(footerPanel);
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
            Padding = new Padding(20)
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 430,
            ColumnCount = 2,
            RowCount = 9,
            BackColor = Color.White,
            Padding = new Padding(15)
        };

        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));

        _pingIntervalInput.Minimum = 1;
        _pingIntervalInput.Maximum = 300;
        _failureInput.Minimum = 1;
        _failureInput.Maximum = 20;
        _speedIntervalInput.Minimum = 0;
        _speedIntervalInput.Maximum = 1440;
        _downloadSizeInput.Minimum = 1;
        _downloadSizeInput.Maximum = 100;
        _uploadSizeInput.Minimum = 1;
        _uploadSizeInput.Maximum = 50;

        AddSettingRow(grid, 0, "Ping target", _targetInput);
        AddSettingRow(grid, 1, "Ping interval (seconds)", _pingIntervalInput);
        AddSettingRow(grid, 2, "Failures required for outage", _failureInput);
        AddSettingRow(grid, 3,
            "Automatic speed-test interval (minutes; 0 = off)",
            _speedIntervalInput);
        AddSettingRow(grid, 4, "Download sample (MB)", _downloadSizeInput);
        AddSettingRow(grid, 5, "Upload sample (MB)", _uploadSizeInput);
        AddSettingRow(grid, 6, "Start with Windows", _startupInput);
        AddSettingRow(grid, 7, "Minimize to system tray", _trayInput);

        var saveButton = new Button
        {
            Text = "Save settings",
            Dock = DockStyle.Fill,
            Height = 42
        };

        saveButton.Click += (_, _) => SaveSettings();
        grid.Controls.Add(saveButton, 0, 8);
        grid.SetColumnSpan(saveButton, 2);

        page.Controls.Add(grid);
        return page;
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
            Text = "-",
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
        control.Margin = new Padding(3, 8, 3, 8);

        grid.Controls.Add(label, 0, row);
        grid.Controls.Add(control, 1, row);
    }

    private void RefreshDashboard()
    {
        MonitorSnapshot snapshot = _engine.GetSnapshot();

        _statusBadge.Text = snapshot.IsPaused
            ? "PAUSED"
            : snapshot.IsOnline ? "ONLINE" : "OFFLINE";

        _statusBadge.BackColor = snapshot.IsPaused
            ? Color.DarkOrange
            : snapshot.IsOnline ? Color.SeaGreen : Color.Firebrick;

        _metrics["Ping"].Text =
            snapshot.CurrentPingMs.HasValue ? snapshot.CurrentPingMs + " ms" : "-";
        _metrics["Jitter"].Text = snapshot.JitterMs.ToString("0.0") + " ms";
        _metrics["Loss"].Text = snapshot.PacketLossPercent.ToString("0.0") + "%";
        _metrics["SuccessFail"].Text =
            snapshot.SuccessfulPings + " / " + snapshot.FailedPings;
        _metrics["RunTime"].Text = FormatDuration(snapshot.RunTime);
        _metrics["Downtime"].Text = FormatDuration(snapshot.TotalDowntime);
        _metrics["Availability"].Text =
            snapshot.AvailabilityPercent.ToString("0.000") + "%";
        _metrics["Outages"].Text = snapshot.Outages.ToString();

        _footer.Text =
            $"Target: {_settings.PingTarget}   •   " +
            $"Next automatic speed test: {FormatNextSpeedTest()}   •   " +
            $"Logs: {_logger.LogFolder}";
    }

    private async Task RunSpeedTestAsync(bool manual)
    {
        if (_speedBusy)
            return;

        _speedBusy = true;
        _speedCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        _speedButton.Text = "Cancel speed test";
        AddEventToGrid(new MonitorEvent
        {
            Kind = "SPEED",
                Message =
                    $"Speed test started: {_settings.DownloadSampleMegabytes} MB down / " +
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

            string message =
                $"Speed result ({result.Provider}): " +
                $"{(result.DownloadMbps.HasValue ? result.DownloadMbps.Value.ToString("0.00") : "N/A")} Mbps down, " +
                $"{(result.UploadMbps.HasValue ? result.UploadMbps.Value.ToString("0.00") : "N/A")} Mbps up";

            if (!string.IsNullOrWhiteSpace(result.Warning))
                message += " — " + result.Warning;

            AddEventToGrid(new MonitorEvent { Kind = "SPEED", Message = message });
        }
        catch (OperationCanceledException)
        {
            AddEventToGrid(new MonitorEvent
            {
                Kind = "SPEED",
                Message = "Speed test cancelled or timed out"
            });
        }
        catch (Exception ex)
        {
            AddEventToGrid(new MonitorEvent
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
        if (_settings.SpeedTestIntervalMinutes <= 0 ||
            _speedBusy ||
            DateTime.Now < _nextAutomaticSpeedTest)
            return;

        MonitorSnapshot snapshot = _engine.GetSnapshot();
        if (!snapshot.IsOnline || snapshot.IsPaused)
            return;

        _ = RunSpeedTestAsync(manual: false);
    }

    private DateTime GetNextSpeedTime()
    {
        return _settings.SpeedTestIntervalMinutes <= 0
            ? DateTime.MaxValue
            : DateTime.Now.AddMinutes(_settings.SpeedTestIntervalMinutes);
    }

    private string FormatNextSpeedTest()
    {
        if (_settings.SpeedTestIntervalMinutes <= 0)
            return "disabled";
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
        _downloadSizeInput.Value = _settings.DownloadSampleMegabytes;
        _uploadSizeInput.Value = _settings.UploadSampleMegabytes;
        _startupInput.Checked = _settings.StartWithWindows;
        _trayInput.Checked = _settings.MinimizeToTray;
    }

    private void SaveSettings()
    {
        _settings.PingTarget = _targetInput.Text;
        _settings.PingIntervalSeconds = (int)_pingIntervalInput.Value;
        _settings.FailuresForOutage = (int)_failureInput.Value;
        _settings.SpeedTestIntervalMinutes = (int)_speedIntervalInput.Value;
        _settings.DownloadSampleMegabytes = (int)_downloadSizeInput.Value;
        _settings.UploadSampleMegabytes = (int)_uploadSizeInput.Value;
        _settings.StartWithWindows = _startupInput.Checked;
        _settings.MinimizeToTray = _trayInput.Checked;
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

        MessageBox.Show(
            "Settings saved.",
            "NetPulse Monitor",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
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
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }

    private static string FormatDuration(TimeSpan duration) =>
        $"{(int)duration.TotalDays:00}d {duration.Hours:00}h " +
        $"{duration.Minutes:00}m {duration.Seconds:00}s";
}
