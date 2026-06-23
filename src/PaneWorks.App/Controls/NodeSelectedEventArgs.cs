namespace PaneWorks.App.Controls;

public sealed class NodeSelectedEventArgs : EventArgs
{
    public NodeSelectedEventArgs(string nodeId)
    {
        NodeId = nodeId;
    }

    public string NodeId { get; }
}

