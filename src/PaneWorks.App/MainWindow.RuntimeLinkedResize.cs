using PaneWorks.App.Diagnostics;
using PaneWorks.Core.Models;
using PaneWorks.Core.Services;
using PaneWorks.Infrastructure.Windows;

namespace PaneWorks.App;

public partial class MainWindow
{
    private void StartRuntimeLinkedResizeLoop(IntPtr windowHandle)
    {
        if (!TryCreateRuntimeLinkedResizeSession(windowHandle, out var session))
        {
            PaneWorksLog.Info($"Runtime linked resize skipped: 0x{windowHandle.ToInt64():X}");
            return;
        }

        StopRuntimeLinkedResizeLoop();
        _runtimeLinkedResizeCancellation = new CancellationTokenSource();
        var cancellationToken = _runtimeLinkedResizeCancellation.Token;
        var resizeGeneration = Interlocked.Increment(ref _runtimeLinkedResizeGeneration);
        _runtimeLinkedResizeActive = true;
        _runtimeLinkedResizeSession = session;
        _movingWindowDetachCandidate = false;

        PaneWorksLog.Info($"Runtime linked resize started: 0x{windowHandle.ToInt64():X}, edge={session.Edge}, neighbors={session.Neighbors.Count}, magnets={session.MagnetCandidates.Count}");
        _ = Task.Factory.StartNew(
            () => RunRuntimeLinkedResizeLoop(session, resizeGeneration, cancellationToken),
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private void StopRuntimeLinkedResizeLoop()
    {
        if (_runtimeLinkedResizeCancellation is not null)
        {
            _runtimeLinkedResizeCancellation.Cancel();
            _runtimeLinkedResizeCancellation = null;
            _ = Interlocked.Increment(ref _runtimeLinkedResizeGeneration);
        }

        _runtimeLinkedResizeActive = false;
        _runtimeLinkedResizeSession = null;
    }

    private void RunRuntimeLinkedResizeLoop(
        RuntimeLinkedResizeSession session,
        long resizeGeneration,
        CancellationToken cancellationToken)
    {
        var lastEdgePosition = double.NaN;
        var activeNeighbors = session.Neighbors.ToList();
        var magnetCandidates = session.MagnetCandidates.ToList();
        var minEdgePosition = session.MinEdgePosition;
        var maxEdgePosition = session.MaxEdgePosition;

        Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        _ = timeBeginPeriod(1);
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsPrimaryMouseButtonPressed())
            {
                var moved = false;
                if (_workspaceApplyService.TryGetVisibleWindowBounds(session.SourceWindowHandle, out var sourceBounds))
                {
                    var rawEdgePosition = GetEdgePosition(sourceBounds, session.Edge);
                    var edgePosition = ClampRuntimeLinkedEdgePosition(rawEdgePosition, minEdgePosition, maxEdgePosition);
                    var isEdgeClamped = Math.Abs(edgePosition - rawEdgePosition) >= 0.5;
                    var sourceBoundsAtEdge = GetBoundsForRuntimeLinkedResizeEdge(
                        session.SourceInitialBounds,
                        session.SourceMinimumSize,
                        session.Edge,
                        edgePosition);
                    var magnetAttached = TryAttachRuntimeEdgeMagnetCandidate(
                        session,
                        activeNeighbors,
                        magnetCandidates,
                        sourceBoundsAtEdge,
                        edgePosition,
                        ref minEdgePosition,
                        ref maxEdgePosition,
                        out var magnetEdgePosition,
                        out var magnetLockedEdge);
                    if (magnetAttached)
                    {
                        edgePosition = ClampRuntimeLinkedEdgePosition(magnetEdgePosition, minEdgePosition, maxEdgePosition);
                    }

                    if (double.IsNaN(lastEdgePosition)
                        || Math.Abs(edgePosition - lastEdgePosition) >= 1
                        || isEdgeClamped
                        || magnetAttached)
                    {
                        var updates = BuildRuntimeLinkedResizeUpdates(
                            session,
                            activeNeighbors,
                            edgePosition,
                            includeSourceWindow: isEdgeClamped || magnetAttached);
                        if (updates.Count > 0)
                        {
                            var stopAtLockedEdge = isEdgeClamped || magnetLockedEdge;
                            _suppressMoveEventsUntil = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(stopAtLockedEdge ? 350 : 120);
                            if (stopAtLockedEdge)
                            {
                                _workspaceApplyService.CancelWindowDrag(session.SourceWindowHandle);
                            }

                            var immediateUpdates = updates
                                .Where(update => !_workspaceApplyService.UsesConservativeSnapHandling(update.WindowHandle))
                                .ToList();
                            if (immediateUpdates.Count > 0)
                            {
                                _workspaceApplyService.MoveSnappedWindowsToBounds(immediateUpdates);
                                moved = true;
                            }

                            if (stopAtLockedEdge)
                            {
                                PaneWorksLog.Info($"Runtime linked resize stopped at locked edge: 0x{session.SourceWindowHandle.ToInt64():X}, edge={edgePosition:0}");
                                break;
                            }
                        }

                        lastEdgePosition = edgePosition;
                    }
                }

                if (moved)
                {
                    WaitForDesktopFrame();
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
        }
        catch (Exception exception)
        {
            Dispatcher.BeginInvoke(() => PaneWorksLog.Error("Runtime linked resize loop failed", exception));
        }
        finally
        {
            _ = timeEndPeriod(1);
            Dispatcher.BeginInvoke(() =>
            {
                if (_runtimeLinkedResizeGeneration == resizeGeneration
                    && _runtimeLinkedResizeActive
                    && _movingWindowHandle == session.SourceWindowHandle
                    && _movingWindowSnapResizeGesture)
                {
                    ApplyDeferredConservativeRuntimeResize(session, activeNeighbors);
                    FinishRuntimeLinkedResizeGesture(session);
                }
            });
        }
    }

    private List<WindowBoundsUpdate> BuildRuntimeLinkedResizeUpdates(
        RuntimeLinkedResizeSession session,
        IReadOnlyList<RuntimeLinkedResizeNeighbor> activeNeighbors,
        double edgePosition,
        bool includeSourceWindow)
    {
        var updates = new List<WindowBoundsUpdate>(activeNeighbors.Count + (includeSourceWindow ? 1 : 0));
        if (includeSourceWindow)
        {
            updates.Add(new WindowBoundsUpdate(
                session.SourceWindowHandle,
                GetBoundsForRuntimeLinkedResizeEdge(session.SourceInitialBounds, session.SourceMinimumSize, session.Edge, edgePosition),
                session.SourceFrameAdjustment));
        }

        foreach (var neighbor in activeNeighbors)
        {
            var bounds = GetRuntimeLinkedNeighborBounds(
                neighbor.InitialBounds,
                neighbor.MinimumSize,
                session.Edge,
                neighbor.Side,
                edgePosition);
            if (AreBoundsClose(bounds, neighbor.InitialBounds, 0.5))
            {
                continue;
            }

            updates.Add(new WindowBoundsUpdate(neighbor.WindowHandle, bounds, neighbor.FrameAdjustment));
        }

        return updates;
    }

    private void ApplyDeferredConservativeRuntimeResize(
        RuntimeLinkedResizeSession session,
        IReadOnlyList<RuntimeLinkedResizeNeighbor> activeNeighbors)
    {
        if (!_workspaceApplyService.TryGetVisibleWindowBounds(session.SourceWindowHandle, out var sourceBounds))
        {
            return;
        }

        var edgePosition = ClampRuntimeLinkedEdgePosition(
            GetEdgePosition(sourceBounds, session.Edge),
            session.MinEdgePosition,
            session.MaxEdgePosition);
        var deferredUpdates = BuildRuntimeLinkedResizeUpdates(
                session,
                activeNeighbors,
                edgePosition,
                includeSourceWindow: true)
            .Where(update => _workspaceApplyService.UsesConservativeSnapHandling(update.WindowHandle))
            .ToList();
        if (deferredUpdates.Count > 0)
        {
            _workspaceApplyService.MoveSnappedWindowsToBounds(deferredUpdates);
        }
    }

    private void FinishRuntimeLinkedResizeGesture(RuntimeLinkedResizeSession session)
    {
        PaneWorksLog.Info($"Runtime linked resize finished: 0x{session.SourceWindowHandle.ToInt64():X}");
        _runtimeLinkedResizeCancellation = null;
        _ = Interlocked.Increment(ref _runtimeLinkedResizeGeneration);
        _runtimeLinkedResizeActive = false;
        _runtimeLinkedResizeSession = null;

        CaptureCurrentRuntimeBoundsForDisplay(session.DisplayId);
        TryUpdateSessionSnapLayoutFromWindowBounds(session.SourceWindowHandle);
        ResetMovingWindowState();
        EnsureSnapOverlayHidden();
    }

    private void CaptureCurrentRuntimeBoundsForDisplay(string? displayId)
    {
        foreach (var binding in _snapBindings.ToList())
        {
            if (!string.IsNullOrWhiteSpace(displayId)
                && !string.Equals(binding.Value.DisplayId, displayId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (_workspaceApplyService.TryGetVisibleWindowBounds(binding.Key, out var bounds))
            {
                _snapRuntimeBounds[binding.Key] = bounds;
            }
        }
    }
}
