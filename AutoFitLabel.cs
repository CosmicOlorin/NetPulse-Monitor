namespace NetPulseMonitor;

/// <summary>Fits a single-line metric value to its current card at any DPI.</summary>
internal sealed class AutoFitLabel : Label
{
    private const float MinimumFontSize = 7F;
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

    protected override void OnTextAlignChanged(EventArgs e)
    {
        base.OnTextAlignChanged(e);
        Invalidate();
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        FitText();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
            FitText();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BeginInvoke(new Action(FitText));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        FitText();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        FitText();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // A parent layout can settle its final display rectangle without
        // raising another child SizeChanged. Paint is the first point at which
        // the card's inner bounds are guaranteed to be final, so make one
        // idempotent fit pass against those actual bounds.
        FitText();

        // Draw the value ourselves with TextRenderer. The stock Label renderer
        // can retain a stale glyph clip region after a DPI/layout transition;
        // that is why descenders previously reappeared only after a resize.
        Rectangle bounds = new(
            Padding.Left,
            Padding.Top,
            Math.Max(0, ClientSize.Width - Padding.Horizontal),
            Math.Max(0, ClientSize.Height - Padding.Vertical));
        TextFormatFlags flags = TextFormatFlags.NoPrefix |
                                TextFormatFlags.SingleLine |
                                GetAlignmentFlags();
        if (AutoEllipsis)
            flags |= TextFormatFlags.EndEllipsis;
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, ForeColor, flags);
    }

    private TextFormatFlags GetAlignmentFlags() => TextAlign switch
    {
        ContentAlignment.TopLeft => TextFormatFlags.Top | TextFormatFlags.Left,
        ContentAlignment.TopCenter => TextFormatFlags.Top | TextFormatFlags.HorizontalCenter,
        ContentAlignment.TopRight => TextFormatFlags.Top | TextFormatFlags.Right,
        ContentAlignment.MiddleCenter => TextFormatFlags.VerticalCenter |
                                         TextFormatFlags.HorizontalCenter,
        ContentAlignment.MiddleRight => TextFormatFlags.VerticalCenter |
                                        TextFormatFlags.Right,
        ContentAlignment.BottomLeft => TextFormatFlags.Bottom | TextFormatFlags.Left,
        ContentAlignment.BottomCenter => TextFormatFlags.Bottom |
                                         TextFormatFlags.HorizontalCenter,
        ContentAlignment.BottomRight => TextFormatFlags.Bottom | TextFormatFlags.Right,
        _ => TextFormatFlags.VerticalCenter | TextFormatFlags.Left
    };

    private void FitText()
    {
        if (_fitting || IsDisposed || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            return;

        _fitting = true;
        try
        {
            int availableWidth = Math.Max(1, ClientSize.Width - Padding.Horizontal);
            // DrawText uses the same measured GDI bounds. One scaled safety
            // pixel protects the bottom edge without shrinking compact cards
            // to an unreadable five-point value.
            int verticalSafety = Math.Max(
                1,
                (int)Math.Ceiling(DeviceDpi / 192F));
            int availableHeight = Math.Max(
                1,
                ClientSize.Height - Padding.Vertical - verticalSafety);
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
            // Size.Empty requests the natural single-line bounds. Supplying
            // int.MaxValue here makes GDI attempt an enormous layout surface;
            // it can stall the UI thread and report unusable dimensions,
            // leaving the label stuck at the minimum font size.
            Size.Empty,
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine);
        return measured.Width <= width && measured.Height <= height;
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
