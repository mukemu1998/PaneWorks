using PaneWorks.Core.Models;

namespace PaneWorks.Infrastructure.Windows;

public sealed record DisplayInfo(
    string Id,
    string DeviceName,
    string Name,
    PaneRect Bounds,
    PaneRect WorkArea,
    bool IsPrimary,
    WorkspaceDisplayOrientation Orientation);
