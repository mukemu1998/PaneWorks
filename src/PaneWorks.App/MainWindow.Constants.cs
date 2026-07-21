namespace PaneWorks.App;

public partial class MainWindow
{
    private const double SnapOverlayInset = 0;
    private const double SnapVisualSplitterThickness = 0;
    private const double SnapTargetSplitterThickness = 0;
    private const double SnapLiveMinRegionSize = 120;
    private const double ResizeDetectionThreshold = 2;
    private const double ResizeGripThreshold = 16;
    private const double WindowBoundsTolerance = 3;
    private const double RuntimeLinkedResizeTolerance = 10;
    private const double RuntimeEdgeMagnetTolerance = 12;
    private const double RuntimeEdgeMagnetMinimumOverlap = 36;
    private const double RuntimeEdgeAlignmentAdjacencyTolerance = 18;
    private const double SnapAssistRegionProximityTolerance = 48;
    private const double SnapAssistRegionSwitchDistanceMargin = 12;
    private const double SnapAssistRegionSwitchOverlapMargin = 4096;
    private const double TemporaryInsertEdgeThreshold = 260;
    private const double TemporaryInsertMinimumEdgeThreshold = 64;
    private const double TemporaryInsertEdgeRatio = 0.25;
    private const double TemporaryInsertDividerTolerance = 22;
    private const double RuntimeLinkedResizeMinWidth = 120;
    private const double RuntimeLinkedResizeMinHeight = 80;
    private const double DragAnchorMinRatio = 0.08;
    private const double DragAnchorMaxRatio = 0.92;
    private const double DragAnchorMinOffsetY = 12;
    private const double DragAnchorMaxOffsetY = 48;
    private const int VirtualKeyLeftButton = 0x01;
    private const int Win32ErrorAccessDenied = 5;
    private const uint GetAncestorRoot = 2;
    private const uint GetWindowNext = 2;

    private static readonly bool EnableRuntimeLinkedResize = true;
    private static readonly TimeSpan DetachedRestoreApplyInterval = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan InternalLayoutMoveSuppressDuration = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan SnapAssistTargetMemoryDuration = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan WorkspaceLaunchInitialRestoreDelay = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan WorkspaceLaunchRestoreRetryInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan WorkspaceLaunchRestoreTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan[] WorkspaceLaunchSnapStabilizationDelays =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(6),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(45),
        TimeSpan.FromSeconds(60)
    };
}
