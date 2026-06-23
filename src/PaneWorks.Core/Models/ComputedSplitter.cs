namespace PaneWorks.Core.Models;

public sealed record ComputedSplitter(
    string SplitNodeId,
    PaneRect Bounds,
    SplitDirection Direction,
    PaneRect HostBounds,
    double CurrentRatio);
