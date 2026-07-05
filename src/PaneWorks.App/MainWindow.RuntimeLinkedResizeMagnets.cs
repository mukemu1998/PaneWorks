using PaneWorks.App.Diagnostics;
using PaneWorks.Core.Models;

namespace PaneWorks.App;

public partial class MainWindow
{
    private bool TryCreateRuntimeEdgeMagnetCandidate(
        PaneRect sourceBounds,
        PaneRect candidateBounds,
        ResizeEdge edge,
        IntPtr windowHandle,
        out RuntimeEdgeMagnetCandidate candidate)
    {
        candidate = default!;
        if (!TryGetRuntimeEdgeMagnetTarget(sourceBounds, candidateBounds, edge, out var targetEdgePosition))
        {
            return false;
        }

        candidate = new RuntimeEdgeMagnetCandidate(
            windowHandle,
            candidateBounds,
            RuntimeLinkedResizeSide.OppositeSide,
            targetEdgePosition,
            RuntimeEdgeMagnetAction.LinkOppositeEdge,
            _workspaceApplyService.GetWindowMinimumVisibleSize(windowHandle),
            _workspaceApplyService.GetWindowFrameAdjustment(windowHandle));
        return true;
    }

    private static bool TryCreateRuntimeDisplayEdgeAlignmentCandidate(
        PaneRect sourceBounds,
        PaneRect displayBounds,
        ResizeEdge edge,
        out RuntimeEdgeMagnetCandidate candidate)
    {
        candidate = default!;
        var sourceEdgePosition = GetEdgePosition(sourceBounds, edge);
        var targetEdgePosition = edge switch
        {
            ResizeEdge.Left => displayBounds.X,
            ResizeEdge.Right => GetRight(displayBounds),
            ResizeEdge.Top => displayBounds.Y,
            _ => GetBottom(displayBounds)
        };

        if (!IsRuntimeEdgeMagnetTargetInDragDirection(edge, sourceEdgePosition, targetEdgePosition))
        {
            return false;
        }

        candidate = new RuntimeEdgeMagnetCandidate(
            IntPtr.Zero,
            displayBounds,
            RuntimeLinkedResizeSide.SameSide,
            targetEdgePosition,
            RuntimeEdgeMagnetAction.AlignSourceEdge,
            default,
            default);
        return true;
    }

    private bool TryCreateRuntimeEdgeAlignmentCandidate(
        PaneRect sourceBounds,
        PaneRect candidateBounds,
        ResizeEdge edge,
        IntPtr windowHandle,
        out RuntimeEdgeMagnetCandidate candidate)
    {
        candidate = default!;
        var sourceEdgePosition = GetEdgePosition(sourceBounds, edge);
        var targetEdgePosition = edge switch
        {
            ResizeEdge.Left => candidateBounds.X,
            ResizeEdge.Right => GetRight(candidateBounds),
            ResizeEdge.Top => candidateBounds.Y,
            _ => GetBottom(candidateBounds)
        };

        if (!IsRuntimeEdgeMagnetTargetInDragDirection(edge, sourceEdgePosition, targetEdgePosition)
            || !IsRuntimeEdgeAlignmentRelated(sourceBounds, candidateBounds, edge))
        {
            return false;
        }

        candidate = new RuntimeEdgeMagnetCandidate(
            windowHandle,
            candidateBounds,
            RuntimeLinkedResizeSide.SameSide,
            targetEdgePosition,
            RuntimeEdgeMagnetAction.AlignSourceEdge,
            _workspaceApplyService.GetWindowMinimumVisibleSize(windowHandle),
            _workspaceApplyService.GetWindowFrameAdjustment(windowHandle));
        return true;
    }

    private static bool TryGetRuntimeEdgeMagnetTarget(
        PaneRect sourceBounds,
        PaneRect candidateBounds,
        ResizeEdge edge,
        out double targetEdgePosition)
    {
        targetEdgePosition = default;
        var sourceEdgePosition = GetEdgePosition(sourceBounds, edge);
        targetEdgePosition = edge switch
        {
            ResizeEdge.Left => GetRight(candidateBounds),
            ResizeEdge.Right => candidateBounds.X,
            ResizeEdge.Top => GetBottom(candidateBounds),
            _ => candidateBounds.Y
        };

        return IsRuntimeEdgeMagnetTargetInDragDirection(edge, sourceEdgePosition, targetEdgePosition);
    }

    private static bool IsRuntimeEdgeMagnetTargetInDragDirection(
        ResizeEdge edge,
        double sourceEdgePosition,
        double targetEdgePosition)
    {
        return edge switch
        {
            ResizeEdge.Left or ResizeEdge.Top => targetEdgePosition < sourceEdgePosition,
            _ => targetEdgePosition > sourceEdgePosition
        };
    }

    private static bool TryAttachRuntimeEdgeMagnetCandidate(
        RuntimeLinkedResizeSession session,
        List<RuntimeLinkedResizeNeighbor> activeNeighbors,
        List<RuntimeEdgeMagnetCandidate> magnetCandidates,
        PaneRect sourceBoundsAtEdge,
        double edgePosition,
        ref double minEdgePosition,
        ref double maxEdgePosition,
        out double snappedEdgePosition,
        out bool lockedEdge)
    {
        snappedEdgePosition = default;
        lockedEdge = false;
        var candidate = magnetCandidates
            .Where(item =>
                Math.Abs(item.TargetEdgePosition - edgePosition) <= RuntimeEdgeMagnetTolerance
                && (item.Action == RuntimeEdgeMagnetAction.AlignSourceEdge
                    || GetRuntimeEdgeMagnetOverlap(sourceBoundsAtEdge, item.InitialBounds, session.Edge) >= RuntimeEdgeMagnetMinimumOverlap))
            .OrderBy(item => Math.Abs(item.TargetEdgePosition - edgePosition))
            .FirstOrDefault();
        if (candidate is null)
        {
            return false;
        }

        snappedEdgePosition = candidate.TargetEdgePosition;
        magnetCandidates.Remove(candidate);
        if (candidate.Action == RuntimeEdgeMagnetAction.LinkOppositeEdge)
        {
            activeNeighbors.Add(new RuntimeLinkedResizeNeighbor(
                candidate.WindowHandle,
                candidate.InitialBounds,
                candidate.Side,
                candidate.MinimumSize,
                candidate.FrameAdjustment));
            AddRuntimeLinkedEdgeConstraints(
                candidate.InitialBounds,
                candidate.MinimumSize,
                GetRuntimeLinkedResizeEdge(session.Edge, candidate.Side),
                ref minEdgePosition,
                ref maxEdgePosition);
        }
        else
        {
            lockedEdge = true;
            AddRuntimeEdgeAlignmentStopConstraint(session.Edge, snappedEdgePosition, ref minEdgePosition, ref maxEdgePosition);
        }

        var targetHandleText = candidate.WindowHandle == IntPtr.Zero
            ? "display"
            : $"0x{candidate.WindowHandle.ToInt64():X}";
        PaneWorksLog.Info($"Runtime edge magnet attached: 0x{session.SourceWindowHandle.ToInt64():X} -> {targetHandleText}, action={candidate.Action}, edge={snappedEdgePosition:0}");
        return true;
    }
}
