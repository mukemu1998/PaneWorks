using PaneWorks.Core.Models;
using PaneWorks.Infrastructure.Windows;

namespace PaneWorks.App;

public partial class MainWindow
{
    private static double GetRuntimeEdgeMagnetOverlap(PaneRect sourceBounds, PaneRect candidateBounds, ResizeEdge edge)
    {
        return edge is ResizeEdge.Left or ResizeEdge.Right
            ? GetAxisOverlap(sourceBounds.Y, GetBottom(sourceBounds), candidateBounds.Y, GetBottom(candidateBounds))
            : GetAxisOverlap(sourceBounds.X, GetRight(sourceBounds), candidateBounds.X, GetRight(candidateBounds));
    }

    private static bool IsRuntimeEdgeAlignmentRelated(PaneRect sourceBounds, PaneRect candidateBounds, ResizeEdge edge)
    {
        if (GetRuntimeEdgeMagnetOverlap(sourceBounds, candidateBounds, edge) >= RuntimeEdgeMagnetMinimumOverlap)
        {
            return true;
        }

        return edge is ResizeEdge.Top or ResizeEdge.Bottom
            ? AreEdgesCloseForAlignment(sourceBounds.X, GetRight(candidateBounds))
                || AreEdgesCloseForAlignment(GetRight(sourceBounds), candidateBounds.X)
            : AreEdgesCloseForAlignment(sourceBounds.Y, GetBottom(candidateBounds))
                || AreEdgesCloseForAlignment(GetBottom(sourceBounds), candidateBounds.Y);
    }

    private static bool AreEdgesCloseForAlignment(double first, double second)
    {
        return Math.Abs(first - second) <= RuntimeEdgeAlignmentAdjacencyTolerance;
    }

    private static double GetAxisOverlap(double firstStart, double firstEnd, double secondStart, double secondEnd)
    {
        return Math.Max(0, Math.Min(firstEnd, secondEnd) - Math.Max(firstStart, secondStart));
    }

    private static bool TryGetRuntimeResizeNeighborSide(
        PaneRect sourceBounds,
        PaneRect candidateBounds,
        ResizeEdge edge,
        out RuntimeLinkedResizeSide side)
    {
        side = default;
        var edgePosition = GetEdgePosition(sourceBounds, edge);

        if (IsSameSideRuntimeResizeEdge(candidateBounds, edge, edgePosition))
        {
            side = RuntimeLinkedResizeSide.SameSide;
            return true;
        }

        if (IsOppositeSideRuntimeResizeEdge(candidateBounds, edge, edgePosition))
        {
            side = RuntimeLinkedResizeSide.OppositeSide;
            return true;
        }

        return false;
    }

    private static bool IsSameSideRuntimeResizeEdge(PaneRect bounds, ResizeEdge edge, double edgePosition)
    {
        return edge switch
        {
            ResizeEdge.Left => AreEdgesClose(bounds.X, edgePosition),
            ResizeEdge.Right => AreEdgesClose(GetRight(bounds), edgePosition),
            ResizeEdge.Top => AreEdgesClose(bounds.Y, edgePosition),
            _ => AreEdgesClose(GetBottom(bounds), edgePosition)
        };
    }

    private static bool IsOppositeSideRuntimeResizeEdge(PaneRect bounds, ResizeEdge edge, double edgePosition)
    {
        return edge switch
        {
            ResizeEdge.Left => AreEdgesClose(GetRight(bounds), edgePosition),
            ResizeEdge.Right => AreEdgesClose(bounds.X, edgePosition),
            ResizeEdge.Top => AreEdgesClose(GetBottom(bounds), edgePosition),
            _ => AreEdgesClose(bounds.Y, edgePosition)
        };
    }

    private static PaneRect GetRuntimeLinkedNeighborBounds(
        PaneRect initialBounds,
        WindowMinimumSize minimumSize,
        ResizeEdge edge,
        RuntimeLinkedResizeSide side,
        double edgePosition)
    {
        return GetBoundsForRuntimeLinkedResizeEdge(
            initialBounds,
            minimumSize,
            GetRuntimeLinkedResizeEdge(edge, side),
            edgePosition);
    }

    private static ResizeEdge GetRuntimeLinkedResizeEdge(ResizeEdge edge, RuntimeLinkedResizeSide side)
    {
        if (side == RuntimeLinkedResizeSide.SameSide)
        {
            return edge;
        }

        return edge switch
        {
            ResizeEdge.Left => ResizeEdge.Right,
            ResizeEdge.Right => ResizeEdge.Left,
            ResizeEdge.Top => ResizeEdge.Bottom,
            _ => ResizeEdge.Top
        };
    }

    private static PaneRect GetBoundsForRuntimeLinkedResizeEdge(
        PaneRect initialBounds,
        WindowMinimumSize minimumSize,
        ResizeEdge edge,
        double edgePosition)
    {
        var right = GetRight(initialBounds);
        var bottom = GetBottom(initialBounds);
        return edge switch
        {
            ResizeEdge.Left => GetLeftEdgeBounds(initialBounds, minimumSize, right, edgePosition),
            ResizeEdge.Right => GetRightEdgeBounds(initialBounds, minimumSize, edgePosition),
            ResizeEdge.Top => GetTopEdgeBounds(initialBounds, minimumSize, bottom, edgePosition),
            _ => GetBottomEdgeBounds(initialBounds, minimumSize, edgePosition)
        };
    }

    private static PaneRect GetLeftEdgeBounds(
        PaneRect initialBounds,
        WindowMinimumSize minimumSize,
        double right,
        double edgePosition)
    {
        var width = Math.Max(GetRuntimeLinkedMinWidth(initialBounds, minimumSize), right - edgePosition);
        return new PaneRect(right - width, initialBounds.Y, width, initialBounds.Height);
    }

    private static PaneRect GetRightEdgeBounds(
        PaneRect initialBounds,
        WindowMinimumSize minimumSize,
        double edgePosition)
    {
        return new PaneRect(
            initialBounds.X,
            initialBounds.Y,
            Math.Max(GetRuntimeLinkedMinWidth(initialBounds, minimumSize), edgePosition - initialBounds.X),
            initialBounds.Height);
    }

    private static PaneRect GetTopEdgeBounds(
        PaneRect initialBounds,
        WindowMinimumSize minimumSize,
        double bottom,
        double edgePosition)
    {
        var height = Math.Max(GetRuntimeLinkedMinHeight(initialBounds, minimumSize), bottom - edgePosition);
        return new PaneRect(initialBounds.X, bottom - height, initialBounds.Width, height);
    }

    private static PaneRect GetBottomEdgeBounds(
        PaneRect initialBounds,
        WindowMinimumSize minimumSize,
        double edgePosition)
    {
        return new PaneRect(
            initialBounds.X,
            initialBounds.Y,
            initialBounds.Width,
            Math.Max(GetRuntimeLinkedMinHeight(initialBounds, minimumSize), edgePosition - initialBounds.Y));
    }

    private static double GetEdgePosition(PaneRect bounds, ResizeEdge edge)
    {
        return edge switch
        {
            ResizeEdge.Left => bounds.X,
            ResizeEdge.Right => GetRight(bounds),
            ResizeEdge.Top => bounds.Y,
            _ => GetBottom(bounds)
        };
    }

    private static bool AreEdgesClose(double first, double second)
    {
        return Math.Abs(first - second) <= RuntimeLinkedResizeTolerance;
    }

    private static double GetRight(PaneRect bounds)
    {
        return bounds.X + bounds.Width;
    }

    private static double GetBottom(PaneRect bounds)
    {
        return bounds.Y + bounds.Height;
    }
}
