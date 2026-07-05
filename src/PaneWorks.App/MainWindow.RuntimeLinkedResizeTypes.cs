using PaneWorks.Core.Models;
using PaneWorks.Infrastructure.Windows;

namespace PaneWorks.App;

public partial class MainWindow
{
    private sealed class ResizeCandidate
    {
        public ResizeCandidate(string displayId, ComputedSplitter splitter, SplitDirection direction, bool usesLeadingEdge)
        {
            DisplayId = displayId;
            Splitter = splitter;
            Direction = direction;
            UsesLeadingEdge = usesLeadingEdge;
        }

        public string DisplayId { get; }

        public ComputedSplitter Splitter { get; }

        public SplitDirection Direction { get; }

        public bool UsesLeadingEdge { get; }
    }

    private sealed record RuntimeLinkedResizeSession(
        IntPtr SourceWindowHandle,
        string DisplayId,
        PaneRect SourceInitialBounds,
        WindowMinimumSize SourceMinimumSize,
        WindowFrameAdjustment SourceFrameAdjustment,
        ResizeEdge Edge,
        double MinEdgePosition,
        double MaxEdgePosition,
        IReadOnlyList<RuntimeLinkedResizeNeighbor> Neighbors,
        IReadOnlyList<RuntimeEdgeMagnetCandidate> MagnetCandidates);

    private sealed record RuntimeLinkedResizeNeighbor(
        IntPtr WindowHandle,
        PaneRect InitialBounds,
        RuntimeLinkedResizeSide Side,
        WindowMinimumSize MinimumSize,
        WindowFrameAdjustment FrameAdjustment);

    private sealed record RuntimeEdgeMagnetCandidate(
        IntPtr WindowHandle,
        PaneRect InitialBounds,
        RuntimeLinkedResizeSide Side,
        double TargetEdgePosition,
        RuntimeEdgeMagnetAction Action,
        WindowMinimumSize MinimumSize,
        WindowFrameAdjustment FrameAdjustment);

    private enum RuntimeEdgeMagnetAction
    {
        LinkOppositeEdge,
        AlignSourceEdge
    }

    private enum RuntimeLinkedResizeSide
    {
        SameSide,
        OppositeSide
    }

    private enum ResizeEdge
    {
        Left,
        Right,
        Top,
        Bottom
    }
}
