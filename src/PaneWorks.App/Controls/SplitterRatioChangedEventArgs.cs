namespace PaneWorks.App.Controls;

public sealed class SplitterRatioChangedEventArgs : EventArgs
{
    public SplitterRatioChangedEventArgs(string splitNodeId, double ratio)
    {
        SplitNodeId = splitNodeId;
        Ratio = ratio;
    }

    public string SplitNodeId { get; }

    public double Ratio { get; }
}
