using PaneWorks.App.Diagnostics;
using PaneWorks.Core.Models;
using PaneWorks.Infrastructure.Windows;
using WpfPoint = System.Windows.Point;

namespace PaneWorks.App;

public partial class MainWindow
{
    private TemporarySnapTarget? ResolveTemporarySnapTarget(
        LayoutDocument document,
        LayoutGeometryResult geometry,
        string displayId,
        WpfPoint cursorInDevicePixels)
    {
        if (TryResolveDividerInsertionTarget(document, geometry, displayId, cursorInDevicePixels, out var dividerTarget))
        {
            return dividerTarget;
        }

        var region = ResolveHoveredSnapRegion(geometry, displayId, cursorInDevicePixels);
        if (region is null)
        {
            return null;
        }

        var edge = ResolveInsertionEdge(region.Bounds, cursorInDevicePixels);
        return edge is null
            ? new TemporarySnapTarget(
                SnapInsertionKind.Center,
                displayId,
                region.NodeId,
                null,
                null,
                null,
                region.Bounds)
            : new TemporarySnapTarget(
                edge.Kind,
                displayId,
                region.NodeId,
                null,
                null,
                null,
                GetEdgeInsertionPreviewBounds(region.Bounds, edge.Kind));
    }

    private bool TryResolveDividerInsertionTarget(
        LayoutDocument document,
        LayoutGeometryResult geometry,
        string displayId,
        WpfPoint cursorInDevicePixels,
        out TemporarySnapTarget target)
    {
        foreach (var splitter in geometry.Splitters)
        {
            if (!TryFindSiblingLeaves(document.Root, splitter.SplitNodeId, out var firstLeafId, out var secondLeafId))
            {
                continue;
            }

            if (!IsPointerNearSplitter(splitter, cursorInDevicePixels))
            {
                continue;
            }

            target = new TemporarySnapTarget(
                SnapInsertionKind.Divider,
                displayId,
                firstLeafId,
                splitter.SplitNodeId,
                firstLeafId,
                secondLeafId,
                GetDividerInsertionPreviewBounds(splitter));
            return true;
        }

        target = default!;
        return false;
    }

    private static bool TryFindSiblingLeaves(
        LayoutNode node,
        string splitNodeId,
        out string firstLeafId,
        out string secondLeafId)
    {
        if (node is SplitNode split)
        {
            if (split.Id == splitNodeId
                && split.First is LeafNode firstLeaf
                && split.Second is LeafNode secondLeaf)
            {
                firstLeafId = firstLeaf.Id;
                secondLeafId = secondLeaf.Id;
                return true;
            }

            if (TryFindSiblingLeaves(split.First, splitNodeId, out firstLeafId, out secondLeafId)
                || TryFindSiblingLeaves(split.Second, splitNodeId, out firstLeafId, out secondLeafId))
            {
                return true;
            }
        }

        firstLeafId = string.Empty;
        secondLeafId = string.Empty;
        return false;
    }

    private static bool IsPointerNearSplitter(ComputedSplitter splitter, WpfPoint cursor)
    {
        var tolerance = TemporaryInsertDividerTolerance;
        if (splitter.Direction == SplitDirection.Vertical)
        {
            return cursor.Y >= splitter.HostBounds.Y - tolerance
                && cursor.Y <= splitter.HostBounds.Y + splitter.HostBounds.Height + tolerance
                && Math.Abs(cursor.X - splitter.Bounds.X) <= tolerance;
        }

        return cursor.X >= splitter.HostBounds.X - tolerance
            && cursor.X <= splitter.HostBounds.X + splitter.HostBounds.Width + tolerance
            && Math.Abs(cursor.Y - splitter.Bounds.Y) <= tolerance;
    }

    private static InsertionEdge? ResolveInsertionEdge(PaneRect bounds, WpfPoint cursor)
    {
        if (!Contains(bounds, cursor))
        {
            return null;
        }

        var threshold = Math.Min(
            TemporaryInsertEdgeThreshold,
            Math.Max(TemporaryInsertMinimumEdgeThreshold, Math.Min(bounds.Width, bounds.Height) * TemporaryInsertEdgeRatio));
        var distances = new[]
        {
            new InsertionEdge(SnapInsertionKind.InsertTop, cursor.Y - bounds.Y),
            new InsertionEdge(SnapInsertionKind.InsertBottom, bounds.Y + bounds.Height - cursor.Y),
            new InsertionEdge(SnapInsertionKind.InsertLeft, cursor.X - bounds.X),
            new InsertionEdge(SnapInsertionKind.InsertRight, bounds.X + bounds.Width - cursor.X)
        };
        var closest = distances.OrderBy(item => item.Distance).First();
        return closest.Distance <= threshold ? closest : null;
    }

    private static PaneRect GetEdgeInsertionPreviewBounds(PaneRect bounds, SnapInsertionKind kind)
    {
        return kind switch
        {
            SnapInsertionKind.InsertTop => new PaneRect(bounds.X, bounds.Y, bounds.Width, bounds.Height / 2),
            SnapInsertionKind.InsertBottom => new PaneRect(bounds.X, bounds.Y + (bounds.Height / 2), bounds.Width, bounds.Height / 2),
            SnapInsertionKind.InsertLeft => new PaneRect(bounds.X, bounds.Y, bounds.Width / 2, bounds.Height),
            SnapInsertionKind.InsertRight => new PaneRect(bounds.X + (bounds.Width / 2), bounds.Y, bounds.Width / 2, bounds.Height),
            _ => bounds
        };
    }

    private static PaneRect GetDividerInsertionPreviewBounds(ComputedSplitter splitter)
    {
        var host = splitter.HostBounds;
        return splitter.Direction == SplitDirection.Vertical
            ? new PaneRect(host.X + (host.Width / 3), host.Y, host.Width / 3, host.Height)
            : new PaneRect(host.X, host.Y + (host.Height / 3), host.Width, host.Height / 3);
    }

    private bool TryApplyTemporarySnapInsertion(TemporarySnapTarget target, string reason)
    {
        if (target.Kind == SnapInsertionKind.Center
            || _movingWindowHandle == IntPtr.Zero
            || !ViewModel.TryGetDisplayById(target.DisplayId, out var display))
        {
            return false;
        }

        var sourceDocument = GetRuntimeSessionLayoutDocumentForDisplay(target.DisplayId);
        var result = target.Kind == SnapInsertionKind.Divider
            ? _runtimeSessionLayoutEditorService.InsertLeafBetweenSiblingLeaves(
                sourceDocument,
                target.SplitNodeId!,
                target.FirstSiblingLeafNodeId!,
                target.SecondSiblingLeafNodeId!)
            : _runtimeSessionLayoutEditorService.InsertLeafAtEdge(
                sourceDocument,
                target.TargetNodeId,
                IsHorizontalInsertion(target.Kind) ? SplitDirection.Horizontal : SplitDirection.Vertical,
                IsLeadingInsertion(target.Kind));

        if (!result.Changed)
        {
            return false;
        }

        _sessionSnapLayoutDocuments[target.DisplayId] = result.Document;
        var geometry = _geometryCalculator.Compute(
            result.Document,
            GetSnapTargetStageBounds(display),
            SnapTargetSplitterThickness);
        var regionsByNodeId = geometry.Regions.ToDictionary(region => region.NodeId, StringComparer.Ordinal);
        if (!regionsByNodeId.TryGetValue(result.InsertedNodeId, out var insertedRegion))
        {
            return false;
        }

        var restoreBounds = ResolveRestoreBoundsForSnap(_movingWindowHandle);
        _snapBindings[_movingWindowHandle] = new SnapBindingState(result.InsertedNodeId, target.DisplayId, restoreBounds);
        foreach (var binding in _snapBindings)
        {
            if (string.Equals(binding.Value.DisplayId, target.DisplayId, StringComparison.OrdinalIgnoreCase)
                && regionsByNodeId.TryGetValue(binding.Value.NodeId, out var region))
            {
                _snapRuntimeBounds[binding.Key] = region.Bounds;
            }
        }

        var updates = BuildTemporaryInsertionUpdates(target.DisplayId);
        PaneWorksLog.Info($"Temporary snap insertion ({reason}): kind={target.Kind}, updates={updates.Count}");
        ApplyTemporaryInsertionUpdates(updates);
        BringSnappedWindowToTopSoon(_movingWindowHandle);
        QueueSnappedWindowInfoCache(_movingWindowHandle);
        return true;
    }

    private List<WindowBoundsUpdate> BuildTemporaryInsertionUpdates(string displayId)
    {
        var updates = new List<WindowBoundsUpdate>();
        foreach (var binding in _snapBindings)
        {
            if (!string.Equals(binding.Value.DisplayId, displayId, StringComparison.OrdinalIgnoreCase)
                || !_snapRuntimeBounds.TryGetValue(binding.Key, out var bounds)
                || !_workspaceApplyService.TryGetVisibleWindowBounds(binding.Key, out _))
            {
                continue;
            }

            updates.Add(new WindowBoundsUpdate(
                binding.Key,
                bounds,
                _workspaceApplyService.GetWindowFrameAdjustment(binding.Key)));
        }

        return updates;
    }

    private void ApplyTemporaryInsertionUpdates(IReadOnlyList<WindowBoundsUpdate> updates)
    {
        if (updates.Count == 0)
        {
            return;
        }

        BeginInternalWindowLayoutUpdate();
        _ = Task.Run(() =>
        {
            _workspaceApplyService.MoveSnappedWindowsToBounds(updates);
        }).ContinueWith(
            _ => Dispatcher.BeginInvoke(EndInternalWindowLayoutUpdate),
            TaskScheduler.Default);
    }

    private static bool IsHorizontalInsertion(SnapInsertionKind kind)
    {
        return kind is SnapInsertionKind.InsertTop or SnapInsertionKind.InsertBottom;
    }

    private static bool IsLeadingInsertion(SnapInsertionKind kind)
    {
        return kind is SnapInsertionKind.InsertTop or SnapInsertionKind.InsertLeft;
    }

    private sealed record TemporarySnapTarget(
        SnapInsertionKind Kind,
        string DisplayId,
        string TargetNodeId,
        string? SplitNodeId,
        string? FirstSiblingLeafNodeId,
        string? SecondSiblingLeafNodeId,
        PaneRect PreviewBounds);

    private sealed record InsertionEdge(SnapInsertionKind Kind, double Distance);

    private enum SnapInsertionKind
    {
        Center,
        InsertTop,
        InsertBottom,
        InsertLeft,
        InsertRight,
        Divider
    }
}
