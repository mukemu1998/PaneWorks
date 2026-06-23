using PaneWorks.Core.Models;

namespace PaneWorks.Core.Services;

public sealed class LayoutGeometryCalculator
{
    public LayoutGeometryResult Compute(LayoutDocument document, PaneRect rootBounds, double splitterThickness)
    {
        var regions = new List<ComputedRegion>();
        var splitters = new List<ComputedSplitter>();

        ComputeNode(document.Root, rootBounds, splitterThickness, regions, splitters);

        return new LayoutGeometryResult(regions, splitters);
    }

    private static void ComputeNode(
        LayoutNode node,
        PaneRect bounds,
        double splitterThickness,
        ICollection<ComputedRegion> regions,
        ICollection<ComputedSplitter> splitters)
    {
        if (node is LeafNode leaf)
        {
            regions.Add(new ComputedRegion(leaf.Id, bounds));
            return;
        }

        var split = (SplitNode)node;

        if (split.Direction == SplitDirection.Vertical)
        {
            var availableWidth = Math.Max(0, bounds.Width - splitterThickness);
            var firstWidth = availableWidth * split.Ratio;
            var secondWidth = Math.Max(0, availableWidth - firstWidth);

            var firstBounds = new PaneRect(bounds.X, bounds.Y, firstWidth, bounds.Height);
            var splitterBounds = new PaneRect(bounds.X + firstWidth, bounds.Y, splitterThickness, bounds.Height);
            var secondBounds = new PaneRect(bounds.X + firstWidth + splitterThickness, bounds.Y, secondWidth, bounds.Height);

            splitters.Add(new ComputedSplitter(split.Id, splitterBounds, split.Direction, bounds, split.Ratio));
            ComputeNode(split.First, firstBounds, splitterThickness, regions, splitters);
            ComputeNode(split.Second, secondBounds, splitterThickness, regions, splitters);
            return;
        }

        var availableHeight = Math.Max(0, bounds.Height - splitterThickness);
        var firstHeight = availableHeight * split.Ratio;
        var secondHeight = Math.Max(0, availableHeight - firstHeight);

        var topBounds = new PaneRect(bounds.X, bounds.Y, bounds.Width, firstHeight);
        var splitterRect = new PaneRect(bounds.X, bounds.Y + firstHeight, bounds.Width, splitterThickness);
        var bottomBounds = new PaneRect(bounds.X, bounds.Y + firstHeight + splitterThickness, bounds.Width, secondHeight);

        splitters.Add(new ComputedSplitter(split.Id, splitterRect, split.Direction, bounds, split.Ratio));
        ComputeNode(split.First, topBounds, splitterThickness, regions, splitters);
        ComputeNode(split.Second, bottomBounds, splitterThickness, regions, splitters);
    }
}
