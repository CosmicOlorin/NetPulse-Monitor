namespace NetPulseMonitor;

/// <summary>
/// Draws readable cue text with the active input palette. Native Win32 cue text
/// uses a fixed gray that becomes illegible on dark backgrounds.
/// </summary>
internal sealed class CueTextBox : TextBox
{
    private const int WmPaint = 0x000F;
    private string _cueText = "";

    public string CueText
    {
        get => _cueText;
        set
        {
            _cueText = value ?? "";
            Invalidate();
        }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg != WmPaint || Focused || TextLength > 0 ||
            string.IsNullOrEmpty(_cueText) || ClientSize.Width <= 6)
            return;

        using Graphics graphics = CreateGraphics();
        Color cueColor = BackColor.GetBrightness() < 0.45F
            ? Color.FromArgb(190, 200, 212)
            : SystemColors.GrayText;
        var bounds = new Rectangle(
            3,
            0,
            Math.Max(0, ClientSize.Width - 6),
            ClientSize.Height);
        TextRenderer.DrawText(
            graphics,
            _cueText,
            Font,
            bounds,
            cueColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
