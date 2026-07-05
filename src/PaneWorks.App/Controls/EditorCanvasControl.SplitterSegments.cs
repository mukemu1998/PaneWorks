using PaneWorks.Core.Models;

namespace PaneWorks.App.Controls;

public sealed partial class EditorCanvasControl
{
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

    private sealed record SplitterDrawSegment(
        SplitDirection Direction,
        double AxisPosition,
        double Start,
        double End,
        bool IsSelected);
}
