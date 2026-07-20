using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PaneWorks.Core.Models;
using WpfCursors = System.Windows.Input.Cursors;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace PaneWorks.App.Controls;

public sealed partial class EditorCanvasControl
{
    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
    {
        var point = hitTestParameters.HitPoint;
        var stageBounds = GetDesktopStageBounds();

        if (Contains(stageBounds, point)
            || ReferenceLayouts?.Any(reference => Contains(reference.StageBounds, point)) == true)
        {
            return new PointHitTestResult(this, point);
        }

        return null!;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (_lastGeometry is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (TryRequestReferenceDisplayEdit(point))
        {
            return;
        }

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
        if (TryRequestReferenceDisplayEdit(point))
        {
            return;
        }

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
            var (firstRegionMinSize, secondRegionMinSize) = GetMinimumRegionSizes(_activeDragSplitter);
            var clampedRatio = _splitResizeService.ClampRatio(
                _activeDragSplitter,
                candidateRatio,
                firstRegionMinSize,
                secondRegionMinSize,
                VisibleSplitterThickness);

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

    private bool TryRequestReferenceDisplayEdit(WpfPoint point)
    {
        if (!IsLayoutEditingEnabled)
        {
            return false;
        }

        var displayId = ReferenceLayouts?
            .FirstOrDefault(reference => Contains(reference.StageBounds, point))
            ?.DisplayId;
        if (string.IsNullOrWhiteSpace(displayId))
        {
            return false;
        }

        DisplayEditRequested?.Invoke(this, new DisplayEditRequestedEventArgs(displayId));
        return true;
    }

    private (double First, double Second) GetMinimumRegionSizes(ComputedSplitter splitter)
    {
        var first = MinRegionSize;
        var second = MinRegionSize;
        var stageBounds = GetDesktopStageBounds();
        if (WorkAreaBounds.Width <= 0 || WorkAreaBounds.Height <= 0)
        {
            return (first, second);
        }

        if (splitter.Direction == SplitDirection.Horizontal)
        {
            if (AreNearlyEqual(splitter.HostBounds.Y, stageBounds.Y)
                && WorkAreaBounds.Y > stageBounds.Y)
            {
                first = Math.Min(first, WorkAreaBounds.Y - stageBounds.Y);
            }

            if (AreNearlyEqual(splitter.HostBounds.Y + splitter.HostBounds.Height, stageBounds.Y + stageBounds.Height)
                && WorkAreaBounds.Y + WorkAreaBounds.Height < stageBounds.Y + stageBounds.Height)
            {
                second = Math.Min(second, (stageBounds.Y + stageBounds.Height) - (WorkAreaBounds.Y + WorkAreaBounds.Height));
            }

            return (first, second);
        }

        if (AreNearlyEqual(splitter.HostBounds.X, stageBounds.X)
            && WorkAreaBounds.X > stageBounds.X)
        {
            first = Math.Min(first, WorkAreaBounds.X - stageBounds.X);
        }

        if (AreNearlyEqual(splitter.HostBounds.X + splitter.HostBounds.Width, stageBounds.X + stageBounds.Width)
            && WorkAreaBounds.X + WorkAreaBounds.Width < stageBounds.X + stageBounds.Width)
        {
            second = Math.Min(second, (stageBounds.X + stageBounds.Width) - (WorkAreaBounds.X + WorkAreaBounds.Width));
        }

        return (first, second);
    }

    private static bool AreNearlyEqual(double left, double right)
    {
        return Math.Abs(left - right) <= 1;
    }
}
