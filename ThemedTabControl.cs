namespace NetPulseMonitor;

/// <summary>Paints the native tab-strip remainder so dark mode has no white gap.</summary>
internal sealed class ThemedTabControl : TabControl
{
    private const int WmPaint = 0x000F;
    private Color _headerBackColor = SystemColors.Control;
    private Color _frameColor = SystemColors.ControlDark;

    public Color HeaderBackColor
    {
        get => _headerBackColor;
        set
        {
            if (_headerBackColor == value)
                return;
            _headerBackColor = value;
            Invalidate();
        }
    }

    public Color FrameColor
    {
        get => _frameColor;
        set
        {
            if (_frameColor == value)
                return;
            _frameColor = value;
            Invalidate();
        }
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg == WmPaint && IsHandleCreated && TabCount > 0)
            PaintChrome();
    }

    private void PaintChrome()
    {
        Rectangle last = GetTabRect(TabCount - 1);
        int headerHeight = Math.Max(last.Bottom, DisplayRectangle.Top);
        using Graphics graphics = CreateGraphics();
        using var brush = new SolidBrush(_headerBackColor);
        if (ClientSize.Width > 0 && headerHeight > 0)
            graphics.FillRectangle(brush, 0, 0, ClientSize.Width, headerHeight);

        // Owner-draw callbacks only receive the inner native tab bounds. The
        // Win32 control can still paint bright two-pixel seams outside those
        // bounds, especially at 125%/150% DPI. Repaint the complete header after
        // the native pass so no system-theme edge can remain visible.
        bool dark = ForeColor.GetBrightness() > 0.55F;
        for (int index = 0; index < TabCount; index++)
        {
            Rectangle tab = GetTabRect(index);
            bool selected = index == SelectedIndex;
            Color tabBack = dark
                ? selected ? Color.FromArgb(35, 55, 69) : Color.FromArgb(23, 29, 36)
                : selected ? Color.White : SystemColors.Control;
            Color tabBorder = dark
                ? selected ? Color.FromArgb(43, 112, 151) : Color.FromArgb(35, 42, 51)
                : SystemColors.ControlDark;
            Color tabText = dark ? Color.FromArgb(238, 242, 247) : ForeColor;
            using var tabBrush = new SolidBrush(tabBack);
            using var tabPen = new Pen(tabBorder);
            graphics.FillRectangle(tabBrush, tab);
            graphics.DrawRectangle(tabPen, tab.X, tab.Y,
                Math.Max(0, tab.Width - 1), Math.Max(0, tab.Height - 1));
            TextRenderer.DrawText(
                graphics,
                TabPages[index].Text,
                Font,
                tab,
                tabText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);
        }

        // The native WinForms tab draws a bright 3-D frame even when its pages
        // are owner-drawn. Cover that frame with the palette border so dark mode
        // never leaves a white rectangle around the active page.
        Rectangle display = DisplayRectangle;
        int left = Math.Max(0, display.Left - 3);
        int top = Math.Max(0, display.Top - 3);
        int right = Math.Min(ClientSize.Width, display.Right + 3);
        int bottom = Math.Min(ClientSize.Height, display.Bottom + 3);
        using var frameBrush = new SolidBrush(_frameColor);
        graphics.FillRectangle(frameBrush, left, top, Math.Max(0, right - left), 4);
        graphics.FillRectangle(frameBrush, left, top, 4, Math.Max(0, bottom - top));
        graphics.FillRectangle(frameBrush, Math.Max(left, right - 4), top,
            Math.Min(4, Math.Max(0, right - left)), Math.Max(0, bottom - top));
        graphics.FillRectangle(frameBrush, left, Math.Max(top, bottom - 4),
            Math.Max(0, right - left), Math.Min(4, Math.Max(0, bottom - top)));
    }
}
