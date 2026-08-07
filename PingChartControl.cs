using System.Drawing.Drawing2D;

namespace NetPulseMonitor;

internal sealed class PingChartControl : Control
{
    private readonly List<long?> _samples = new();
    private readonly object _gate = new();

    public PingChartControl()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        ForeColor = Color.FromArgb(25, 32, 42);
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    public void AddSample(long? latency)
    {
        lock (_gate)
        {
            _samples.Add(latency);
            while (_samples.Count > 180)
                _samples.RemoveAt(0);
        }
        Invalidate();
    }

    public void ClearSamples()
    {
        lock (_gate)
            _samples.Clear();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);

        using var titleFont = new Font("Segoe UI", 10, FontStyle.Bold);
        using var smallFont = new Font("Segoe UI", 8);
        using var axisPen = new Pen(Color.FromArgb(220, 225, 232));
        using var graphPen = new Pen(Color.FromArgb(35, 110, 195), 2);
        using var downPen = new Pen(Color.Firebrick, 2);

        e.Graphics.DrawString("Live ping history — last 180 samples",
            titleFont, Brushes.Black, 12, 10);

        Rectangle chart = new(
            55, 42,
            Math.Max(80, Width - 75),
            Math.Max(80, Height - 75));

        e.Graphics.DrawRectangle(axisPen, chart);

        long?[] samples;
        lock (_gate)
            samples = _samples.ToArray();

        if (samples.Length < 2)
        {
            e.Graphics.DrawString("Waiting for measurements…",
                smallFont, Brushes.DimGray, chart.Left + 10, chart.Top + 10);
            return;
        }

        double max = Math.Max(100,
            samples.Where(x => x.HasValue)
                   .Select(x => (double)x!.Value)
                   .DefaultIfEmpty(100)
                   .Max());

        e.Graphics.DrawString($"{max:0} ms",
            smallFont, Brushes.DimGray, 5, chart.Top - 5);
        e.Graphics.DrawString("0 ms",
            smallFont, Brushes.DimGray, 12, chart.Bottom - 10);

        var currentSegment = new List<PointF>();

        for (int i = 0; i < samples.Length; i++)
        {
            float x = chart.Left +
                      i / (float)Math.Max(1, samples.Length - 1) * chart.Width;

            if (!samples[i].HasValue)
            {
                if (currentSegment.Count > 1)
                    e.Graphics.DrawLines(graphPen, currentSegment.ToArray());

                currentSegment.Clear();
                e.Graphics.DrawLine(downPen, x, chart.Top, x, chart.Bottom);
                continue;
            }

            float y = chart.Bottom -
                      (float)Math.Min(samples[i]!.Value, max) / (float)max *
                      chart.Height;

            currentSegment.Add(new PointF(x, y));
        }

        if (currentSegment.Count > 1)
            e.Graphics.DrawLines(graphPen, currentSegment.ToArray());
    }
}
