namespace PaneWorks.Core.Models;

public sealed record WorkspaceDisplaySnapshot(
    string DeviceName,
    PaneRect Bounds,
    bool IsPrimary,
    string? PhysicalId = null,
    WorkspaceDisplayOrientation? Orientation = null);
