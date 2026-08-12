namespace NetPulseMonitor;

internal sealed class FlickerFreeDataGridView : DataGridView
{
    public FlickerFreeDataGridView()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        SetStyle(ControlStyles.AllPaintingInWmPaint, true);
        UpdateStyles();
    }
}
