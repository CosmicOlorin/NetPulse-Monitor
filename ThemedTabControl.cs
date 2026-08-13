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
        // are owner-drawn. Cover both the frame around DisplayRectangle and the
        // control's complete outer edge. The latter matters at non-100% DPI:
        // Windows can round the native frame outward and otherwise leave one or
        // more bright pixels along the left edge of the window.
        Rectangle display = DisplayRectangle;
        int frameThickness = Math.Max(4, LogicalToDeviceUnits(4));
        int outerThickness = Math.Max(6, LogicalToDeviceUnits(6));
        int left = Math.Max(0, display.Left - frameThickness);
        int top = Math.Max(0, display.Top - frameThickness);
        int right = Math.Min(ClientSize.Width, display.Right + frameThickness);
        int bottom = Math.Min(ClientSize.Height, display.Bottom + frameThickness);
        using var frameBrush = new SolidBrush(_frameColor);
        graphics.FillRectangle(frameBrush, left, top,
            Math.Max(0, right - left), frameThickness);
        graphics.FillRectangle(frameBrush, left, top,
            frameThickness, Math.Max(0, bottom - top));
        graphics.FillRectangle(frameBrush, Math.Max(left, right - frameThickness), top,
            Math.Min(frameThickness, Math.Max(0, right - left)), Math.Max(0, bottom - top));
        graphics.FillRectangle(frameBrush, left, Math.Max(top, bottom - frameThickness),
            Math.Max(0, right - left), Math.Min(frameThickness, Math.Max(0, bottom - top)));

        int pageTop = Math.Max(headerHeight, top);
        int pageHeight = Math.Max(0, ClientSize.Height - pageTop);
        graphics.FillRectangle(frameBrush, 0, pageTop,
            Math.Min(outerThickness, ClientSize.Width), pageHeight);
        graphics.FillRectangle(frameBrush,
            Math.Max(0, ClientSize.Width - outerThickness), pageTop,
            Math.Min(outerThickness, ClientSize.Width), pageHeight);
        graphics.FillRectangle(frameBrush, 0,
            Math.Max(pageTop, ClientSize.Height - outerThickness),
            ClientSize.Width, Math.Min(outerThickness, pageHeight));
    }
}
