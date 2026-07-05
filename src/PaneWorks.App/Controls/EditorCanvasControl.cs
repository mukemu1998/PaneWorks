using System.Windows;
using System.Windows.Media;
using PaneWorks.App.ViewModels;
using PaneWorks.Core.Models;
using PaneWorks.Core.Services;
using WpfPoint = System.Windows.Point;

namespace PaneWorks.App.Controls;

public sealed partial class EditorCanvasControl : FrameworkElement
{
    private const double StageInset = 0;
    private const double VisibleSplitterThickness = 0;
    private const double SplitterHitThickness = 10;
    private const double MinRegionSize = 120;
    private const double SplitterSnapThreshold = 12;
    private const double SplitterDrawMergeTolerance = 6;

    private readonly LayoutGeometryCalculator _geometryCalculator = new();
    private readonly SplitResizeService _splitResizeService = new();
    private readonly Dictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    private LayoutGeometryResult? _lastGeometry;
    private ComputedSplitter? _activeDragSplitter;
    private double? _snapGuideAxisPosition;
    private SplitDirection? _snapGuideDirection;

    public event EventHandler<NodeSelectedEventArgs>? NodeSelected;
    public event EventHandler<SplitterRatioChangedEventArgs>? SplitterRatioChanged;
    public event EventHandler<CanvasContextActionRequestedEventArgs>? CanvasContextActionRequested;
    public event EventHandler<SnapLayoutSwitchRequestedEventArgs>? SnapLayoutSwitchRequested;
    public event EventHandler<WorkspaceProfileSwitchRequestedEventArgs>? WorkspaceProfileSwitchRequested;

    private PaneRect GetDesktopStageBounds()
    {
        if (StageBounds.Width > 0 && StageBounds.Height > 0)
        {
            return StageBounds;
        }

        return new PaneRect(
            StageInset,
            StageInset,
            Math.Max(0, ActualWidth - (StageInset * 2)),
            Math.Max(0, ActualHeight - (StageInset * 2)));
    }

    private static bool Contains(PaneRect rect, WpfPoint point)
    {
        return point.X >= rect.X
            && point.X <= rect.X + rect.Width
            && point.Y >= rect.Y
            && point.Y <= rect.Y + rect.Height;
    }

    private static bool IsOnSplitter(ComputedSplitter splitter, WpfPoint point)
    {
        if (splitter.Direction == SplitDirection.Vertical)
        {
            var x = splitter.Bounds.X + (splitter.Bounds.Width / 2);
            return point.X >= x - (SplitterHitThickness / 2)
                && point.X <= x + (SplitterHitThickness / 2)
                && point.Y >= splitter.HostBounds.Y
                && point.Y <= splitter.HostBounds.Y + splitter.HostBounds.Height;
        }

        var y = splitter.Bounds.Y + (splitter.Bounds.Height / 2);
        return point.Y >= y - (SplitterHitThickness / 2)
            && point.Y <= y + (SplitterHitThickness / 2)
            && point.X >= splitter.HostBounds.X
            && point.X <= splitter.HostBounds.X + splitter.HostBounds.Width;
    }

}
