using PaneWorks.Core.Models;
using PaneWorks.Core.Services;

namespace PaneWorks.App;

public partial class MainWindow
{
    private bool TryUpdateSessionSnapLayoutFromWindowBounds(IntPtr windowHandle)
    {
        if (!TryResolveSnapResize(
                windowHandle,
                GetRuntimeSessionLayoutDocumentForDisplay,
                out var resizeCandidate,
                out var clampedRatio))
        {
            return false;
        }

        var sourceDocument = GetRuntimeSessionLayoutDocumentForDisplay(resizeCandidate.DisplayId);
        var result = _runtimeSessionLayoutEditorService.UpdateSplitRatio(
            sourceDocument,
            resizeCandidate.Splitter.SplitNodeId,
            clampedRatio);
        if (!result.Changed)
        {
            return false;
        }

        _sessionSnapLayoutDocuments[resizeCandidate.DisplayId] = result.Document;
        return true;
    }

    private bool TryResolveSnapResize(
        IntPtr windowHandle,
        Func<string, LayoutDocument> layoutResolver,
        out ResizeCandidate resizeCandidate,
        out double clampedRatio)
    {
        resizeCandidate = new ResizeCandidate(
            string.Empty,
            new ComputedSplitter(string.Empty, new PaneRect(), SplitDirection.Vertical, new PaneRect(), 0.5),
            SplitDirection.Vertical,
            usesLeadingEdge: false);
        clampedRatio = default;

        if (!_snapBindings.TryGetValue(windowHandle, out var binding)
            || !ViewModel.TryGetDisplayById(binding.DisplayId, out var display)
            || !_workspaceApplyService.TryGetVisibleWindowBounds(windowHandle, out var currentBounds))
        {
            return false;
        }

        if (!HasMeaningfulResize(currentBounds))
        {
            return false;
        }

        var stageBounds = GetSnapTargetStageBounds(display);
        var layoutDocument = layoutResolver(binding.DisplayId);
        var geometry = _geometryCalculator.Compute(
            layoutDocument,
            stageBounds,
            SnapTargetSplitterThickness);
        var region = geometry.Regions.FirstOrDefault(item => item.NodeId == binding.NodeId);
        if (region is null)
        {
            return false;
        }

        var splittersById = geometry.Splitters.ToDictionary(item => item.SplitNodeId, StringComparer.Ordinal);
        var matchedCandidate = FindResizeCandidate(
            binding.DisplayId,
            layoutDocument.Root,
            binding.NodeId,
            region.Bounds,
            currentBounds,
            splittersById,
            new List<ResizeCandidate>());

        if (matchedCandidate is null)
        {
            return false;
        }

        resizeCandidate = matchedCandidate;

        var axisPosition = resizeCandidate.Direction switch
        {
            SplitDirection.Vertical when resizeCandidate.UsesLeadingEdge => currentBounds.X,
            SplitDirection.Vertical => currentBounds.X + currentBounds.Width,
            SplitDirection.Horizontal when resizeCandidate.UsesLeadingEdge => currentBounds.Y,
            _ => currentBounds.Y + currentBounds.Height
        };

        var candidateRatio = _splitResizeService.GetCandidateRatio(
            resizeCandidate.Splitter,
            axisPosition,
            SnapTargetSplitterThickness);

        clampedRatio = _splitResizeService.ClampRatio(
            resizeCandidate.Splitter,
            candidateRatio,
            SnapLiveMinRegionSize,
            SnapTargetSplitterThickness);

        return true;
    }

    private bool HasMeaningfulResize(PaneRect currentBounds)
    {
        if (_movingWindowInitialBounds is null)
        {
            return true;
        }

        return Math.Abs(currentBounds.Width - _movingWindowInitialBounds.Value.Width) >= ResizeDetectionThreshold
            || Math.Abs(currentBounds.Height - _movingWindowInitialBounds.Value.Height) >= ResizeDetectionThreshold;
    }

    private ResizeCandidate? FindResizeCandidate(
        string displayId,
        LayoutNode currentNode,
        string targetNodeId,
        PaneRect originalBounds,
        PaneRect currentBounds,
        IReadOnlyDictionary<string, ComputedSplitter> splittersById,
        List<ResizeCandidate> path)
    {
        if (currentNode.Id == targetNodeId)
        {
            return SelectResizeCandidate(path, originalBounds, currentBounds);
        }

        if (currentNode is not SplitNode split)
        {
            return null;
        }

        if (!splittersById.TryGetValue(split.Id, out var splitter))
        {
            return null;
        }

        path.Add(new ResizeCandidate(displayId, splitter, split.Direction, usesLeadingEdge: false));
        var firstResult = FindResizeCandidate(displayId, split.First, targetNodeId, originalBounds, currentBounds, splittersById, path);
        path.RemoveAt(path.Count - 1);
        if (firstResult is not null)
        {
            return firstResult;
        }

        path.Add(new ResizeCandidate(displayId, splitter, split.Direction, usesLeadingEdge: true));
        var secondResult = FindResizeCandidate(displayId, split.Second, targetNodeId, originalBounds, currentBounds, splittersById, path);
        path.RemoveAt(path.Count - 1);
        return secondResult;
    }

    private static ResizeCandidate? SelectResizeCandidate(
        IReadOnlyList<ResizeCandidate> path,
        PaneRect originalBounds,
        PaneRect currentBounds)
    {
        var leftDelta = Math.Abs(currentBounds.X - originalBounds.X);
        var rightDelta = Math.Abs((currentBounds.X + currentBounds.Width) - (originalBounds.X + originalBounds.Width));
        var topDelta = Math.Abs(currentBounds.Y - originalBounds.Y);
        var bottomDelta = Math.Abs((currentBounds.Y + currentBounds.Height) - (originalBounds.Y + originalBounds.Height));

        var maxDelta = new[] { leftDelta, rightDelta, topDelta, bottomDelta }.Max();
        if (maxDelta < ResizeDetectionThreshold)
        {
            return null;
        }

        if (Math.Abs(maxDelta - leftDelta) < 0.001)
        {
            return path.LastOrDefault(item => item.Direction == SplitDirection.Vertical && item.UsesLeadingEdge);
        }

        if (Math.Abs(maxDelta - rightDelta) < 0.001)
        {
            return path.LastOrDefault(item => item.Direction == SplitDirection.Vertical && !item.UsesLeadingEdge);
        }

        if (Math.Abs(maxDelta - topDelta) < 0.001)
        {
            return path.LastOrDefault(item => item.Direction == SplitDirection.Horizontal && item.UsesLeadingEdge);
        }

        return path.LastOrDefault(item => item.Direction == SplitDirection.Horizontal && !item.UsesLeadingEdge);
    }
}
