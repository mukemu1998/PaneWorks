using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private const double SplitterDrawMergeTolerance = 6;

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

    public static readonly DependencyProperty AvailableWorkspaceProfilesProperty =
        DependencyProperty.Register(
            nameof(AvailableWorkspaceProfiles),
            typeof(IEnumerable<LayoutListItemViewModel>),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty ActiveWorkspaceProfileIdProperty =
        DependencyProperty.Register(
            nameof(ActiveWorkspaceProfileId),
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

    public static readonly DependencyProperty IsLayoutEditingEnabledProperty =
        DependencyProperty.Register(
            nameof(IsLayoutEditingEnabled),
            typeof(bool),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowWorkspaceBindingMarkersProperty =
        DependencyProperty.Register(
            nameof(ShowWorkspaceBindingMarkers),
            typeof(bool),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BindingDisplayIdProperty =
        DependencyProperty.Register(
            nameof(BindingDisplayId),
            typeof(string),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WorkspaceWindowBindingsProperty =
        DependencyProperty.Register(
            nameof(WorkspaceWindowBindings),
            typeof(IEnumerable<WorkspaceWindowBinding>),
            typeof(EditorCanvasControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

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

    public IEnumerable<LayoutListItemViewModel>? AvailableWorkspaceProfiles
    {
        get => (IEnumerable<LayoutListItemViewModel>?)GetValue(AvailableWorkspaceProfilesProperty);
        set => SetValue(AvailableWorkspaceProfilesProperty, value);
    }

    public string ActiveWorkspaceProfileId
    {
        get => (string)GetValue(ActiveWorkspaceProfileIdProperty);
        set => SetValue(ActiveWorkspaceProfileIdProperty, value);
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

    public bool IsLayoutEditingEnabled
    {
        get => (bool)GetValue(IsLayoutEditingEnabledProperty);
        set => SetValue(IsLayoutEditingEnabledProperty, value);
    }

    public bool ShowWorkspaceBindingMarkers
    {
        get => (bool)GetValue(ShowWorkspaceBindingMarkersProperty);
        set => SetValue(ShowWorkspaceBindingMarkersProperty, value);
    }

    public string BindingDisplayId
    {
        get => (string)GetValue(BindingDisplayIdProperty);
        set => SetValue(BindingDisplayIdProperty, value);
    }

    public IEnumerable<WorkspaceWindowBinding>? WorkspaceWindowBindings
    {
        get => (IEnumerable<WorkspaceWindowBinding>?)GetValue(WorkspaceWindowBindingsProperty);
        set => SetValue(WorkspaceWindowBindingsProperty, value);
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
                drawingContext.DrawRectangle(new SolidColorBrush(WpfColor.FromArgb(34, 84, 166, 255)), null, rect);
            }

            if (isPreview || isSelected)
            {
                drawingContext.DrawRectangle(
                    null,
                    new WpfPen(
                        new SolidColorBrush(isPreview ? WpfColor.FromRgb(74, 222, 128) : WpfColor.FromRgb(84, 166, 255)),
                        isPreview ? 3.2 : 3),
                    rect);
            }
        }

        drawingContext.DrawRectangle(
            null,
            new WpfPen(new SolidColorBrush(WpfColor.FromArgb(210, 255, 255, 255)), 1),
            ToRect(stageBounds));

        foreach (var splitter in MergeSplitterDrawSegments(_lastGeometry.Splitters, SelectedNodeId))
        {
            var basePen = new WpfPen(
                new SolidColorBrush(splitter.IsSelected ? WpfColor.FromRgb(84, 166, 255) : WpfColor.FromRgb(255, 214, 64)),
                splitter.IsSelected ? 3.2 : 2.2)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            var glowPen = new WpfPen(
                new SolidColorBrush(splitter.IsSelected ? WpfColor.FromArgb(92, 84, 166, 255) : WpfColor.FromArgb(48, 255, 214, 64)),
                splitter.IsSelected ? 7 : 4.5)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            if (splitter.Direction == SplitDirection.Vertical)
            {
                drawingContext.DrawLine(
                    glowPen,
                    new WpfPoint(splitter.AxisPosition, splitter.Start),
                    new WpfPoint(splitter.AxisPosition, splitter.End));
                drawingContext.DrawLine(
                    basePen,
                    new WpfPoint(splitter.AxisPosition, splitter.Start),
                    new WpfPoint(splitter.AxisPosition, splitter.End));
                continue;
            }

            drawingContext.DrawLine(
                glowPen,
                new WpfPoint(splitter.Start, splitter.AxisPosition),
                new WpfPoint(splitter.End, splitter.AxisPosition));
            drawingContext.DrawLine(
                basePen,
                new WpfPoint(splitter.Start, splitter.AxisPosition),
                new WpfPoint(splitter.End, splitter.AxisPosition));
        }

        DrawWorkspaceBindingMarkers(drawingContext);

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

            foreach (var splitter in MergeSplitterDrawSegments(geometry.Splitters, selectedNodeId: null))
            {
                if (splitter.Direction == SplitDirection.Vertical)
                {
                    var start = new WpfPoint(splitter.AxisPosition, splitter.Start);
                    var end = new WpfPoint(splitter.AxisPosition, splitter.End);
                    drawingContext.DrawLine(splitterGlowPen, start, end);
                    drawingContext.DrawLine(splitterPen, start, end);
                    continue;
                }

                var horizontalStart = new WpfPoint(splitter.Start, splitter.AxisPosition);
                var horizontalEnd = new WpfPoint(splitter.End, splitter.AxisPosition);
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
        if (splitter is not null && IsLayoutEditingEnabled)
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
            if (!IsLayoutEditingEnabled)
            {
                return;
            }

            OpenContextMenu(splitter.SplitNodeId, includeSplitActions: false, includeDelete: true);
            return;
        }

        var region = _lastGeometry.Regions.FirstOrDefault(item => Contains(item.Bounds, point));
        if (region is null)
        {
            return;
        }

        NodeSelected?.Invoke(this, new NodeSelectedEventArgs(region.NodeId));
        if (!IsLayoutEditingEnabled)
        {
            return;
        }

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

        if (!IsLayoutEditingEnabled)
        {
            Cursor = WpfCursors.Arrow;
            return;
        }

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
        AppendWorkspaceProfileMenu(menu);

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
            Header = "切换吸附布局"
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

    private void AppendWorkspaceProfileMenu(ContextMenu menu)
    {
        var profiles = AvailableWorkspaceProfiles?.ToList();
        if (profiles is null || profiles.Count == 0)
        {
            return;
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        var profileMenu = new MenuItem
        {
            Header = "切换并应用工作区方案"
        };

        foreach (var profile in profiles)
        {
            var profileItem = new MenuItem
            {
                Header = profile.Name,
                IsCheckable = true,
                IsChecked = string.Equals(profile.Id, ActiveWorkspaceProfileId, StringComparison.OrdinalIgnoreCase),
                ToolTip = profile.Description
            };

            profileItem.Click += (_, _) =>
            {
                WorkspaceProfileSwitchRequested?.Invoke(this, new WorkspaceProfileSwitchRequestedEventArgs(profile.Id));
            };

            profileMenu.Items.Add(profileItem);
        }

        menu.Items.Add(profileMenu);
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

    private void DrawWorkspaceBindingMarkers(DrawingContext drawingContext)
    {
        if (!ShowWorkspaceBindingMarkers || _lastGeometry is null)
        {
            return;
        }

        var bindings = WorkspaceWindowBindings?.ToList();
        if (bindings is null || bindings.Count == 0)
        {
            return;
        }

        var currentDisplayId = BindingDisplayId ?? string.Empty;
        foreach (var region in _lastGeometry.Regions)
        {
            var regionBindings = bindings
                .Where(item =>
                    string.Equals(item.DisplayId, currentDisplayId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.NodeId, region.NodeId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.StackOrder)
                .ToList();
            if (regionBindings.Count == 0)
            {
                continue;
            }

            DrawBindingMarker(drawingContext, region.Bounds, regionBindings);
        }
    }

    private void DrawBindingMarker(DrawingContext drawingContext, PaneRect regionBounds, IReadOnlyList<WorkspaceWindowBinding> bindings)
    {
        const double markerSize = 58;
        var centerX = regionBounds.X + (regionBounds.Width / 2);
        var centerY = regionBounds.Y + (regionBounds.Height / 2);
        var markerRect = new Rect(centerX - (markerSize / 2), centerY - (markerSize / 2), markerSize, markerSize);
        var background = new SolidColorBrush(WpfColor.FromArgb(220, 16, 22, 37));
        var stroke = new WpfPen(new SolidColorBrush(WpfColor.FromArgb(210, 159, 199, 255)), 1.2);
        drawingContext.DrawRoundedRectangle(background, stroke, markerRect, 16, 16);

        var visibleBindings = bindings
            .OrderBy(item => item.StackOrder)
            .TakeLast(3)
            .ToList();
        for (var index = 0; index < visibleBindings.Count; index++)
        {
            var itemBinding = visibleBindings[index];
            var iconRect = visibleBindings.Count == 1
                ? new Rect(markerRect.X + 13, markerRect.Y + 8, 32, 32)
                : new Rect(markerRect.X + 8 + (index * 7), markerRect.Y + 7 + (index * 4), 28, 28);
            var icon = TryGetIcon(itemBinding);
            if (icon is not null)
            {
                drawingContext.DrawImage(icon, iconRect);
            }
            else
            {
                var glyph = string.IsNullOrWhiteSpace(itemBinding.ProcessName)
                    ? "?"
                    : itemBinding.ProcessName.Trim()[0].ToString().ToUpperInvariant();
                var formatted = CreateFormattedText(glyph, 18, System.Windows.Media.Brushes.White, FontWeights.Bold);
                drawingContext.DrawText(
                    formatted,
                    new WpfPoint(
                        iconRect.X + ((iconRect.Width - formatted.Width) / 2),
                        iconRect.Y + 3));
            }
        }

        if (bindings.Count > 1)
        {
            var countLabel = CreateFormattedText($"+{bindings.Count - 1}", 10, System.Windows.Media.Brushes.White, FontWeights.Bold);
            drawingContext.DrawText(
                countLabel,
                new WpfPoint(
                    markerRect.Right - countLabel.Width - 6,
                    markerRect.Y + 5));
        }

        var binding = bindings.OrderBy(item => item.StackOrder).Last();
        var label = NormalizeProcessLabel(binding.ProcessName);
        if (!string.IsNullOrWhiteSpace(label))
        {
            var formattedLabel = CreateFormattedText(label, 9.5, new SolidColorBrush(WpfColor.FromRgb(207, 231, 255)), FontWeights.SemiBold);
            drawingContext.DrawText(
                formattedLabel,
                new WpfPoint(
                    markerRect.X + ((markerRect.Width - formattedLabel.Width) / 2),
                    markerRect.Bottom - 14));
        }
    }

    private ImageSource? TryGetIcon(WorkspaceWindowBinding binding)
    {
        var path = binding.ExecutablePath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            return null;
        }

        if (_iconCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                _iconCache[path] = null;
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            _iconCache[path] = source;
            return source;
        }
        catch
        {
            _iconCache[path] = null;
            return null;
        }
    }

    private static string NormalizeProcessLabel(string processName)
    {
        var label = processName.Trim();
        return label.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? label[..^4]
            : label;
    }

    private FormattedText CreateFormattedText(string text, double size, System.Windows.Media.Brush brush, FontWeight weight)
    {
        return new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(new System.Windows.Media.FontFamily("Microsoft YaHei UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
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

    private static List<SplitterDrawSegment> MergeSplitterDrawSegments(
        IEnumerable<ComputedSplitter> splitters,
        string? selectedNodeId)
    {
        var rawSegments = splitters
            .Select(splitter =>
            {
                var isSelected = selectedNodeId is not null
                    && splitter.SplitNodeId == selectedNodeId;
                return splitter.Direction == SplitDirection.Vertical
                    ? new SplitterDrawSegment(
                        splitter.Direction,
                        splitter.Bounds.X + (splitter.Bounds.Width / 2),
                        splitter.HostBounds.Y,
                        splitter.HostBounds.Y + splitter.HostBounds.Height,
                        isSelected)
                    : new SplitterDrawSegment(
                        splitter.Direction,
                        splitter.Bounds.Y + (splitter.Bounds.Height / 2),
                        splitter.HostBounds.X,
                        splitter.HostBounds.X + splitter.HostBounds.Width,
                        isSelected);
            })
            .OrderBy(segment => segment.Direction)
            .ThenBy(segment => segment.AxisPosition)
            .ThenBy(segment => segment.Start)
            .ToList();

        var mergedSegments = new List<SplitterDrawSegment>();
        foreach (var segment in rawSegments)
        {
            var mergeIndex = mergedSegments.FindLastIndex(existing =>
                existing.Direction == segment.Direction
                && Math.Abs(existing.AxisPosition - segment.AxisPosition) <= SplitterDrawMergeTolerance
                && segment.Start <= existing.End + SplitterDrawMergeTolerance
                && existing.Start <= segment.End + SplitterDrawMergeTolerance);
            if (mergeIndex < 0)
            {
                mergedSegments.Add(segment);
                continue;
            }

            var existing = mergedSegments[mergeIndex];
            mergedSegments[mergeIndex] = new SplitterDrawSegment(
                existing.Direction,
                (existing.AxisPosition + segment.AxisPosition) / 2,
                Math.Min(existing.Start, segment.Start),
                Math.Max(existing.End, segment.End),
                existing.IsSelected || segment.IsSelected);
        }

        return mergedSegments;
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

    private sealed record SplitterDrawSegment(
        SplitDirection Direction,
        double AxisPosition,
        double Start,
        double End,
        bool IsSelected);
}
