using PaneWorks.Core.Models;
using WpfPoint = System.Windows.Point;

namespace PaneWorks.App;

public partial class MainWindow
{
    private bool IsResizeGesture(PaneRect? initialBounds)
    {
        if (initialBounds is null || !TryGetCursorPosition(out var cursor))
        {
            return false;
        }

        var bounds = initialBounds.Value;
        var nearLeft = Math.Abs(cursor.X - bounds.X) <= ResizeGripThreshold;
        var nearRight = Math.Abs(cursor.X - (bounds.X + bounds.Width)) <= ResizeGripThreshold;
        var nearTop = Math.Abs(cursor.Y - bounds.Y) <= ResizeGripThreshold;
        var nearBottom = Math.Abs(cursor.Y - (bounds.Y + bounds.Height)) <= ResizeGripThreshold;

        return nearLeft || nearRight || nearTop || nearBottom;
    }

    private static ResizeEdge? GetResizeEdge(PaneRect bounds, WpfPoint cursor)
    {
        ResizeEdge? edge = null;
        var bestDistance = ResizeGripThreshold + 1;

        ConsiderEdge(ResizeEdge.Left, Math.Abs(cursor.X - bounds.X));
        ConsiderEdge(ResizeEdge.Right, Math.Abs(cursor.X - (bounds.X + bounds.Width)));
        ConsiderEdge(ResizeEdge.Top, Math.Abs(cursor.Y - bounds.Y));
        ConsiderEdge(ResizeEdge.Bottom, Math.Abs(cursor.Y - (bounds.Y + bounds.Height)));

        return edge;

        void ConsiderEdge(ResizeEdge candidateEdge, double distance)
        {
            if (distance > ResizeGripThreshold || distance >= bestDistance)
            {
                return;
            }

            edge = candidateEdge;
            bestDistance = distance;
        }
    }
}
