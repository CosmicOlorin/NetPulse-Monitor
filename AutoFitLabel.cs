namespace NetPulseMonitor;

/// <summary>Fits a single-line metric value to its current card at any DPI.</summary>
internal sealed class AutoFitLabel : Label
{
    private const float MinimumFontSize = 5F;
    private Font? _ownedFont;
    private bool _fitting;
    private float _maximumFontSize = 16F;

    public float MaximumFontSize
    {
        get => _maximumFontSize;
        set
        {
            _maximumFontSize = Math.Max(MinimumFontSize, value);
            FitText();
        }
    }

    public AutoFitLabel()
    {
        AutoSize = false;
        AutoEllipsis = false;
        UseMnemonic = false;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        FitText();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        FitText();
    }

    protected override void OnPaddingChanged(EventArgs e)
    {
        base.OnPaddingChanged(e);
        FitText();
    }

    private void FitText()
    {
        if (_fitting || IsDisposed || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            return;

        _fitting = true;
        try
        {
            int availableWidth = Math.Max(1, ClientSize.Width - Padding.Horizontal);
            int availableHeight = Math.Max(1, ClientSize.Height - Padding.Vertical);
            float selected = _maximumFontSize;

            if (!Fits(selected, availableWidth, availableHeight))
            {
                float low = MinimumFontSize;
                float high = _maximumFontSize;
                for (int i = 0; i < 8; i++)
                {
                    float candidate = (low + high) / 2F;
                    if (Fits(candidate, availableWidth, availableHeight))
                        low = candidate;
                    else
                        high = candidate;
                }
                selected = MathF.Floor(low * 2F) / 2F;
            }

            AutoEllipsis = !Fits(MinimumFontSize, availableWidth, availableHeight);
            if (Math.Abs(Font.SizeInPoints - selected) < 0.1F &&
                Font.Style == FontStyle.Bold)
                return;

            var replacement = new Font("Segoe UI", selected, FontStyle.Bold,
                GraphicsUnit.Point);
            Font? previous = _ownedFont;
            _ownedFont = replacement;
            Font = replacement;
            previous?.Dispose();
        }
        finally
        {
            _fitting = false;
        }
    }

    private bool Fits(float size, int width, int height)
    {
        using var candidate = new Font("Segoe UI", size, FontStyle.Bold,
            GraphicsUnit.Point);
        Size measured = TextRenderer.MeasureText(
            Text,
            candidate,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);
        // TextRenderer reports 96-DPI units while WinForms lays this control out
        // in scaled device pixels under PerMonitorV2. Account for that difference.
        float dpiScale = Math.Max(1F, DeviceDpi / 96F);
        return measured.Width * dpiScale <= width &&
               measured.Height * dpiScale <= height;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ownedFont?.Dispose();
            _ownedFont = null;
        }
        base.Dispose(disposing);
    }
}
