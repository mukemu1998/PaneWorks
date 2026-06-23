namespace PaneWorks.App.Controls;

public sealed class SnapLayoutSwitchRequestedEventArgs : EventArgs
{
    public SnapLayoutSwitchRequestedEventArgs(string layoutId)
    {
        LayoutId = layoutId;
    }

    public string LayoutId { get; }
}
