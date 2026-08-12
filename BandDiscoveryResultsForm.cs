using System.Globalization;

namespace NetPulseMonitor;

internal sealed class BandDiscoveryResultsForm : Form
{
    public BandDiscoveryResultsForm(
        LteBandScanPlan plan,
        IReadOnlyList<LteBandCellObservation> results,
        string csvPath,
        NetPulseTheme theme)
    {
        Text = "Band & Cell Discovery results";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 500);
        Size = new Size(1220, 680);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        int observedBands = results
            .Where(item => item.Samples > 0)
            .Select(item => item.RequestedBand)
            .Distinct()
            .Count();
        int distinctCells = results.Count(item =>
            item.Samples > 0 &&
            (item.Earfcn != "-" || item.Pci != "-" || item.CellId != "-"));
        var summary = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Text = $"{plan.RouterProfile} • {observedBands}/{plan.Bands.Count} bands available • " +
                   $"{distinctCells} distinct serving identities\r\n" +
                   "A band-only scan reports serving cells selected by the modem; it is not a hidden-neighbor RF list. " +
                   $"Full results were appended to {csvPath}"
        };

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false
        };
        AddColumn(grid, "Requested", "Requested band", 11);
        AddColumn(grid, "Serving", "Serving profile", 13);
        AddColumn(grid, "Earfcn", "EARFCN", 10);
        AddColumn(grid, "Pci", "PCI", 8);
        AddColumn(grid, "Cid", "CID", 12);
        AddColumn(grid, "Rsrp", "RSRP", 9);
        AddColumn(grid, "Rsrq", "RSRQ", 9);
        AddColumn(grid, "Snr", "SNR", 9);
        AddColumn(grid, "Samples", "Samples", 8);
        AddColumn(grid, "Status", "Result", 22);
        foreach (LteBandCellObservation item in results
                     .OrderBy(item => item.RequestedBand)
                     .ThenBy(item => item.Earfcn)
                     .ThenBy(item => item.Pci)
                     .ThenBy(item => item.CellId))
        {
            grid.Rows.Add(
                "B" + item.RequestedBand.ToString(CultureInfo.InvariantCulture),
                item.ServingProfile,
                item.Earfcn,
                item.Pci,
                item.CellId,
                FormatMeasurement(item.RsrpDbm, "dBm"),
                FormatMeasurement(item.RsrqDb, "dB"),
                FormatMeasurement(item.SnrDb, "dB"),
                item.Samples == 0 ? "-" : item.Samples.ToString(CultureInfo.InvariantCulture),
                item.Status);
        }

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0)
        };
        var close = new Button { Text = "Close", Size = new Size(120, 34) };
        close.Click += (_, _) => Close();
        var copy = new Button { Text = "Copy results", Size = new Size(140, 34) };
        copy.Click += (_, _) => Clipboard.SetText(ToTabSeparated(results));
        var openLog = new Button { Text = "Open discovery log", Size = new Size(175, 34) };
        openLog.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = csvPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Open discovery log",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        controls.Controls.Add(close);
        controls.Controls.Add(copy);
        controls.Controls.Add(openLog);

        layout.Controls.Add(summary, 0, 0);
        layout.Controls.Add(grid, 0, 1);
        layout.Controls.Add(controls, 0, 2);
        Controls.Add(layout);
        AcceptButton = close;
        AppThemeManager.Apply(this, theme);
    }

    private static void AddColumn(
        DataGridView grid,
        string name,
        string header,
        float weight) =>
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            FillWeight = weight,
            SortMode = DataGridViewColumnSortMode.Automatic
        });

    private static string FormatMeasurement(double? value, string unit) =>
        value.HasValue
            ? value.Value.ToString("0.#", CultureInfo.CurrentCulture) + " " + unit
            : "-";

    private static string ToTabSeparated(IEnumerable<LteBandCellObservation> results)
    {
        var lines = new List<string>
        {
            "Requested band\tServing profile\tPrimary band\tEARFCN\tPCI\tCID\tRSRP dBm\tRSRQ dB\tSNR dB\tSamples\tResult"
        };
        lines.AddRange(results.Select(item => string.Join('\t',
            "B" + item.RequestedBand.ToString(CultureInfo.InvariantCulture),
            item.ServingProfile,
            item.PrimaryBand,
            item.Earfcn,
            item.Pci,
            item.CellId,
            item.RsrpDbm?.ToString("0.#", CultureInfo.InvariantCulture) ?? "",
            item.RsrqDb?.ToString("0.#", CultureInfo.InvariantCulture) ?? "",
            item.SnrDb?.ToString("0.#", CultureInfo.InvariantCulture) ?? "",
            item.Samples.ToString(CultureInfo.InvariantCulture),
            item.Status)));
        return string.Join(Environment.NewLine, lines);
    }
}
