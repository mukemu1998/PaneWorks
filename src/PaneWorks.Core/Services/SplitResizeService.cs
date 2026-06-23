using PaneWorks.Core.Models;

namespace PaneWorks.Core.Services;

public sealed class SplitResizeService
{
    public double GetCandidateRatio(ComputedSplitter splitter, double axisPosition, double splitterThickness)
    {
        var availableLength = GetAvailableLength(splitter, splitterThickness);
        if (availableLength <= 0)
        {
            return splitter.CurrentRatio;
        }

        var offset = splitter.Direction == SplitDirection.Vertical
            ? axisPosition - splitter.HostBounds.X
            : axisPosition - splitter.HostBounds.Y;

        return offset / availableLength;
    }

    public double ClampRatio(ComputedSplitter splitter, double candidateRatio, double minRegionSize, double splitterThickness)
    {
        var availableLength = GetAvailableLength(splitter, splitterThickness);
        if (availableLength <= 0)
        {
            return splitter.CurrentRatio;
        }

        var minRatio = minRegionSize / availableLength;
        if (minRatio >= 0.5)
        {
            return 0.5;
        }

        var maxRatio = 1 - minRatio;
        return Math.Clamp(candidateRatio, minRatio, maxRatio);
    }

    private static double GetAvailableLength(ComputedSplitter splitter, double splitterThickness)
    {
        return splitter.Direction == SplitDirection.Vertical
            ? Math.Max(0, splitter.HostBounds.Width - splitterThickness)
            : Math.Max(0, splitter.HostBounds.Height - splitterThickness);
    }
}
