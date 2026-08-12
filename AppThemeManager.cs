using System.Runtime.InteropServices;

namespace NetPulseMonitor;

internal enum NetPulseTheme
{
    System,
    Light,
    Dark
}

internal static class AppThemeManager
{
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;
    private const int WcaUseDarkModeColors = 26;
    private const int DwmColorDefault = -1;

    public static bool IsDark(NetPulseTheme theme) => theme switch
    {
        NetPulseTheme.Dark => true,
        NetPulseTheme.Light => false,
        _ => IsWindowsDarkMode()
    };

    public static void Apply(Control root, NetPulseTheme theme)
    {
        bool dark = IsDark(theme);
        Color background = dark ? Color.FromArgb(20, 25, 31) : Color.FromArgb(244, 247, 250);
        Color surface = dark ? Color.FromArgb(31, 37, 45) : Color.White;
        Color text = dark ? Color.FromArgb(235, 239, 244) : Color.FromArgb(24, 28, 33);
        Color muted = dark ? Color.FromArgb(168, 177, 188) : Color.DimGray;
        ApplyRecursive(root, background, surface, text, muted, dark);
        if (root is Form form)
        {
            form.Tag = theme;
            ApplyWindowFrame(form, theme);
            form.Shown -= ReapplyWindowFrame;
            form.Shown += ReapplyWindowFrame;
            form.Activated -= ReapplyWindowFrame;
            form.Activated += ReapplyWindowFrame;
        }
    }

    private static void ReapplyWindowFrame(object? sender, EventArgs args)
    {
        if (sender is Form form && form.Tag is NetPulseTheme theme)
            ApplyWindowFrame(form, theme);
    }

    public static void ApplyWindowFrame(Form form, NetPulseTheme theme)
    {
        form.Tag = theme;
        if (!form.IsHandleCreated || !OperatingSystem.IsWindowsVersionAtLeast(10))
            return;

        bool dark = IsDark(theme);
        int enabled = dark ? 1 : 0;
        try
        {
            // Windows 11 can leave the native caption/frame light until the
            // composition preference is set in addition to the DWM attributes.
            var composition = new WindowCompositionAttributeData
            {
                Attribute = WcaUseDarkModeColors,
                Data = Marshal.AllocHGlobal(sizeof(int)),
                SizeOfData = sizeof(int)
            };
            try
            {
                Marshal.WriteInt32(composition.Data, enabled);
                SetWindowCompositionAttribute(form.Handle, ref composition);
            }
            finally
            {
                Marshal.FreeHGlobal(composition.Data);
            }

            int result = DwmSetWindowAttribute(
                form.Handle,
                DwmUseImmersiveDarkMode,
                ref enabled,
                sizeof(int));
            if (result != 0)
            {
                DwmSetWindowAttribute(
                    form.Handle,
                    DwmUseImmersiveDarkModeBefore20H1,
                    ref enabled,
                    sizeof(int));
            }

            int caption = dark
                ? ToColorRef(Color.FromArgb(27, 32, 39))
                : DwmColorDefault;
            int border = dark
                ? ToColorRef(Color.FromArgb(27, 32, 39))
                : DwmColorDefault;
            int titleText = dark
                ? ToColorRef(Color.FromArgb(235, 239, 244))
                : DwmColorDefault;
            DwmSetWindowAttribute(
                form.Handle, DwmCaptionColor, ref caption, sizeof(int));
            DwmSetWindowAttribute(
                form.Handle, DwmBorderColor, ref border, sizeof(int));
            DwmSetWindowAttribute(
                form.Handle, DwmTextColor, ref titleText, sizeof(int));
            form.Invalidate(true);
            form.Update();
        }
        catch (DllNotFoundException)
        {
            // Older Windows versions keep their system-managed frame.
        }
        catch (EntryPointNotFoundException)
        {
            // Older Windows versions keep their system-managed frame.
        }
    }

    private static int ToColorRef(Color color) =>
        color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int value,
        int valueSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(
        IntPtr window,
        ref WindowCompositionAttributeData data);

    private static void ApplyRecursive(
        Control control,
        Color background,
        Color surface,
        Color text,
        Color muted,
        bool dark)
    {
        Color inputSurface = dark ? Color.FromArgb(45, 52, 63) : Color.White;
        switch (control)
        {
            case Form or TabPage:
                control.BackColor = background;
                control.ForeColor = text;
                break;
            case TabControl tabs:
                tabs.BackColor = background;
                tabs.ForeColor = text;
                if (tabs is ThemedTabControl themedTabs)
                {
                    themedTabs.HeaderBackColor = dark
                        ? Color.FromArgb(23, 29, 36)
                        : SystemColors.Control;
                    themedTabs.FrameColor = dark
                        ? Color.FromArgb(31, 37, 45)
                        : SystemColors.ControlDark;
                }
                tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
                tabs.DrawItem -= DrawTabItem;
                tabs.DrawItem += DrawTabItem;
                tabs.Invalidate();
                break;
            case DataGridView grid:
                grid.BackgroundColor = surface;
                grid.GridColor = dark ? Color.FromArgb(70, 77, 88) : Color.LightGray;
                grid.DefaultCellStyle.BackColor = surface;
                grid.DefaultCellStyle.ForeColor = text;
                grid.DefaultCellStyle.SelectionBackColor = dark
                    ? Color.FromArgb(28, 104, 150)
                    : SystemColors.Highlight;
                grid.DefaultCellStyle.SelectionForeColor = Color.White;
                grid.ColumnHeadersDefaultCellStyle.BackColor = dark
                    ? Color.FromArgb(45, 51, 61)
                    : SystemColors.Control;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = text;
                grid.EnableHeadersVisualStyles = !dark;
                grid.BorderStyle = dark
                    ? BorderStyle.FixedSingle
                    : BorderStyle.Fixed3D;
                break;
            case TextBoxBase textBox:
                textBox.BackColor = inputSurface;
                textBox.ForeColor = text;
                break;
            case ComboBox comboBox:
                comboBox.BackColor = inputSurface;
                comboBox.ForeColor = text;
                comboBox.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
                comboBox.DrawMode = DrawMode.OwnerDrawFixed;
                comboBox.ItemHeight = Math.Max(
                    comboBox.ItemHeight,
                    TextRenderer.MeasureText("Ag", comboBox.Font).Height + 3);
                comboBox.DrawItem -= DrawComboBoxItem;
                comboBox.DrawItem += DrawComboBoxItem;
                break;
            case NumericUpDown numeric:
                numeric.BackColor = inputSurface;
                numeric.ForeColor = text;
                break;
            case Button button:
                button.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
                button.UseVisualStyleBackColor = !dark;
                button.BackColor = dark ? Color.FromArgb(43, 51, 62) : SystemColors.Control;
                button.ForeColor = dark && !button.Enabled
                    ? Color.FromArgb(153, 163, 175)
                    : text;
                if (dark)
                {
                    button.FlatAppearance.BorderSize = 1;
                    button.FlatAppearance.BorderColor = Color.FromArgb(52, 62, 74);
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 69, 83);
                    button.FlatAppearance.MouseDownBackColor = Color.FromArgb(29, 103, 139);
                }
                break;
            case Label label:
                bool semanticBackground =
                    label.BackColor == Color.DarkGoldenrod ||
                    label.BackColor == Color.Firebrick ||
                    label.BackColor == Color.DarkOrange ||
                    label.BackColor == Color.DimGray ||
                    label.BackColor == Color.SeaGreen;
                if (!semanticBackground)
                {
                    label.ForeColor = text;
                    if (label.BackColor != Color.Transparent)
                        label.BackColor = surface;
                }
                break;
            case Panel or TableLayoutPanel or FlowLayoutPanel:
                control.BackColor = surface;
                control.ForeColor = text;
                break;
            default:
                control.ForeColor = text;
                if (control.GetType().Name == "PingChartControl")
                    control.BackColor = surface;
                break;
        }

        foreach (Control child in control.Controls)
            ApplyRecursive(child, background, surface, text, muted, dark);
    }

    private static void DrawComboBoxItem(object? sender, DrawItemEventArgs args)
    {
        if (sender is not ComboBox comboBox || args.Index < 0)
            return;

        bool selected = (args.State & DrawItemState.Selected) != 0;
        Color backColor = selected
            ? Color.FromArgb(28, 104, 150)
            : comboBox.BackColor;
        Color foreColor = selected ? Color.White : comboBox.ForeColor;
        using var background = new SolidBrush(backColor);
        args.Graphics.FillRectangle(background, args.Bounds);
        string itemText = comboBox.GetItemText(comboBox.Items[args.Index]) ?? string.Empty;
        var textBounds = new Rectangle(
            args.Bounds.X + 3,
            args.Bounds.Y,
            Math.Max(0, args.Bounds.Width - 6),
            args.Bounds.Height);
        TextRenderer.DrawText(
            args.Graphics,
            itemText,
            comboBox.Font,
            textBounds,
            foreColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
            TextFormatFlags.NoPrefix);
        args.DrawFocusRectangle();
    }

    private static void DrawTabItem(object? sender, DrawItemEventArgs args)
    {
        if (sender is not TabControl tabs || args.Index < 0 ||
            args.Index >= tabs.TabPages.Count)
            return;

        // Native TabControl ignores BackColor on themed Windows builds. ForeColor
        // is reliable because the theme manager sets it explicitly.
        bool dark = tabs.ForeColor.GetBrightness() > 0.55F;
        bool selected = args.Index == tabs.SelectedIndex;
        Color backColor = dark
            ? selected
                ? Color.FromArgb(35, 55, 69)
                : Color.FromArgb(23, 29, 36)
            : selected
                ? Color.White
                : SystemColors.Control;
        Color foreColor = dark
            ? Color.FromArgb(238, 242, 247)
            : Color.FromArgb(24, 28, 33);
        Color borderColor = dark
            ? selected
                ? Color.FromArgb(43, 112, 151)
                : Color.FromArgb(35, 42, 51)
            : SystemColors.ControlDark;

        Rectangle bounds = args.Bounds;
        using var background = new SolidBrush(backColor);
        using var border = new Pen(borderColor);
        args.Graphics.FillRectangle(background, bounds);
        args.Graphics.DrawRectangle(
            border,
            bounds.X,
            bounds.Y,
            Math.Max(0, bounds.Width - 1),
            Math.Max(0, bounds.Height - 1));
        TextRenderer.DrawText(
            args.Graphics,
            tabs.TabPages[args.Index].Text,
            tabs.Font,
            bounds,
            foreColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if ((args.State & DrawItemState.Focus) != 0)
            args.DrawFocusRectangle();
    }

    private static bool IsWindowsDarkMode()
    {
        try
        {
            object? value = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            return value is int integer && integer == 0;
        }
        catch
        {
            return false;
        }
    }
}
