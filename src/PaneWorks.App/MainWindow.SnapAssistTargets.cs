using PaneWorks.App.Diagnostics;
using PaneWorks.Core.Models;
using PaneWorks.Core.Services;
using WpfPoint = System.Windows.Point;

namespace PaneWorks.App;

public partial class MainWindow
{
    private static bool Contains(PaneRect rect, WpfPoint point)
    {
        return point.X >= rect.X
            && point.X <= rect.X + rect.Width
            && point.Y >= rect.Y
            && point.Y <= rect.Y + rect.Height;
    }

    private ComputedRegion? ResolveHoveredSnapRegion(LayoutGeometryResult geometry, string displayId, WpfPoint cursorInDevicePixels)
    {
        if (geometry.Regions.Count == 0)
        {
            return null;
        }

        PaneRect movingBounds = default;
        var hasMovingBounds = _movingWindowHandle != IntPtr.Zero
            && _workspaceApplyService.TryGetVisibleWindowBounds(_movingWindowHandle, out movingBounds);
        var candidates = geometry.Regions
            .Select(region =>
            {
                var containsCursor = Contains(region.Bounds, cursorInDevicePixels);
                var distance = GetDistanceToRect(region.Bounds, cursorInDevicePixels);
                var overlapArea = hasMovingBounds ? GetOverlapArea(region.Bounds, movingBounds) : 0;
                return new SnapRegionCandidate(region, containsCursor, distance, overlapArea);
            })
            .Where(candidate =>
                candidate.ContainsCursor
                || candidate.Distance <= SnapAssistRegionProximityTolerance
                || candidate.OverlapArea > 0)
            .ToList();

        var bestCandidate = candidates
            .OrderByDescending(candidate => candidate.ContainsCursor)
            .ThenByDescending(candidate => candidate.OverlapArea)
            .ThenBy(candidate => candidate.Distance)
            .FirstOrDefault();

        if (bestCandidate is null)
        {
            return null;
        }

        if (_hoveredSnapRegion is null
            || !string.Equals(_hoveredSnapDisplayId, displayId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(_hoveredSnapRegion.NodeId, bestCandidate.Region.NodeId, StringComparison.Ordinal))
        {
            return bestCandidate.Region;
        }

        var currentCandidate = candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.Region.NodeId, _hoveredSnapRegion.NodeId, StringComparison.Ordinal));
        if (currentCandidate is null)
        {
            return bestCandidate.Region;
        }

        if (currentCandidate.ContainsCursor && !bestCandidate.ContainsCursor)
        {
            return currentCandidate.Region;
        }

        if (bestCandidate.ContainsCursor && !currentCandidate.ContainsCursor)
        {
            return bestCandidate.Region;
        }

        var bestClearlyCloser = bestCandidate.Distance + SnapAssistRegionSwitchDistanceMargin < currentCandidate.Distance;
        var bestClearlyLargerOverlap = bestCandidate.OverlapArea > currentCandidate.OverlapArea + SnapAssistRegionSwitchOverlapMargin;
        return bestClearlyCloser || bestClearlyLargerOverlap
            ? bestCandidate.Region
            : currentCandidate.Region;
    }

    private void RememberSnapAssistTarget(ComputedRegion? region, string? displayId)
    {
        if (region is null || string.IsNullOrWhiteSpace(displayId) || _movingWindowHandle == IntPtr.Zero)
        {
            return;
        }

        _lastSnapAssistRegion = region;
        _lastSnapAssistDisplayId = displayId;
        _lastSnapAssistTargetAt = DateTimeOffset.UtcNow;
        _lastSnapAssistWindowHandle = _movingWindowHandle;
    }

    private bool TryResolveSnapTargetForRelease(out ComputedRegion region, out string displayId)
    {
        if (_hoveredSnapRegion is not null && !string.IsNullOrWhiteSpace(_hoveredSnapDisplayId))
        {
            region = _hoveredSnapRegion;
            displayId = _hoveredSnapDisplayId;
            return true;
        }

        if (_lastSnapAssistRegion is not null
            && _lastSnapAssistWindowHandle == _movingWindowHandle
            && !string.IsNullOrWhiteSpace(_lastSnapAssistDisplayId)
            && DateTimeOffset.UtcNow - _lastSnapAssistTargetAt <= SnapAssistTargetMemoryDuration)
        {
            region = _lastSnapAssistRegion;
            displayId = _lastSnapAssistDisplayId;
            PaneWorksLog.Info($"Use remembered snap target: 0x{_movingWindowHandle.ToInt64():X}, node={region.NodeId}");
            return true;
        }

        region = default!;
        displayId = string.Empty;
        return false;
    }

    private void ClearSnapAssistTargetMemory()
    {
        _lastSnapAssistRegion = null;
        _lastSnapAssistDisplayId = null;
        _lastSnapAssistTargetAt = DateTimeOffset.MinValue;
        _lastSnapAssistWindowHandle = IntPtr.Zero;
    }

    private static double GetDistanceToRect(PaneRect rect, WpfPoint point)
    {
        var dx = point.X < rect.X
            ? rect.X - point.X
            : point.X > GetRight(rect)
                ? point.X - GetRight(rect)
                : 0;
        var dy = point.Y < rect.Y
            ? rect.Y - point.Y
            : point.Y > GetBottom(rect)
                ? point.Y - GetBottom(rect)
                : 0;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double GetOverlapArea(PaneRect first, PaneRect second)
    {
        return GetAxisOverlap(first.X, GetRight(first), second.X, GetRight(second))
            * GetAxisOverlap(first.Y, GetBottom(first), second.Y, GetBottom(second));
    }

    private sealed record SnapRegionCandidate(
        ComputedRegion Region,
        bool ContainsCursor,
        double Distance,
        double OverlapArea);
}
