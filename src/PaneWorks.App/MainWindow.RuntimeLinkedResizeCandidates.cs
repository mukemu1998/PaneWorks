using PaneWorks.App.Diagnostics;
using PaneWorks.Core.Models;

namespace PaneWorks.App;

public partial class MainWindow
{
    private bool TryCreateRuntimeLinkedResizeSession(
        IntPtr windowHandle,
        out RuntimeLinkedResizeSession session)
    {
        session = default!;
        if (!_snapBindings.TryGetValue(windowHandle, out var binding)
            || _movingWindowInitialBounds is null
            || !TryGetCursorPosition(out var cursor))
        {
            return false;
        }

        var edge = GetResizeEdge(_movingWindowInitialBounds.Value, cursor);
        if (edge is null)
        {
            return false;
        }

        var sourceBounds = _movingWindowInitialBounds.Value;
        var neighbors = _snapBindings
            .Where(item => item.Key != windowHandle
                && string.Equals(item.Value.DisplayId, binding.DisplayId, StringComparison.OrdinalIgnoreCase))
            .Select(item =>
            {
                if (!_workspaceApplyService.TryGetVisibleWindowBounds(item.Key, out var bounds))
                {
                    return null;
                }

                return TryGetRuntimeResizeNeighborSide(sourceBounds, bounds, edge.Value, out var side)
                    ? new RuntimeLinkedResizeNeighbor(
                        item.Key,
                        bounds,
                        side,
                        _workspaceApplyService.GetWindowMinimumVisibleSize(item.Key),
                        _workspaceApplyService.GetWindowFrameAdjustment(item.Key))
                    : null;
            })
            .Where(item => item is not null)
            .Cast<RuntimeLinkedResizeNeighbor>()
            .ToList();

        var neighborHandles = neighbors.Select(item => item.WindowHandle).ToHashSet();
        var magnetCandidates = _snapBindings
            .Where(item => item.Key != windowHandle
                && !neighborHandles.Contains(item.Key)
                && string.Equals(item.Value.DisplayId, binding.DisplayId, StringComparison.OrdinalIgnoreCase))
            .Select(item =>
            {
                if (!_workspaceApplyService.TryGetVisibleWindowBounds(item.Key, out var bounds))
                {
                    return null;
                }

                return TryCreateRuntimeEdgeMagnetCandidate(sourceBounds, bounds, edge.Value, item.Key, out var candidate)
                    ? candidate
                    : null;
            })
            .Where(item => item is not null)
            .Cast<RuntimeEdgeMagnetCandidate>()
            .ToList();

        if (ViewModel.TryGetDisplayById(binding.DisplayId, out var display)
            && TryCreateRuntimeDisplayEdgeAlignmentCandidate(
                sourceBounds,
                GetSnapTargetStageBounds(display),
                edge.Value,
                out var displayAlignmentCandidate))
        {
            magnetCandidates.Add(displayAlignmentCandidate);
        }

        magnetCandidates.AddRange(_snapBindings
            .Where(item => item.Key != windowHandle
                && !neighborHandles.Contains(item.Key)
                && string.Equals(item.Value.DisplayId, binding.DisplayId, StringComparison.OrdinalIgnoreCase))
            .Select(item =>
            {
                if (!_workspaceApplyService.TryGetVisibleWindowBounds(item.Key, out var bounds))
                {
                    return null;
                }

                return TryCreateRuntimeEdgeAlignmentCandidate(sourceBounds, bounds, edge.Value, item.Key, out var candidate)
                    ? candidate
                    : null;
            })
            .Where(item => item is not null)
            .Cast<RuntimeEdgeMagnetCandidate>());

        if (neighbors.Count == 0 && magnetCandidates.Count == 0)
        {
            return false;
        }

        var minEdgePosition = double.NegativeInfinity;
        var maxEdgePosition = double.PositiveInfinity;
        var sourceMinimumSize = _workspaceApplyService.GetWindowMinimumVisibleSize(windowHandle);
        AddRuntimeLinkedEdgeConstraints(sourceBounds, sourceMinimumSize, edge.Value, ref minEdgePosition, ref maxEdgePosition);
        foreach (var neighbor in neighbors)
        {
            AddRuntimeLinkedEdgeConstraints(
                neighbor.InitialBounds,
                neighbor.MinimumSize,
                GetRuntimeLinkedResizeEdge(edge.Value, neighbor.Side),
                ref minEdgePosition,
                ref maxEdgePosition);
        }

        var initialEdgePosition = GetEdgePosition(sourceBounds, edge.Value);
        if (minEdgePosition > maxEdgePosition)
        {
            minEdgePosition = initialEdgePosition;
            maxEdgePosition = initialEdgePosition;
        }

        session = new RuntimeLinkedResizeSession(
            windowHandle,
            binding.DisplayId,
            sourceBounds,
            sourceMinimumSize,
            _workspaceApplyService.GetWindowFrameAdjustment(windowHandle),
            edge.Value,
            minEdgePosition,
            maxEdgePosition,
            neighbors,
            magnetCandidates);
        return true;
    }

}
