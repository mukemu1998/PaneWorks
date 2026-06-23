namespace PaneWorks.App.Controls;

public sealed class CanvasContextActionRequestedEventArgs : EventArgs
{
    public CanvasContextActionRequestedEventArgs(CanvasContextAction action, string targetNodeId)
    {
        Action = action;
        TargetNodeId = targetNodeId;
    }

    public CanvasContextAction Action { get; }

    public string TargetNodeId { get; }
}
