using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PaneWorks.Core.Models;
using WpfCursors = System.Windows.Input.Cursors;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace PaneWorks.App.Controls;

public sealed partial class EditorCanvasControl
{
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
}
