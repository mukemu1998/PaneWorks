namespace PaneWorks.App.Controls;

public sealed class DisplayEditRequestedEventArgs : EventArgs
{
    public DisplayEditRequestedEventArgs(string displayId)
    {
        DisplayId = displayId;
    }

    public string DisplayId { get; }
}
