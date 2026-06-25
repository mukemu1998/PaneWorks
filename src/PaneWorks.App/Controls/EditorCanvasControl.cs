using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PaneWorks.App.ViewModels;
using PaneWorks.Core.Models;
using PaneWorks.Core.Services;
using WpfColor = System.Windows.Media.Color;
using WpfCursors = System.Windows.Input.Cursors;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace PaneWorks.App.Controls;

public sealed class EditorCanvasControl : FrameworkElement
{
    private const double StageInset = 0;
    private const double VisibleSplitterThickness = 0;
    private const double SplitterHitThickness = 10;
    private const double MinRegionSize = 120;
    private const double SplitterSnapThreshold = 12;

    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(
            nameof(Document),
            typeof(LayoutDocument),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedNodeIdProperty =
        DependencyProperty.Register(
            nameof(SelectedNodeId),
            typeof(string),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PreviewNodeIdProperty =
        DependencyProperty.Register(
            nameof(PreviewNodeId),
            typeof(string),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AvailableLayoutsProperty =
        DependencyProperty.Register(
            nameof(AvailableLayouts),
            typeof(IEnumerable<LayoutListItemViewModel>),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty ActiveSnapLayoutIdProperty =
        DependencyProperty.Register(
            nameof(ActiveSnapLayoutId),
            typeof(string),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty StageBoundsProperty =
        DependencyProperty.Register(
            nameof(StageBounds),
            typeof(PaneRect),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(default(PaneRect), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ReferenceLayoutsProperty =
        DependencyProperty.Register(
            nameof(ReferenceLayouts),
            typeof(IEnumerable<EditorReferenceLayout>),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly LayoutGeometryCalculator _geometryCalculator = new();
    private readonly SplitResizeService _splitResizeService = new();

    private LayoutGeometryResult? _lastGeometry;
    private ComputedSplitter? _activeDragSplitter;
    private double? _snapGuideAxisPosition;
    private SplitDirection? _snapGuideDirection;

    public event EventHandler<NodeSelectedEventArgs>? NodeSelected;
    public event EventHandler<SplitterRatioChangedEventArgs>? SplitterRatioChanged;
    public event EventHandler<CanvasContextActionRequestedEventArgs>? CanvasContextActionRequested;
    public event EventHandler<SnapLayoutSwitchRequestedEventArgs>? SnapLayoutSwitchRequested;

    public LayoutDocument? Document
    {
        get => (LayoutDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public string? SelectedNodeId
    {
        get => (string?)GetValue(SelectedNodeIdProperty);
        set => SetValue(SelectedNodeIdProperty, value);
    }

    public string? PreviewNodeId
    {
        get => (string?)GetValue(PreviewNodeIdProperty);
        set => SetValue(PreviewNodeIdProperty, value);
    }

    public IEnumerable<LayoutListItemViewModel>? AvailableLayouts
    {
        get => (IEnumerable<LayoutListItemViewModel>?)GetValue(AvailableLayoutsProperty);
        set => SetValue(AvailableLayoutsProperty, value);
    }

    public string ActiveSnapLayoutId
    {
        get => (string)GetValue(ActiveSnapLayoutIdProperty);
        set => SetValue(ActiveSnapLayoutIdProperty, value);
    }

    public PaneRect StageBounds
    {
        get => (PaneRect)GetValue(StageBoundsProperty);
        set => SetValue(StageBoundsProperty, value);
    }

    public IEnumerable<EditorReferenceLayout>? ReferenceLayouts
    {
        get => (IEnumerable<EditorReferenceLayout>?)GetValue(ReferenceLayoutsProperty);
        set => SetValue(ReferenceLayoutsProperty, value);
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
    {
        var point = hitTestParameters.HitPoint;
        var stageBounds = GetDesktopStageBounds();

        if (Contains(stageBounds, point))
        {
            return new PointHitTestResult(this, point);
        }

        return null!;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (Document is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        DrawReferenceLayouts(drawingContext);

        var stageBounds = GetDesktopStageBounds();
        _lastGeometry = _geometryCalculator.Compute(Document, stageBounds, VisibleSplitterThickness);

        foreach (var region in _lastGeometry.Regions)
        {
            var rect = ToRect(region.Bounds);
            var isSelected = region.NodeId == SelectedNodeId;
            var isPreview = region.NodeId == PreviewNodeId;

            if (isPreview)
            {
                drawingContext.DrawRectangle(new SolidColorBrush(WpfColor.FromArgb(52, 74, 222, 128)), null, rect);
            }
            else if (isSelected)
            {
                drawingContext.DrawRectangle(new SolidColorBrush(WpfColor.FromArgb(18, 255, 255, 255)), null, rect);
            }

            drawingContext.DrawRectangle(
                null,
                new WpfPen(
                    new SolidColorBrush(
                        isPreview
                            ? WpfColor.FromRgb(74, 222, 128)
                            : isSelected
                                ? WpfColor.FromRgb(255, 170, 96)
                                : WpfColor.FromArgb(210, 255, 255, 255)),
                    isPreview ? 3 : isSelected ? 2.2 : 1),
                rect);
        }

        foreach (var splitter in _lastGeometry.Splitters)
        {
            var isSelected = splitter.SplitNodeId == SelectedNodeId;
            var basePen = new WpfPen(
                new SolidColorBrush(isSelected ? WpfColor.FromRgb(84, 166, 255) : WpfColor.FromRgb(255, 214, 64)),
                isSelected ? 3.2 : 2.2)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            var glowPen = new WpfPen(
                new SolidColorBrush(isSelected ? WpfColor.FromArgb(92, 84, 166, 255) : WpfColor.FromArgb(48, 255, 214, 64)),
                isSelected ? 7 : 4.5)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            if (splitter.Direction == SplitDirection.Vertical)
            {
                var x = splitter.Bounds.X + (splitter.Bounds.Width / 2);
                drawingContext.DrawLine(
                    glowPen,
                    new WpfPoint(x, splitter.HostBounds.Y),
                    new WpfPoint(x, splitter.HostBounds.Y + splitter.HostBounds.Height));
                drawingContext.DrawLine(
                    basePen,
                    new WpfPoint(x, splitter.HostBounds.Y),
                    new WpfPoint(x, splitter.HostBounds.Y + splitter.HostBounds.Height));
                continue;
            }

            var y = splitter.Bounds.Y + (splitter.Bounds.Height / 2);
            drawingContext.DrawLine(
                glowPen,
                new WpfPoint(splitter.HostBounds.X, y),
                new WpfPoint(splitter.HostBounds.X + splitter.HostBounds.Width, y));
            drawingContext.DrawLine(
                basePen,
                new WpfPoint(splitter.HostBounds.X, y),
                new WpfPoint(splitter.HostBounds.X + splitter.HostBounds.Width, y));
        }

        if (_snapGuideAxisPosition.HasValue && _snapGuideDirection.HasValue)
        {
            var guidePen = new WpfPen(
                new SolidColorBrush(WpfColor.FromArgb(180, 255, 248, 182)),
                1.4)
            {
                DashStyle = DashStyles.Dash,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            if (_snapGuideDirection == SplitDirection.Vertical)
            {
                drawingContext.DrawLine(
                    guidePen,
                    new WpfPoint(_snapGuideAxisPosition.Value, stageBounds.Y),
                    new WpfPoint(_snapGuideAxisPosition.Value, stageBounds.Y + stageBounds.Height));
            }
            else
            {
                drawingContext.DrawLine(
                    guidePen,
                    new WpfPoint(stageBounds.X, _snapGuideAxisPosition.Value),
                    new WpfPoint(stageBounds.X + stageBounds.Width, _snapGuideAxisPosition.Value));
            }
        }
    }

    private void DrawReferenceLayouts(DrawingContext drawingContext)
    {
        var references = ReferenceLayouts?.ToList();
        if (references is null || references.Count == 0)
        {
            return;
        }

        var borderPen = new WpfPen(
            new SolidColorBrush(WpfColor.FromArgb(130, 255, 255, 255)),
            1.1);
        var splitterGlowPen = new WpfPen(
            new SolidColorBrush(WpfColor.FromArgb(42, 255, 255, 255)),
            5.2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        var splitterPen = new WpfPen(
            new SolidColorBrush(WpfColor.FromArgb(230, 255, 255, 255)),
            2.1)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        foreach (var reference in references)
        {
            if (reference.StageBounds.Width <= 0 || reference.StageBounds.Height <= 0)
            {
                continue;
            }

            var geometry = _geometryCalculator.Compute(
                reference.Document,
                reference.StageBounds,
                VisibleSplitterThickness);

            drawingContext.DrawRectangle(null, borderPen, ToRect(reference.StageBounds));

            foreach (var splitter in geometry.Splitters)
            {
                if (splitter.Direction == SplitDirection.Vertical)
                {
                    var x = splitter.Bounds.X + (splitter.Bounds.Width / 2);
                    var start = new WpfPoint(x, splitter.HostBounds.Y);
                    var end = new WpfPoint(x, splitter.HostBounds.Y + splitter.HostBounds.Height);
                    drawingContext.DrawLine(splitterGlowPen, start, end);
                    drawingContext.DrawLine(splitterPen, start, end);
                    continue;
                }

                var y = splitter.Bounds.Y + (splitter.Bounds.Height / 2);
                var horizontalStart = new WpfPoint(splitter.HostBounds.X, y);
                var horizontalEnd = new WpfPoint(splitter.HostBounds.X + splitter.HostBounds.Width, y);
                drawingContext.DrawLine(splitterGlowPen, horizontalStart, horizontalEnd);
                drawingContext.DrawLine(splitterPen, horizontalStart, horizontalEnd);
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (_lastGeometry is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        var splitter = _lastGeometry.Splitters.FirstOrDefault(item => IsOnSplitter(item, point));
        if (splitter is not null)
        {
            _activeDragSplitter = splitter;
            CaptureMouse();
            Cursor = splitter.Direction == SplitDirection.Vertical ? WpfCursors.SizeWE : WpfCursors.SizeNS;
            NodeSelected?.Invoke(this, new NodeSelectedEventArgs(splitter.SplitNodeId));
            return;
        }

        var region = _lastGeometry.Regions.FirstOrDefault(item => Contains(item.Bounds, point));
        if (region is not null)
        {
            NodeSelected?.Invoke(this, new NodeSelectedEventArgs(region.NodeId));
        }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);

        if (_lastGeometry is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        var splitter = _lastGeometry.Splitters.FirstOrDefault(item => IsOnSplitter(item, point));
        if (splitter is not null)
        {
            NodeSelected?.Invoke(this, new NodeSelectedEventArgs(splitter.SplitNodeId));
            OpenContextMenu(splitter.SplitNodeId, includeSplitActions: false, includeDelete: true);
            return;
        }

        var region = _lastGeometry.Regions.FirstOrDefault(item => Contains(item.Bounds, point));
        if (region is null)
        {
            return;
        }

        NodeSelected?.Invoke(this, new NodeSelectedEventArgs(region.NodeId));
        var includeDelete = Document?.Root.Id != region.NodeId;
        OpenContextMenu(region.NodeId, includeSplitActions: true, includeDelete: includeDelete);
    }

    protected override void OnMouseMove(WpfMouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_lastGeometry is null)
        {
            Cursor = WpfCursors.Arrow;
            return;
        }

        var point = e.GetPosition(this);

        if (_activeDragSplitter is not null && IsMouseCaptured)
        {
            var axisPosition = _activeDragSplitter.Direction == SplitDirection.Vertical ? point.X : point.Y;
            axisPosition = ApplyAlignmentSnap(_activeDragSplitter, axisPosition);
            var candidateRatio = _splitResizeService.GetCandidateRatio(_activeDragSplitter, axisPosition, VisibleSplitterThickness);
            var clampedRatio = _splitResizeService.ClampRatio(_activeDragSplitter, candidateRatio, MinRegionSize, VisibleSplitterThickness);

            SplitterRatioChanged?.Invoke(this, new SplitterRatioChangedEventArgs(_activeDragSplitter.SplitNodeId, clampedRatio));
            return;
        }

        var hoverSplitter = _lastGeometry.Splitters.FirstOrDefault(item => IsOnSplitter(item, point));
        Cursor = hoverSplitter is null
            ? WpfCursors.Arrow
            : hoverSplitter.Direction == SplitDirection.Vertical
                ? WpfCursors.SizeWE
                : WpfCursors.SizeNS;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        EndDrag();
    }

    protected override void OnLostMouseCapture(WpfMouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        EndDrag();
    }

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

    private static Rect ToRect(PaneRect rect)
    {
        return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    private void OpenContextMenu(string targetNodeId, bool includeSplitActions, bool includeDelete)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = this,
            Placement = PlacementMode.MousePoint,
            StaysOpen = false,
            Focusable = true
        };

        if (includeSplitActions)
        {
            menu.Items.Add(CreateMenuItem("横向二等分", CanvasContextAction.SplitHorizontalHalf, targetNodeId));
            menu.Items.Add(CreateMenuItem("纵向二等分", CanvasContextAction.SplitVerticalHalf, targetNodeId));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("横向三等分", CanvasContextAction.SplitHorizontalThirds, targetNodeId));
            menu.Items.Add(CreateMenuItem("纵向三等分", CanvasContextAction.SplitVerticalThirds, targetNodeId));
        }

        if (includeDelete)
        {
            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
            }

            menu.Items.Add(CreateMenuItem("删除当前分割", CanvasContextAction.Delete, targetNodeId));
        }

        AppendSnapLayoutMenu(menu);

        if (menu.Items.Count == 0)
        {
            return;
        }

        menu.Opened += (_, _) => menu.Focus();
        menu.IsOpen = true;
    }

    private void AppendSnapLayoutMenu(ContextMenu menu)
    {
        var layouts = AvailableLayouts?.ToList();
        if (layouts is null || layouts.Count == 0)
        {
            return;
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        var layoutMenu = new MenuItem
        {
            Header = "快速切换吸附布局"
        };

        foreach (var layout in layouts)
        {
            var layoutItem = new MenuItem
            {
                Header = layout.Name,
                IsCheckable = true,
                IsChecked = string.Equals(layout.Id, ActiveSnapLayoutId, StringComparison.OrdinalIgnoreCase),
                ToolTip = $"{layout.Id}.json"
            };

            layoutItem.Click += (_, _) =>
            {
                SnapLayoutSwitchRequested?.Invoke(this, new SnapLayoutSwitchRequestedEventArgs(layout.Id));
            };

            layoutMenu.Items.Add(layoutItem);
        }

        menu.Items.Add(layoutMenu);
    }

    private MenuItem CreateMenuItem(string header, CanvasContextAction action, string targetNodeId)
    {
        var item = new MenuItem
        {
            Header = header
        };

        item.Click += (_, _) =>
        {
            CanvasContextActionRequested?.Invoke(this, new CanvasContextActionRequestedEventArgs(action, targetNodeId));
        };

        return item;
    }

    private void EndDrag()
    {
        _activeDragSplitter = null;
        Cursor = WpfCursors.Arrow;
        ClearSnapGuide();

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    private double ApplyAlignmentSnap(ComputedSplitter activeSplitter, double axisPosition)
    {
        if (_lastGeometry is null)
        {
            ClearSnapGuide();
            return axisPosition;
        }

        var candidatePositions = GetAlignmentCandidates(activeSplitter);
        if (candidatePositions.Count == 0)
        {
            ClearSnapGuide();
            return axisPosition;
        }

        var nearest = candidatePositions
            .Select(position => new { Position = position, Distance = Math.Abs(position - axisPosition) })
            .OrderBy(item => item.Distance)
            .First();

        if (nearest.Distance > SplitterSnapThreshold)
        {
            ClearSnapGuide();
            return axisPosition;
        }

        SetSnapGuide(activeSplitter.Direction, nearest.Position);
        return nearest.Position;
    }

    private List<double> GetAlignmentCandidates(ComputedSplitter activeSplitter)
    {
        if (_lastGeometry is null)
        {
            return [];
        }

        var candidates = new List<double>();

        foreach (var splitter in _lastGeometry.Splitters)
        {
            if (splitter.SplitNodeId == activeSplitter.SplitNodeId)
            {
                continue;
            }

            if (activeSplitter.Direction == SplitDirection.Vertical)
            {
                if (splitter.Direction == SplitDirection.Vertical)
                {
                    candidates.Add(splitter.Bounds.X + (splitter.Bounds.Width / 2));
                }
                else
                {
                    candidates.Add(splitter.HostBounds.X);
                    candidates.Add(splitter.HostBounds.X + splitter.HostBounds.Width);
                }

                continue;
            }

            if (splitter.Direction == SplitDirection.Horizontal)
            {
                candidates.Add(splitter.Bounds.Y + (splitter.Bounds.Height / 2));
            }
            else
            {
                candidates.Add(splitter.HostBounds.Y);
                candidates.Add(splitter.HostBounds.Y + splitter.HostBounds.Height);
            }
        }

        return candidates
            .OrderBy(value => value)
            .Aggregate(
                new List<double>(),
                (distinct, value) =>
                {
                    if (distinct.Count == 0 || Math.Abs(distinct[^1] - value) > 0.5)
                    {
                        distinct.Add(value);
                    }

                    return distinct;
                });
    }

    private void SetSnapGuide(SplitDirection direction, double axisPosition)
    {
        var changed = _snapGuideDirection != direction
            || !_snapGuideAxisPosition.HasValue
            || Math.Abs(_snapGuideAxisPosition.Value - axisPosition) > 0.1;

        _snapGuideDirection = direction;
        _snapGuideAxisPosition = axisPosition;

        if (changed)
        {
            InvalidateVisual();
        }
    }

    private void ClearSnapGuide()
    {
        if (!_snapGuideAxisPosition.HasValue && _snapGuideDirection is null)
        {
            return;
        }

        _snapGuideAxisPosition = null;
        _snapGuideDirection = null;
        InvalidateVisual();
    }
}
