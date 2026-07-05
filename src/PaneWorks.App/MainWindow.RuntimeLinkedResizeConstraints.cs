using PaneWorks.Core.Models;
using PaneWorks.Infrastructure.Windows;

namespace PaneWorks.App;

public partial class MainWindow
{
    private static void AddRuntimeLinkedEdgeConstraints(
        PaneRect bounds,
        WindowMinimumSize minimumSize,
        ResizeEdge edge,
        ref double minEdgePosition,
        ref double maxEdgePosition)
    {
        switch (edge)
        {
            case ResizeEdge.Left:
                maxEdgePosition = Math.Min(maxEdgePosition, GetRight(bounds) - GetRuntimeLinkedMinWidth(bounds, minimumSize));
                break;
            case ResizeEdge.Right:
                minEdgePosition = Math.Max(minEdgePosition, bounds.X + GetRuntimeLinkedMinWidth(bounds, minimumSize));
                break;
            case ResizeEdge.Top:
                maxEdgePosition = Math.Min(maxEdgePosition, GetBottom(bounds) - GetRuntimeLinkedMinHeight(bounds, minimumSize));
                break;
            case ResizeEdge.Bottom:
                minEdgePosition = Math.Max(minEdgePosition, bounds.Y + GetRuntimeLinkedMinHeight(bounds, minimumSize));
                break;
        }
    }

    private static void AddRuntimeEdgeAlignmentStopConstraint(
        ResizeEdge edge,
        double edgePosition,
        ref double minEdgePosition,
        ref double maxEdgePosition)
    {
        switch (edge)
        {
            case ResizeEdge.Left:
            case ResizeEdge.Top:
                minEdgePosition = Math.Max(minEdgePosition, edgePosition);
                break;
            case ResizeEdge.Right:
            case ResizeEdge.Bottom:
                maxEdgePosition = Math.Min(maxEdgePosition, edgePosition);
                break;
        }
    }

    private static double ClampRuntimeLinkedEdgePosition(
        double edgePosition,
        double minEdgePosition,
        double maxEdgePosition)
    {
        if (minEdgePosition > maxEdgePosition)
        {
            return Math.Abs(edgePosition - minEdgePosition) < Math.Abs(edgePosition - maxEdgePosition)
                ? minEdgePosition
                : maxEdgePosition;
        }

        return Math.Clamp(edgePosition, minEdgePosition, maxEdgePosition);
    }

    private static double GetRuntimeLinkedMinWidth(PaneRect bounds, WindowMinimumSize minimumSize)
    {
        return Math.Min(Math.Max(RuntimeLinkedResizeMinWidth, minimumSize.Width), Math.Max(1, bounds.Width));
    }

    private static double GetRuntimeLinkedMinHeight(PaneRect bounds, WindowMinimumSize minimumSize)
    {
        return Math.Min(Math.Max(RuntimeLinkedResizeMinHeight, minimumSize.Height), Math.Max(1, bounds.Height));
    }
}
