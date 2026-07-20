using System.Windows;
using System.Windows.Threading;
using PaneWorks.App.ViewModels;
using PaneWorks.Core.Models;
using PaneWorks.Core.Services;
using PaneWorks.Infrastructure.Windows;
using System.Windows.Media;
using WpfPoint = System.Windows.Point;

namespace PaneWorks.App;

public partial class MainWindow : Window
{
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
    private bool _isSnapAssistPausedByUser;
    private bool _isWorkbenchPanelDragging;
    private CacheMode? _workbenchPanelOriginalCacheMode;
    private WpfPoint _workbenchPanelDragStartPoint;
    private double _workbenchPanelDragStartOffsetX;
    private double _workbenchPanelDragStartOffsetY;
    private bool _isWorkbenchMiniBarPointerDown;
    private bool _isWorkbenchMiniBarDragging;
    private string? _workbenchPanelAnchorDisplayId;
    private double? _workbenchPanelAnchoredTop;
    private WpfPoint _workbenchMiniBarDragStartPoint;
    private double _workbenchMiniBarDragStartOffsetX;
    private double _workbenchMiniBarDragStartOffsetY;

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    internal bool IsAutomaticUpdateCheckEnabled => ViewModel.AutoCheckForUpdates;

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
        EditorCanvas.DisplayEditRequested += EditorCanvas_DisplayEditRequested;
        EditorCanvas.CanvasContextActionRequested += EditorCanvas_CanvasContextActionRequested;
        EditorCanvas.SnapLayoutSwitchRequested += EditorCanvas_SnapLayoutSwitchRequested;
        EditorCanvas.WorkspaceProfileSwitchRequested += EditorCanvas_WorkspaceProfileSwitchRequested;
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Opacity = 0;
    }

    private enum SnapAssistMode
    {
        None,
        SavedLayout,
        RuntimeSession
    }
}
