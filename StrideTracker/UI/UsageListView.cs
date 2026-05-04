namespace StrideTracker.UI;

public sealed class UsageListView : ListView
{
    public UsageListView()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        UpdateStyles();
    }
}
