using System.Windows;
using System.Windows.Threading;
using PaneWorks.App.ViewModels;
using PaneWorks.Core.Models;
using PaneWorks.Core.Services;
using PaneWorks.Infrastructure.Windows;
using WpfPoint = System.Windows.Point;

namespace PaneWorks.App;

public partial class MainWindow : Window
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
    private readonly WindowMoveMonitor _windowMoveMonitor = new();
    private readonly WorkspaceApplyService _workspaceApplyService = new();
    private readonly LayoutGeometryCalculator _geometryCalculator = new();
    private readonly LayoutEditorService _runtimeSessionLayoutEditorService = new();
    private readonly SplitResizeService _splitResizeService = new();
    private readonly DisplayDiscoveryService _displayDiscoveryService = new();
    private readonly DispatcherTimer _snapAssistTimer;
    private readonly DispatcherTimer _snapAssistHealthTimer;
    private readonly DispatcherTimer _snapAssistFallbackTimer;
    private readonly Dictionary<IntPtr, SnapBindingState> _snapBindings = new();
    private readonly Dictionary<IntPtr, PaneRect> _snapRuntimeBounds = new();
    private readonly Dictionary<IntPtr, VisibleWindowInfo> _snapWindowInfoCache = new();
    private readonly Dictionary<string, LayoutDocument> _sessionSnapLayoutDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SnapOverlayWindow> _snapOverlayWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<IntPtr, DateTimeOffset> _snapSuppressUntilByWindow = new();
    private PaneRect _virtualDesktopBounds;
    private IntPtr _movingWindowHandle;
    private ComputedRegion? _hoveredSnapRegion;
    private string? _hoveredSnapDisplayId;
    private ComputedRegion? _lastSnapAssistRegion;
    private string? _lastSnapAssistDisplayId;
    private DateTimeOffset _lastSnapAssistTargetAt;
    private IntPtr _lastSnapAssistWindowHandle;
    private PaneRect? _movingWindowInitialBounds;
    private PaneRect? _pendingDetachedRestoreBounds;
    private DateTimeOffset? _movingWindowStartedAt;
    private DateTimeOffset? _movingWindowMouseReleasedAt;
    private DateTimeOffset _lastMoveStartEventAt;
    private IntPtr _lastMoveStartEventHandle;
    private DateTimeOffset _lastDetachedRestoreApplyAt;
    private DateTimeOffset _suppressMoveEventsUntil;
    private int _internalWindowLayoutUpdateDepth;
    private double _movingWindowDragAnchorRatioX = 0.5;
    private double _movingWindowDragAnchorOffsetY = 24;
    private PaneRect? _manualDetachedDragLastBounds;
    private WindowFrameAdjustment _manualDetachedDragFrameAdjustment;
    private CancellationTokenSource? _manualDetachedDragCancellation;
    private long _manualDetachedDragGeneration;
    private CancellationTokenSource? _runtimeLinkedResizeCancellation;
    private long _runtimeLinkedResizeGeneration;
    private RuntimeLinkedResizeSession? _runtimeLinkedResizeSession;
    private bool _detachedRestoreSettled;
    private bool _manualDetachedDragActive;
    private bool _runtimeLinkedResizeActive;
    private bool _isSnapAssistArmed;
    private bool _movingWindowWasSnapped;
    private bool _movingWindowDetachedFromSnapGroup;
    private bool _movingWindowDetachCandidate;
    private bool _movingWindowSnapResizeGesture;
    private bool _movingWindowStartedByForegroundFallback;
    private bool _isWorkbenchPanelDragging;
    private WpfPoint _workbenchPanelDragStartPoint;
    private double _workbenchPanelDragStartOffsetX;
    private double _workbenchPanelDragStartOffsetY;
    private bool _isWorkbenchMiniBarPointerDown;
    private bool _isWorkbenchMiniBarDragging;
    private WpfPoint _workbenchMiniBarDragStartPoint;
    private double _workbenchMiniBarDragStartOffsetX;
    private double _workbenchMiniBarDragStartOffsetY;

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        _snapAssistTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _snapAssistHealthTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _snapAssistFallbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };

        _snapAssistTimer.Tick += SnapAssistTimer_Tick;
        _snapAssistHealthTimer.Tick += SnapAssistHealthTimer_Tick;
        _snapAssistFallbackTimer.Tick += SnapAssistFallbackTimer_Tick;
        EditorCanvas.NodeSelected += EditorCanvas_NodeSelected;
        EditorCanvas.SplitterRatioChanged += EditorCanvas_SplitterRatioChanged;
        EditorCanvas.CanvasContextActionRequested += EditorCanvas_CanvasContextActionRequested;
        EditorCanvas.SnapLayoutSwitchRequested += EditorCanvas_SnapLayoutSwitchRequested;
        EditorCanvas.WorkspaceProfileSwitchRequested += EditorCanvas_WorkspaceProfileSwitchRequested;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private enum SnapAssistMode
    {
        None,
        SavedLayout,
        RuntimeSession
    }
}
