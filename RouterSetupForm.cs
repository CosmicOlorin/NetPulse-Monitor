namespace NetPulseMonitor;

internal sealed class RouterSetupForm : Form
{
    private readonly CheckBox _enabled = new();
    private readonly TextBox _address = new();
    private readonly TextBox _password = new();
    private readonly CheckBox _remember = new();
    private readonly CheckBox _showPassword = new();
    private readonly Label _status = new();
    private readonly Button _testButton = new();
    private readonly Button _saveButton = new();
    private readonly bool _firstRun;

    public bool Skipped { get; private set; }
    public bool MonitoringEnabled => _enabled.Checked;
    public string RouterAddress => _address.Text.Trim();
    public string Password => _password.Text;
    public bool RememberPassword => _remember.Checked;

    public RouterSetupForm(
        AppSettings settings,
        string? currentPassword,
        bool firstRun,
        NetPulseTheme theme)
    {
        _firstRun = firstRun;
        Text = firstRun
            ? "Set up TP-Link monitoring"
            : "TP-Link router setup";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(680, 540);
        Font = new Font("Segoe UI", 9F);

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18),
            BackColor = Color.White
        };
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        var heading = new Label
        {
            Text = firstRun
                ? "Connect NetPulse to your TP-Link router"
                : "TP-Link router monitoring",
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };
        outer.Controls.Add(heading, 0, 0);

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(0, 6, 0, 4)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var explanation = new Label
        {
            Text = "NetPulse uses the local router password only. No username is " +
                   "required. The password is never written to settings, CSV files, " +
                   "diagnostics, or installer logs.",
            AutoSize = true,
            MaximumSize = new Size(610, 0),
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 0, 6)
        };
        fields.Controls.Add(explanation, 0, 0);
        fields.SetColumnSpan(explanation, 2);

        _enabled.Text = "Enable live TP-Link monitoring";
        _enabled.Checked = settings.TpLinkRouterEnabled || firstRun;
        _enabled.Dock = DockStyle.Fill;
        fields.Controls.Add(_enabled, 0, 1);
        fields.SetColumnSpan(_enabled, 2);

        _address.Text = settings.TpLinkRouterAddress;
        _address.PlaceholderText = "http://192.168.1.1/";
        AddRow(fields, 2, "Router address", _address);

        _password.Text = currentPassword ?? "";
        _password.UseSystemPasswordChar = true;
        _password.MaxLength = 32;
        AddRow(fields, 3, "Router password", _password);

        _showPassword.Text = "Show password";
        _showPassword.Dock = DockStyle.Fill;
        _showPassword.CheckedChanged += (_, _) =>
            _password.UseSystemPasswordChar = !_showPassword.Checked;
        _remember.Text = "Protect and remember on this Windows PC";
        _remember.Dock = DockStyle.Fill;
        _remember.Checked = settings.RememberTpLinkPassword;
        var credentialOptions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        credentialOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        credentialOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        credentialOptions.Controls.Add(_showPassword, 0, 0);
        credentialOptions.Controls.Add(_remember, 1, 0);
        fields.Controls.Add(credentialOptions, 0, 4);
        fields.SetColumnSpan(credentialOptions, 2);

        var safety = new Label
        {
            Text = "Monitoring is read-only. Cell and band lock changes are separate, " +
                   "off by default, and require explicit confirmation or opt-in.",
            AutoSize = true,
            MaximumSize = new Size(610, 0),
            ForeColor = Color.DimGray
        };
        fields.Controls.Add(safety, 0, 5);
        fields.SetColumnSpan(safety, 2);
        outer.Controls.Add(fields, 0, 1);

        var testPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        testPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        testPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));

        _testButton.Text = "Test connection";
        _testButton.Size = new Size(150, 34);
        _testButton.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        _testButton.Click += async (_, _) => await TestConnectionAsync();
        testPanel.Controls.Add(_testButton, 0, 0);

        _status.Text = "Live data refreshes every second after setup.";
        _status.AutoEllipsis = true;
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = Color.DimGray;
        testPanel.Controls.Add(_status, 1, 0);
        outer.Controls.Add(testPanel, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        _saveButton.Text = "Save and continue";
        _saveButton.Size = new Size(150, 36);
        _saveButton.Click += (_, _) => SaveAndClose();

        var cancelButton = new Button
        {
            Text = firstRun ? "Skip for now" : "Cancel",
            Size = new Size(125, 36),
            DialogResult = DialogResult.Cancel
        };
        cancelButton.Click += (_, _) => Skipped = firstRun;

        buttons.Controls.Add(_saveButton);
        buttons.Controls.Add(cancelButton);
        outer.Controls.Add(buttons, 0, 3);

        Controls.Add(outer);
        AcceptButton = _saveButton;
        CancelButton = cancelButton;

        _enabled.CheckedChanged += (_, _) => UpdateEnabledState();
        UpdateEnabledState();
        InterfaceHelp.Install(this);
        AppThemeManager.Apply(this, theme);
    }

    private static void AddRow(
        TableLayoutPanel grid,
        int row,
        string caption,
        Control control)
    {
        var label = new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(3, 7, 3, 7);
        grid.Controls.Add(label, 0, row);
        grid.Controls.Add(control, 1, row);
    }

    private void UpdateEnabledState()
    {
        bool enabled = _enabled.Checked;
        _address.Enabled = enabled;
        _password.Enabled = enabled;
        _showPassword.Enabled = enabled;
        _remember.Enabled = enabled;
        _testButton.Enabled = enabled;
    }

    private void SaveAndClose()
    {
        if (_enabled.Checked)
        {
            if (!TryGetRouterUri(out _))
                return;
            if (string.IsNullOrWhiteSpace(_password.Text))
            {
                MessageBox.Show(
                    "Enter the local TP-Link router password.",
                    "TP-Link setup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                _password.Focus();
                return;
            }
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private async Task TestConnectionAsync()
    {
        if (!TryGetRouterUri(out Uri? uri))
            return;
        if (string.IsNullOrWhiteSpace(_password.Text))
        {
            MessageBox.Show(
                "Enter the local TP-Link router password before testing.",
                "TP-Link setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        SetBusy(true, "Connecting safely…");
        await using var provider = new TpLinkMr600Provider();
        try
        {
            RouterCapabilities capabilities;
            try
            {
                capabilities = await ConnectForTestAsync(
                    provider,
                    uri!,
                    allowSessionTakeover: false);
            }
            catch (RouterBusyException ex)
            {
                DialogResult replace = MessageBox.Show(
                    ex.Message + "\r\n\r\n" +
                    "Replace the existing management session? This signs the " +
                    "router webpage or Tether app out, but does not change any " +
                    "router setting.",
                    "TP-Link session in use",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (replace != DialogResult.Yes)
                {
                    _status.ForeColor = Color.DarkGoldenrod;
                    _status.Text = "Waiting for the existing router session.";
                    return;
                }

                capabilities = await ConnectForTestAsync(
                    provider,
                    uri!,
                    allowSessionTakeover: true);
            }
            _status.ForeColor = Color.SeaGreen;
            _status.Text = capabilities.SupportsLteTelemetry
                ? $"Connected: {capabilities.HardwareVersion}"
                : "Connected, but LTE telemetry was not detected.";
        }
        catch (OperationCanceledException)
        {
            ShowConnectionError("The router did not respond within 20 seconds.");
        }
        catch (RouterConnectionException ex)
        {
            ShowConnectionError(ex.Message);
        }
        catch (Exception)
        {
            ShowConnectionError("The router connection could not be tested.");
        }
        finally
        {
            SetBusy(false, _status.Text);
        }
    }

    private async Task<RouterCapabilities> ConnectForTestAsync(
        TpLinkMr600Provider provider,
        Uri uri,
        bool allowSessionTakeover)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        return await provider.ConnectAsync(
            new RouterConnectionOptions
            {
                RouterUri = uri,
                Password = _password.Text,
                AllowSessionTakeover = allowSessionTakeover
            },
            timeout.Token);
    }

    private bool TryGetRouterUri(out Uri? uri)
    {
        string value = _address.Text.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
            value = "http://" + value;
        if (Uri.TryCreate(value, UriKind.Absolute, out uri))
        {
            try
            {
                uri = TpLinkMr600Provider.NormalizeRouterUri(uri);
                _address.Text = uri.ToString();
                return true;
            }
            catch (RouterConnectionException)
            {
            }
        }

        MessageBox.Show(
            "Enter a valid local router address, for example http://192.168.1.1/.",
            "TP-Link setup",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        _address.Focus();
        return false;
    }

    private void SetBusy(bool busy, string text)
    {
        UseWaitCursor = busy;
        _testButton.Enabled = !busy && _enabled.Checked;
        _saveButton.Enabled = !busy;
        _status.Text = text;
        if (busy)
            _status.ForeColor = Color.DarkGoldenrod;
    }

    private void ShowConnectionError(string message)
    {
        _status.ForeColor = Color.Firebrick;
        _status.Text = message;
        MessageBox.Show(
            message,
            "TP-Link connection",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _password.Clear();
            _password.UseSystemPasswordChar = true;
        }
        base.Dispose(disposing);
    }
}
