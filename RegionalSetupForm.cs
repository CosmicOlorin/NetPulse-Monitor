using System.Globalization;

namespace NetPulseMonitor;

internal sealed class RegionalSetupForm : Form
{
    private readonly ComboBox _country = new();
    private readonly ComboBox _timeZone = new();
    private readonly Label _preview = new();
    private bool _loading;

    public string CountryCode =>
        (_country.SelectedItem as RegionalCountryOption)?.Code ?? "US";
    public string CountryCultureName =>
        (_country.SelectedItem as RegionalCountryOption)?.CultureName ?? "en-US";
    public string OfficialTimeZoneId =>
        (_timeZone.SelectedItem as RegionalTimeZoneOption)?.Id ??
        TimeZoneInfo.Local.Id;

    public RegionalSetupForm(AppSettings settings, bool firstRun)
    {
        Text = firstRun
            ? "Country and official time"
            : "Regional and ISP-report time";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(760, 460);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.White;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(22, 18, 22, 18)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heading = new Label
        {
            Text = "Official timestamps",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(heading, 0, 0);
        layout.SetColumnSpan(heading, 2);

        var explanation = new Label
        {
            Text = "Choose the country and its exact official time zone. NetPulse uses " +
                   "the country's date format and converts timestamps from UTC for the UI, " +
                   "CSV logs and ISP evidence. This does not change the Windows clock.",
            Dock = DockStyle.Fill,
            ForeColor = Color.DimGray,
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(explanation, 0, 1);
        layout.SetColumnSpan(explanation, 2);

        _country.DropDownStyle = ComboBoxStyle.DropDownList;
        _country.Dock = DockStyle.Fill;
        _country.Margin = new Padding(3, 10, 3, 10);
        foreach (RegionalCountryOption option in
                 RegionalSettingsCatalog.GetCountries())
            _country.Items.Add(option);
        AddRow(layout, 2, "Country / ISO code", _country);

        _timeZone.DropDownStyle = ComboBoxStyle.DropDownList;
        _timeZone.Dock = DockStyle.Fill;
        _timeZone.Margin = new Padding(3, 10, 3, 10);
        foreach (RegionalTimeZoneOption option in
                 RegionalSettingsCatalog.GetTimeZones())
            _timeZone.Items.Add(option);
        AddRow(layout, 3, "Official time zone", _timeZone);

        _preview.Dock = DockStyle.Fill;
        _preview.BackColor = Color.FromArgb(244, 247, 250);
        _preview.Padding = new Padding(14, 8, 14, 8);
        _preview.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_preview, 0, 4);
        layout.SetColumnSpan(_preview, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(22, 12, 22, 12),
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(244, 247, 250)
        };
        var save = new Button
        {
            Text = "Save and continue",
            DialogResult = DialogResult.OK,
            Size = new Size(170, 38),
            Margin = new Padding(8, 2, 0, 2)
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Size = new Size(110, 38),
            Margin = new Padding(8, 2, 0, 2)
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        root.Controls.Add(layout, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        AcceptButton = save;
        CancelButton = cancel;

        _loading = true;
        string countryCode = RegionalSettingsCatalog.ResolveCountry(
            settings.CountryCode).Code;
        _country.SelectedItem = _country.Items
            .Cast<RegionalCountryOption>()
            .First(item => item.Code == countryCode);
        string zoneId = RegionalSettingsCatalog.ResolveTimeZone(
            settings.OfficialTimeZoneId,
            countryCode).Id;
        SelectTimeZone(zoneId);
        _loading = false;

        _country.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading)
                SelectTimeZone(RegionalSettingsCatalog.SuggestTimeZoneId(CountryCode));
            UpdatePreview();
        };
        _timeZone.SelectedIndexChanged += (_, _) => UpdatePreview();
        Shown += (_, _) =>
        {
            FitToWorkingArea();
            UpdatePreview();
            save.Focus();
        };
    }

    private static void AddRow(
        TableLayoutPanel layout,
        int row,
        string caption,
        Control control)
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

    private void SelectTimeZone(string id)
    {
        RegionalTimeZoneOption? match = _timeZone.Items
            .Cast<RegionalTimeZoneOption>()
            .FirstOrDefault(item => item.Id.Equals(
                id, StringComparison.OrdinalIgnoreCase));
        _timeZone.SelectedItem = match ?? _timeZone.Items
            .Cast<RegionalTimeZoneOption>()
            .First(item => item.Id == TimeZoneInfo.Local.Id);
    }

    private void UpdatePreview()
    {
        if (_country.SelectedItem is not RegionalCountryOption country ||
            _timeZone.SelectedItem is not RegionalTimeZoneOption zone)
            return;
        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(country.CultureName);
            TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(zone.Id);
            DateTimeOffset current = TimeZoneInfo.ConvertTime(
                DateTimeOffset.UtcNow,
                timeZone);
            _preview.Text =
                $"ISP timestamp preview: {current.ToString(culture.DateTimeFormat.ShortDatePattern + " HH:mm:ss zzz", culture)}\r\n" +
                $"Country code: {country.Code}   |   Culture: {culture.Name}   |   Zone: {zone.Id}";
        }
        catch (Exception)
        {
            _preview.Text = "Select a valid country and official time zone.";
        }
    }

    private void FitToWorkingArea()
    {
        Rectangle working = Screen.FromControl(this).WorkingArea;
        int width = Math.Min(Width, Math.Max(420, working.Width - 24));
        int height = Math.Min(Height, Math.Max(360, working.Height - 24));
        Size = new Size(width, height);
        Location = new Point(
            working.Left + Math.Max(0, (working.Width - width) / 2),
            working.Top + Math.Max(0, (working.Height - height) / 2));
    }
}
