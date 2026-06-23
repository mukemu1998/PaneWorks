namespace PaneWorks.Core.Models;

public sealed record SplitNode(
    string Id,
    SplitDirection Direction,
    double Ratio,
    LayoutNode First,
    LayoutNode Second) : LayoutNode(Id);

