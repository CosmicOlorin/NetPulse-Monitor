using QRCoder;

namespace NetPulseMonitor;

internal sealed class CompanionSetupForm : Form
{
    private readonly CheckBox _enabled = new();
    private readonly NumericUpDown _port = new();
    private readonly TextBox _pairing = new();
    private readonly PictureBox _qr = new();
    private readonly PictureBox _downloadQr = new();
    private string _secret;

    public bool CompanionEnabled => _enabled.Checked;
    public int CompanionPort => (int)_port.Value;
    public string PairingSecret => _secret;

    public CompanionSetupForm(AppSettings settings, string pairingSecret, NetPulseTheme theme)
    {
        _secret = pairingSecret;
        Text = "Mobile Companion";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1100, 470);
        MinimumSize = new Size(980, 500);
        Font = new Font("Segoe UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 4,
            RowCount = 6
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "NetPulse Mobile Companion",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(title, 0, 0);
        layout.SetColumnSpan(title, 2);

        layout.Controls.Add(new Label { Text = "PAIR THIS PHONE", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9F, FontStyle.Bold) }, 2, 0);
        layout.Controls.Add(new Label { Text = "DOWNLOAD ANDROID APP", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9F, FontStyle.Bold) }, 3, 0);

        _qr.Dock = DockStyle.Fill;
        _qr.SizeMode = PictureBoxSizeMode.Zoom;
        _qr.Padding = new Padding(12);
        layout.Controls.Add(_qr, 2, 1);
        layout.SetRowSpan(_qr, 5);

        _downloadQr.Dock = DockStyle.Fill;
        _downloadQr.SizeMode = PictureBoxSizeMode.Zoom;
        _downloadQr.Padding = new Padding(12);
        layout.Controls.Add(_downloadQr, 3, 1);
        layout.SetRowSpan(_downloadQr, 5);

        _enabled.Text = "Allow paired phones on this Wi-Fi/LAN";
        _enabled.Checked = settings.CompanionEnabled;
        _enabled.Dock = DockStyle.Fill;
        AddRow(layout, 1, "Service", _enabled);

        _port.Minimum = 1024;
        _port.Maximum = 65535;
        _port.Value = settings.CompanionPort;
        _port.Dock = DockStyle.Fill;
        _port.ValueChanged += (_, _) => RefreshPairingUri();
        AddRow(layout, 2, "Local port", _port);

        _pairing.ReadOnly = true;
        _pairing.Multiline = true;
        _pairing.Dock = DockStyle.Fill;
        AddRow(layout, 3, "Persistent pairing", _pairing);

        var pairingActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        var copy = new Button { Text = "Copy pairing code", AutoSize = true };
        copy.Click += (_, _) => Clipboard.SetText(_pairing.Text);
        var regenerate = new Button { Text = "Revoke all and regenerate", AutoSize = true };
        regenerate.Click += (_, _) =>
        {
            if (MessageBox.Show(
                    "Every currently paired phone will lose access. Continue?",
                    "Regenerate pairing",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            _secret = CompanionService.CreatePairingSecret();
            RefreshPairingUri();
        };
        pairingActions.Controls.Add(copy);
        pairingActions.Controls.Add(regenerate);
        layout.Controls.Add(pairingActions, 1, 4);

        var explanation = new Label
        {
            Text = "This pairing does not expire automatically. It remains valid until you revoke it here. " +
                   "The TP-Link password is never sent to the phone. The first mobile protocol is read-only, " +
                   "uses signed requests, replay protection and encrypted payloads, and never opens an Internet service.",
            Dock = DockStyle.Fill,
            AutoSize = false
        };
        layout.Controls.Add(explanation, 0, 5);
        layout.SetColumnSpan(explanation, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(20, 9, 20, 9)
        };
        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, Width = 120, Height = 34 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 120, Height = 34 };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        root.Controls.Add(layout, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(root);
        RefreshPairingUri();
        AppThemeManager.Apply(this, theme);
    }

    private void RefreshPairingUri()
    {
        _pairing.Text = CompanionService.BuildPairingUri((int)_port.Value, _secret);
        using var generator = new QRCodeGenerator();
        using QRCodeData data = generator.CreateQrCode(_pairing.Text, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        using var stream = new MemoryStream(png.GetGraphic(8));
        using var image = Image.FromStream(stream);
        Image? previous = _qr.Image;
        _qr.Image = new Bitmap(image);
        previous?.Dispose();

        string downloadUrl = CompanionService.BuildAndroidDownloadUri((int)_port.Value);
        using QRCodeData downloadData = generator.CreateQrCode(downloadUrl, QRCodeGenerator.ECCLevel.Q);
        var downloadPng = new PngByteQRCode(downloadData);
        using var downloadStream = new MemoryStream(downloadPng.GetGraphic(8));
        using var downloadImage = Image.FromStream(downloadStream);
        Image? previousDownload = _downloadQr.Image;
        _downloadQr.Image = new Bitmap(downloadImage);
        previousDownload?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _qr.Image?.Dispose();
            _downloadQr.Image?.Dispose();
        }
        base.Dispose(disposing);
    }

    private static void AddRow(TableLayoutPanel layout, int row, string caption, Control control)
    {
        layout.Controls.Add(new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        }, 0, row);
        layout.Controls.Add(control, 1, row);
    }
}
