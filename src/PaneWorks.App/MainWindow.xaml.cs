using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PaneWorks.App.Controls;
using PaneWorks.App.Diagnostics;
using PaneWorks.App.Views;
using PaneWorks.App.ViewModels;
using PaneWorks.Core.Models;
using PaneWorks.Core.Services;
using PaneWorks.Infrastructure.Windows;
using WpfApplication = System.Windows.Application;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfTextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

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

    public IReadOnlyList<LayoutListItemViewModel> GetTrayLayoutItems()
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.Invoke(GetTrayLayoutItems);
        }

        return ViewModel.Layouts
            .Select(item => new LayoutListItemViewModel
            {
                Id = item.Id,
                Name = item.Name,
                Description = item.Description
            })
            .ToList();
    }

    public IReadOnlyList<LayoutListItemViewModel> GetTrayWorkspaceProfileItems()
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.Invoke(GetTrayWorkspaceProfileItems);
        }

        return ViewModel.WorkspaceProfiles
            .Select(item => new LayoutListItemViewModel
            {
                Id = item.Id,
                Name = item.Name,
                Description = item.Description
            })
            .ToList();
    }

    public string GetActiveSnapLayoutId()
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.Invoke(GetActiveSnapLayoutId);
        }

        return ViewModel.ActiveSnapLayoutId;
    }

    public string GetActiveWorkspaceProfileId()
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.Invoke(GetActiveWorkspaceProfileId);
        }

        return ViewModel.ActiveWorkspaceProfileId;
    }

    public void SwitchSnapLayoutFromTray(string layoutId)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SwitchSnapLayoutFromTray(layoutId));
            return;
        }

        SwitchSnapLayoutAndResetRuntimeState(layoutId);
    }

    public void SwitchWorkspaceProfileFromTray(string profileId)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SwitchWorkspaceProfileFromTray(profileId));
            return;
        }

        SwitchWorkspaceProfileAndResetRuntimeState(profileId, notifyOnSuccess: false);
    }

    public void PrepareForTrayRestore()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(PrepareForTrayRestore);
            return;
        }

        PaneWorksLog.Info("Prepare main window restore from tray");
        EnsureSnapOverlayHidden();
        RestoreWorkbenchFromSidebar();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedDisplayItem))
        {
            UpdateEditorStageBounds();
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.CurrentDocument))
        {
            UpdateEditorStageBounds();
        }
    }

    private void EditorCanvas_NodeSelected(object? sender, NodeSelectedEventArgs e)
    {
        ViewModel.SelectNode(e.NodeId);
    }

    private void EditorCanvas_SplitterRatioChanged(object? sender, SplitterRatioChangedEventArgs e)
    {
        ViewModel.UpdateSplitRatio(e.SplitNodeId, e.Ratio);
    }

    private void EditorCanvas_CanvasContextActionRequested(object? sender, CanvasContextActionRequestedEventArgs e)
    {
        ViewModel.HandleCanvasContextAction(e.Action, e.TargetNodeId);
    }

    private void EditorCanvas_SnapLayoutSwitchRequested(object? sender, SnapLayoutSwitchRequestedEventArgs e)
    {
        SwitchSnapLayoutAndResetRuntimeState(e.LayoutId);
    }

    private void EditorCanvas_WorkspaceProfileSwitchRequested(object? sender, WorkspaceProfileSwitchRequestedEventArgs e)
    {
        SwitchWorkspaceProfileAndResetRuntimeState(e.ProfileId, notifyOnSuccess: false);
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        var app = (App)WpfApplication.Current;

        if (!app.IsExitRequested)
        {
            e.Cancel = true;
            app.MinimizeMainWindowToTray();
            return;
        }

        if (!ViewModel.TryClose())
        {
            app.CancelExitRequest();
            e.Cancel = true;
            return;
        }

        if (_isSnapAssistArmed)
        {
            DisarmSnapAssist(restoreWindow: false);
        }

        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void MainWindow_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (!IsTextInputFocused() && MatchesMinimizeShortcut(e))
        {
            e.Handled = true;
            PaneWorksLog.Info("Tray shortcut pressed");
            MinimizeWorkbenchToTray();
            return;
        }

        if (HandleCommandShortcut(e))
        {
            e.Handled = true;
        }
    }

    private bool HandleCommandShortcut(WpfKeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            if (ViewModel.SaveLayoutCommand.CanExecute(null))
            {
                ViewModel.SaveLayoutCommand.Execute(null);
            }

            return true;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S)
        {
            if (ViewModel.SaveAsLayoutCommand.CanExecute(null))
            {
                ViewModel.SaveAsLayoutCommand.Execute(null);
            }

            return true;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            if (ViewModel.NewLayoutCommand.CanExecute(null))
            {
                ViewModel.NewLayoutCommand.Execute(null);
            }

            return true;
        }

        if (!IsTextInputFocused() && Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            if (ViewModel.UndoCommand.CanExecute(null))
            {
                ViewModel.UndoCommand.Execute(null);
            }

            return true;
        }

        if (!IsTextInputFocused() && Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y)
        {
            if (ViewModel.RedoCommand.CanExecute(null))
            {
                ViewModel.RedoCommand.Execute(null);
            }

            return true;
        }

        if (!IsTextInputFocused() && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z)
        {
            if (ViewModel.RedoCommand.CanExecute(null))
            {
                ViewModel.RedoCommand.Execute(null);
            }

            return true;
        }

        if (!IsTextInputFocused() && Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Delete)
        {
            if (ViewModel.DeleteSelectedSplitCommand.CanExecute(null))
            {
                ViewModel.DeleteSelectedSplitCommand.Execute(null);
            }

            return true;
        }

        return false;
    }

    private static bool IsTextInputFocused()
    {
        return Keyboard.FocusedElement is WpfTextBoxBase;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureMainWindowCoversVirtualDesktop();
        UpdateEditorStageBounds();
        UpdateWorkbenchPanelPosition();
        _windowMoveMonitor.MoveStarted += WindowMoveMonitor_MoveStarted;
        _windowMoveMonitor.MoveEnded += WindowMoveMonitor_MoveEnded;
        EnsureSnapAssistStarted();
        _snapAssistHealthTimer.Start();
        PaneWorksLog.Info("Main window loaded");
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized && _isSnapAssistArmed)
        {
            EnsureMainWindowCoversVirtualDesktop();
            UpdateWorkbenchPanelPosition();
            EnsureSnapOverlayHidden();
        }
    }

    private void EnsureSnapAssistStarted()
    {
        if (_isSnapAssistArmed)
        {
            return;
        }

        _windowMoveMonitor.Start();
        _isSnapAssistArmed = true;
        PaneWorksLog.Info("Snap assist started");
        EnsureMainWindowCoversVirtualDesktop();
        UpdateEditorStageBounds();
        UpdateWorkbenchPanelPosition();
        _snapAssistHealthTimer.Start();
        _snapAssistFallbackTimer.Start();
    }

    private void DisarmSnapAssist(bool restoreWindow)
    {
        _isSnapAssistArmed = false;
        _snapAssistHealthTimer.Stop();
        _snapAssistFallbackTimer.Stop();
        _windowMoveMonitor.Stop();
        PaneWorksLog.Info("Snap assist stopped");
        StopManualDetachedDragLoop();
        StopRuntimeLinkedResizeLoop();
        _snapAssistTimer.Stop();
        _movingWindowHandle = IntPtr.Zero;
        _hoveredSnapRegion = null;
        _hoveredSnapDisplayId = null;
        _movingWindowInitialBounds = null;
        _pendingDetachedRestoreBounds = null;
        _movingWindowStartedAt = null;
        _movingWindowMouseReleasedAt = null;
        _lastDetachedRestoreApplyAt = DateTimeOffset.MinValue;
        _manualDetachedDragLastBounds = null;
        _manualDetachedDragFrameAdjustment = default;
        _runtimeLinkedResizeSession = null;
        _detachedRestoreSettled = false;
        _manualDetachedDragActive = false;
        _runtimeLinkedResizeActive = false;
        _movingWindowWasSnapped = false;
        _movingWindowDetachedFromSnapGroup = false;
        _movingWindowDetachCandidate = false;
        _movingWindowSnapResizeGesture = false;
        _movingWindowStartedByForegroundFallback = false;
        EnsureSnapOverlayHidden();

        if (restoreWindow)
        {
            WindowState = WindowState.Normal;
            EnsureMainWindowCoversVirtualDesktop();
            Activate();
        }
    }

    private void SnapAssistHealthTimer_Tick(object? sender, EventArgs e)
    {
        if (_isSnapAssistArmed && !_windowMoveMonitor.IsRunning && !IsInternalWindowLayoutUpdateActive())
        {
            PaneWorksLog.Info("Snap assist hook restarted by health timer");
            _windowMoveMonitor.Start();
        }
    }

    private void SnapAssistFallbackTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (!_isSnapAssistArmed
                || _movingWindowHandle != IntPtr.Zero
                || IsInternalWindowLayoutUpdateActive()
                || GetActiveSnapAssistMode() == SnapAssistMode.None
                || !IsPrimaryMouseButtonPressed())
            {
                return;
            }

            if (!TryGetForegroundSnapCandidate(out var foregroundWindowHandle))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (_lastMoveStartEventHandle == foregroundWindowHandle
                && now - _lastMoveStartEventAt < TimeSpan.FromMilliseconds(250))
            {
                return;
            }

            _lastMoveStartEventHandle = foregroundWindowHandle;
            _lastMoveStartEventAt = now;
            BeginTrackingExternalWindowMove(foregroundWindowHandle, now, startedByForegroundFallback: true);
        }
        catch (Exception exception)
        {
            PaneWorksLog.Error("Snap assist foreground fallback failed", exception);
            ResetMovingWindowState();
            EnsureSnapOverlayHidden();
        }
    }

    private bool TryGetForegroundSnapCandidate(out IntPtr windowHandle)
    {
        windowHandle = GetForegroundWindow();
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        windowHandle = GetAncestor(windowHandle, GetAncestorRoot);
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(windowHandle, out var processId);
        if (processId == Environment.ProcessId)
        {
            return false;
        }

        if (!_workspaceApplyService.TryGetVisibleWindowBounds(windowHandle, out var bounds))
        {
            return false;
        }

        return bounds.Width >= 80 && bounds.Height >= 40;
    }

    private bool HasForegroundFallbackWindowMoved()
    {
        if (!_movingWindowStartedByForegroundFallback || _movingWindowInitialBounds is null)
        {
            return true;
        }

        return _workspaceApplyService.TryGetVisibleWindowBounds(_movingWindowHandle, out var currentBounds)
            && !AreBoundsClose(currentBounds, _movingWindowInitialBounds.Value, WindowBoundsTolerance);
    }

    private void WindowMoveMonitor_MoveStarted(object? sender, WindowMoveStateChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (!_isSnapAssistArmed || e.WindowHandle == IntPtr.Zero)
                {
                    return;
                }

                if (IsInternalWindowLayoutUpdateActive())
                {
                    PaneWorksLog.Info($"Ignore move start during internal layout update: 0x{e.WindowHandle.ToInt64():X}");
                    return;
                }

                var now = DateTimeOffset.UtcNow;
                if (_lastMoveStartEventHandle == e.WindowHandle
                    && now - _lastMoveStartEventAt < TimeSpan.FromMilliseconds(250))
                {
                    PaneWorksLog.Info($"Duplicate move start ignored: 0x{e.WindowHandle.ToInt64():X}");
                    return;
                }

                _lastMoveStartEventHandle = e.WindowHandle;
                _lastMoveStartEventAt = now;

                if (_movingWindowHandle != IntPtr.Zero)
                {
                    if (_movingWindowHandle == e.WindowHandle
                        && (_snapAssistTimer.IsEnabled || _manualDetachedDragActive))
                    {
                        PaneWorksLog.Info($"Active move start ignored: 0x{e.WindowHandle.ToInt64():X}");
                        return;
                    }

                    PaneWorksLog.Info($"Move state reset before new move: 0x{_movingWindowHandle.ToInt64():X}");
                    ResetMovingWindowState();
                }

                BeginTrackingExternalWindowMove(e.WindowHandle, now, startedByForegroundFallback: false);
            }
            catch (Exception exception)
            {
                PaneWorksLog.Error("Move started handler failed", exception);
                ResetMovingWindowState();
                EnsureSnapOverlayHidden();
            }
        });
    }

    private void BeginTrackingExternalWindowMove(
        IntPtr windowHandle,
        DateTimeOffset startedAt,
        bool startedByForegroundFallback)
    {
        _movingWindowHandle = windowHandle;
        _movingWindowStartedAt = startedAt;
        _movingWindowStartedByForegroundFallback = startedByForegroundFallback;
        PaneWorksLog.Info($"{(startedByForegroundFallback ? "Foreground fallback move started" : "Move started")}: 0x{_movingWindowHandle.ToInt64():X}");
        _hoveredSnapRegion = null;
        _hoveredSnapDisplayId = null;
        _movingWindowInitialBounds = _workspaceApplyService.TryGetVisibleWindowBounds(_movingWindowHandle, out var initialBounds)
            ? initialBounds
            : null;
        CaptureMovingWindowDragAnchor(_movingWindowInitialBounds);
        _movingWindowWasSnapped = _snapBindings.ContainsKey(_movingWindowHandle);
        _movingWindowDetachedFromSnapGroup = false;
        _movingWindowSnapResizeGesture = _movingWindowWasSnapped && IsResizeGesture(_movingWindowInitialBounds);
        _movingWindowDetachCandidate = _movingWindowWasSnapped && !_movingWindowSnapResizeGesture;

        if (_movingWindowDetachCandidate)
        {
            DetachMovingWindowFromSnapGroup();
        }

        if (_movingWindowSnapResizeGesture)
        {
            if (EnableRuntimeLinkedResize)
            {
                StartRuntimeLinkedResizeLoop(_movingWindowHandle);
            }
        }
        else
        {
            _snapAssistTimer.Start();
        }
    }

    private void WindowMoveMonitor_MoveEnded(object? sender, WindowMoveStateChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (_movingWindowHandle == IntPtr.Zero || e.WindowHandle != _movingWindowHandle)
                {
                    return;
                }

                if (IsInternalWindowLayoutUpdateActive())
                {
                    PaneWorksLog.Info($"Ignore move end during internal layout update: 0x{e.WindowHandle.ToInt64():X}");
                    return;
                }

                PaneWorksLog.Info($"Move ended: 0x{_movingWindowHandle.ToInt64():X}");

                if (_manualDetachedDragActive && _movingWindowDetachedFromSnapGroup)
                {
                    PaneWorksLog.Info($"Ignore system move end during manual detached drag: 0x{_movingWindowHandle.ToInt64():X}");
                    return;
                }

                if (_runtimeLinkedResizeActive && _movingWindowSnapResizeGesture)
                {
                    PaneWorksLog.Info($"Ignore system move end during runtime linked resize: 0x{_movingWindowHandle.ToInt64():X}");
                    return;
                }

                _snapAssistTimer.Stop();
                FinalizeMovingWindowAfterRelease("system");

                ResetMovingWindowState();
                EnsureSnapOverlayHidden();
            }
            catch (Exception exception)
            {
                PaneWorksLog.Error("Move ended handler failed", exception);
                ResetMovingWindowState();
                EnsureSnapOverlayHidden();
            }
        });
    }

    private void FinalizeMovingWindowAfterRelease(string reason)
    {
        if (_movingWindowHandle == IntPtr.Zero)
        {
            return;
        }

        if (_movingWindowDetachedFromSnapGroup)
        {
            RestoreDetachedWindowAfterMove();
        }
        else if (_hoveredSnapRegion is not null && !string.IsNullOrWhiteSpace(_hoveredSnapDisplayId))
        {
            var restoreBounds = ResolveRestoreBoundsForSnap(_movingWindowHandle);
            PaneWorksLog.Info($"Snap window ({reason}): 0x{_movingWindowHandle.ToInt64():X}, restore={restoreBounds.Width:0}x{restoreBounds.Height:0}");
            _snapBindings[_movingWindowHandle] = new SnapBindingState(_hoveredSnapRegion.NodeId, _hoveredSnapDisplayId, restoreBounds);
            _snapRuntimeBounds[_movingWindowHandle] = _hoveredSnapRegion.Bounds;
            TrySnapWindowToBoundsWithStatus(_movingWindowHandle, _hoveredSnapRegion.Bounds, reason);
            QueueSnappedWindowInfoCache(_movingWindowHandle);
        }
        else if (_movingWindowSnapResizeGesture)
        {
            PaneWorksLog.Info($"Finalize snapped runtime resize without linked session ({reason}): 0x{_movingWindowHandle.ToInt64():X}");
            CaptureCurrentRuntimeBoundsForDisplay(null);
            TryUpdateSessionSnapLayoutFromWindowBounds(_movingWindowHandle);
        }
        else if (!_movingWindowDetachedFromSnapGroup && _snapBindings.ContainsKey(_movingWindowHandle))
        {
            PaneWorksLog.Info($"Finalize snapped resize into session layout ({reason}): 0x{_movingWindowHandle.ToInt64():X}");
            CaptureCurrentRuntimeBoundsForDisplay(null);
            TryUpdateSessionSnapLayoutFromWindowBounds(_movingWindowHandle);
        }
    }

    private bool TrySnapWindowToBoundsWithStatus(IntPtr windowHandle, PaneRect bounds, string reason)
    {
        if (_workspaceApplyService.TrySnapWindowToBounds(windowHandle, bounds, out var errorCode))
        {
            return true;
        }

        PaneWorksLog.Info($"Snap window failed ({reason}): 0x{windowHandle.ToInt64():X}, error={errorCode}");
        ViewModel.SetUserStatusMessage(errorCode == Win32ErrorAccessDenied
            ? "这个窗口权限高于 PaneWorks。请用管理员身份启动 PaneWorks 后再吸附任务管理器或管理员软件。"
            : $"窗口吸附被系统拒绝，错误码：{errorCode}");
        return false;
    }

    private void SnapAssistTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (!_isSnapAssistArmed || _movingWindowHandle == IntPtr.Zero)
            {
                return;
            }

            if (_manualDetachedDragActive && _movingWindowDetachedFromSnapGroup)
            {
                _hoveredSnapRegion = null;
                _hoveredSnapDisplayId = null;
                EnsureSnapOverlayHidden();
                return;
            }

            if (RecoverStaleMoveStateIfNeeded())
            {
                return;
            }

            if (_movingWindowDetachedFromSnapGroup)
            {
                _hoveredSnapRegion = null;
                _hoveredSnapDisplayId = null;
                EnsureSnapOverlayHidden();
                return;
            }

            if (IsSnapTemporarilySuppressed(_movingWindowHandle))
            {
                _hoveredSnapRegion = null;
                _hoveredSnapDisplayId = null;
                EnsureSnapOverlayHidden();
                return;
            }

            if (_movingWindowStartedByForegroundFallback && !HasForegroundFallbackWindowMoved())
            {
                _hoveredSnapRegion = null;
                _hoveredSnapDisplayId = null;
                EnsureSnapOverlayHidden();
                return;
            }

                var snapAssistMode = GetActiveSnapAssistMode();
                if (snapAssistMode == SnapAssistMode.None)
                {
                    if (!_movingWindowDetachedFromSnapGroup && _snapBindings.ContainsKey(_movingWindowHandle))
                    {
                        EnsureSnapOverlayHidden();
                        _movingWindowDetachCandidate = false;
                        return;
                    }

                _hoveredSnapRegion = null;
                _hoveredSnapDisplayId = null;
                EnsureSnapOverlayHidden();
                return;
            }
        }
            catch (Exception exception)
            {
                PaneWorksLog.Error("Snap assist timer failed", exception);
                ResetMovingWindowState();
                EnsureSnapOverlayHidden();
                return;
            }

        try
        {
            if (!TryGetCursorPosition(out var cursorInDevicePixels))
            {
                return;
            }

            var activeDisplay = _displayDiscoveryService.GetDisplayFromPoint((int)cursorInDevicePixels.X, (int)cursorInDevicePixels.Y);
            var snapAssistMode = GetActiveSnapAssistMode();
            if (snapAssistMode == SnapAssistMode.None)
            {
                _hoveredSnapRegion = null;
                _hoveredSnapDisplayId = null;
                EnsureSnapOverlayHidden();
                return;
            }

            var snapDocument = GetSnapAssistLayoutDocumentForDisplay(activeDisplay.Id, snapAssistMode);
            var targetStageBounds = GetSnapTargetStageBounds(activeDisplay);
            var targetGeometry = _geometryCalculator.Compute(
                snapDocument,
                targetStageBounds,
                SnapTargetSplitterThickness);

            _hoveredSnapRegion = targetGeometry.Regions.FirstOrDefault(region => Contains(region.Bounds, cursorInDevicePixels));
            _hoveredSnapDisplayId = activeDisplay.Id;

            EnsureSnapOverlaysVisible(activeDisplay.Id, _hoveredSnapRegion?.NodeId, snapAssistMode);
        }
        catch (Exception exception)
        {
            PaneWorksLog.Error("Snap assist preview failed", exception);
            ResetMovingWindowState();
            EnsureSnapOverlayHidden();
        }
    }

    private void EnsureSnapOverlaysVisible(string activeDisplayId, string? activePreviewNodeId, SnapAssistMode snapAssistMode)
    {
        var liveDisplayIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var display in ViewModel.GetDisplays())
        {
            liveDisplayIds.Add(display.Id);

            if (!_snapOverlayWindows.TryGetValue(display.Id, out var overlayWindow))
            {
                overlayWindow = new SnapOverlayWindow();
                _snapOverlayWindows[display.Id] = overlayWindow;
            }

            var displayDipBounds = DeviceRectToDipRect(display.Bounds);
            overlayWindow.Document = GetSnapAssistLayoutDocumentForDisplay(display.Id, snapAssistMode);
            overlayWindow.PreviewNodeId = string.Equals(display.Id, activeDisplayId, StringComparison.OrdinalIgnoreCase)
                ? activePreviewNodeId
                : null;
            overlayWindow.StageBounds = GetSnapVisualStageBounds(displayDipBounds);
            overlayWindow.Left = displayDipBounds.X;
            overlayWindow.Top = displayDipBounds.Y;
            overlayWindow.Width = displayDipBounds.Width;
            overlayWindow.Height = displayDipBounds.Height;

            if (!overlayWindow.IsVisible)
            {
                overlayWindow.Show();
            }
        }

        var staleDisplayIds = _snapOverlayWindows.Keys
            .Where(displayId => !liveDisplayIds.Contains(displayId))
            .ToList();

        foreach (var staleDisplayId in staleDisplayIds)
        {
            _snapOverlayWindows[staleDisplayId].Hide();
            _snapOverlayWindows.Remove(staleDisplayId);
        }
    }

    private void EnsureSnapOverlayHidden()
    {
        foreach (var overlayWindow in _snapOverlayWindows.Values)
        {
            overlayWindow.PreviewNodeId = null;
            if (overlayWindow.IsVisible)
            {
                overlayWindow.Hide();
            }
        }
    }

    private void EnsureMainWindowCoversVirtualDesktop()
    {
        if (WindowState == WindowState.Minimized)
        {
            return;
        }

        _virtualDesktopBounds = DeviceRectToDipRect(_displayDiscoveryService.GetVirtualDesktopBounds());

        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
        }

        Left = _virtualDesktopBounds.X;
        Top = _virtualDesktopBounds.Y;
        Width = Math.Max(MinWidth, _virtualDesktopBounds.Width);
        Height = Math.Max(MinHeight, _virtualDesktopBounds.Height);
    }

    private void UpdateEditorStageBounds()
    {
        EnsureMainWindowCoversVirtualDesktop();

        var selectedDisplay = ViewModel.GetSelectedDisplay();
        var selectedDisplayDipBounds = DeviceRectToDipRect(selectedDisplay.Bounds);
        EditorCanvas.StageBounds = new PaneRect(
            selectedDisplayDipBounds.X - _virtualDesktopBounds.X,
            selectedDisplayDipBounds.Y - _virtualDesktopBounds.Y,
            selectedDisplayDipBounds.Width,
            selectedDisplayDipBounds.Height);
        EditorCanvas.ReferenceLayouts = ViewModel.GetDisplays()
            .Where(display => !string.Equals(display.Id, selectedDisplay.Id, StringComparison.OrdinalIgnoreCase))
            .Select(display =>
            {
                var displayDipBounds = DeviceRectToDipRect(display.Bounds);
                return new EditorReferenceLayout(
                    display.Id,
                    ViewModel.GetCurrentLayoutDocumentForDisplay(display.Id),
                    new PaneRect(
                        displayDipBounds.X - _virtualDesktopBounds.X,
                        displayDipBounds.Y - _virtualDesktopBounds.Y,
                        displayDipBounds.Width,
                        displayDipBounds.Height));
            })
            .ToList();
        EditorCanvas.InvalidateVisual();
        UpdateWorkbenchPanelPosition();
    }

    private void UpdateWorkbenchPanelPosition()
    {
        if (!IsLoaded)
        {
            return;
        }

        var primaryDisplay = _displayDiscoveryService.GetPrimaryDisplay();
        var primaryDisplayDipBounds = DeviceRectToDipRect(primaryDisplay.Bounds);
        WorkbenchPanel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));

        var panelWidth = WorkbenchPanel.Width > 0
            ? WorkbenchPanel.Width
            : WorkbenchPanel.DesiredSize.Width;
        var panelHeight = WorkbenchPanel.ActualHeight > 0
            ? WorkbenchPanel.ActualHeight
            : WorkbenchPanel.DesiredSize.Height;

        var preferredLeft = (primaryDisplayDipBounds.X - _virtualDesktopBounds.X)
            + Math.Max(0, (primaryDisplayDipBounds.Width - panelWidth) / 2);
        var leftMin = primaryDisplayDipBounds.X - _virtualDesktopBounds.X + 32;
        var leftMax = primaryDisplayDipBounds.X - _virtualDesktopBounds.X + Math.Max(32, primaryDisplayDipBounds.Width - panelWidth - 32);
        var left = Math.Clamp(preferredLeft, leftMin, leftMax);
        var top = (primaryDisplayDipBounds.Y - _virtualDesktopBounds.Y)
            + Math.Max(24, (primaryDisplayDipBounds.Height - panelHeight) / 2);

        WorkbenchPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        WorkbenchPanel.VerticalAlignment = System.Windows.VerticalAlignment.Top;
        WorkbenchPanel.Margin = new Thickness(left, top, 0, 0);
    }

    private void WorkbenchDragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isWorkbenchPanelDragging = true;
        _workbenchPanelDragStartPoint = e.GetPosition(this);
        _workbenchPanelDragStartOffsetX = WorkbenchPanelTranslate.X;
        _workbenchPanelDragStartOffsetY = WorkbenchPanelTranslate.Y;

        if (sender is UIElement element)
        {
            element.CaptureMouse();
        }

        e.Handled = true;
    }

    private void WorkbenchDragHandle_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_isWorkbenchPanelDragging)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndWorkbenchPanelDrag(sender);
            return;
        }

        var currentPoint = e.GetPosition(this);
        SetWorkbenchPanelDragOffset(
            _workbenchPanelDragStartOffsetX + currentPoint.X - _workbenchPanelDragStartPoint.X,
            _workbenchPanelDragStartOffsetY + currentPoint.Y - _workbenchPanelDragStartPoint.Y);
        e.Handled = true;
    }

    private void WorkbenchDragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndWorkbenchPanelDrag(sender);
        e.Handled = true;
    }

    private void EndWorkbenchPanelDrag(object? sender)
    {
        if (!_isWorkbenchPanelDragging)
        {
            return;
        }

        _isWorkbenchPanelDragging = false;
        if (sender is UIElement { IsMouseCaptured: true } element)
        {
            element.ReleaseMouseCapture();
        }
    }

    private void SetWorkbenchPanelDragOffset(double offsetX, double offsetY)
    {
        var panelWidth = WorkbenchPanel.ActualWidth > 0
            ? WorkbenchPanel.ActualWidth
            : WorkbenchPanel.DesiredSize.Width;
        var panelHeight = WorkbenchPanel.ActualHeight > 0
            ? WorkbenchPanel.ActualHeight
            : WorkbenchPanel.DesiredSize.Height;

        if (ActualWidth > 0 && panelWidth > 0)
        {
            var minOffsetX = 12 - WorkbenchPanel.Margin.Left;
            var maxOffsetX = Math.Max(
                minOffsetX,
                ActualWidth - WorkbenchPanel.Margin.Left - panelWidth - 12);
            offsetX = Math.Clamp(offsetX, minOffsetX, maxOffsetX);
        }

        if (ActualHeight > 0 && panelHeight > 0)
        {
            var minOffsetY = 12 - WorkbenchPanel.Margin.Top;
            var maxOffsetY = Math.Max(
                minOffsetY,
                ActualHeight - WorkbenchPanel.Margin.Top - panelHeight - 12);
            offsetY = Math.Clamp(offsetY, minOffsetY, maxOffsetY);
        }

        WorkbenchPanelTranslate.X = offsetX;
        WorkbenchPanelTranslate.Y = offsetY;
    }

    private static PaneRect GetSnapVisualStageBounds(PaneRect displayBounds)
    {
        return new PaneRect(
            SnapOverlayInset,
            SnapOverlayInset,
            Math.Max(0, displayBounds.Width - (SnapOverlayInset * 2)),
            Math.Max(0, displayBounds.Height - (SnapOverlayInset * 2)));
    }

    private static PaneRect GetSnapTargetStageBounds(DisplayInfo display)
    {
        return display.Bounds;
    }

    private static bool Contains(PaneRect rect, WpfPoint point)
    {
        return point.X >= rect.X
            && point.X <= rect.X + rect.Width
            && point.Y >= rect.Y
            && point.Y <= rect.Y + rect.Height;
    }

    private SnapAssistMode GetActiveSnapAssistMode()
    {
        if (ShortcutGestureHelper.IsPressed(ViewModel.RuntimeSessionModifierKey))
        {
            return SnapAssistMode.RuntimeSession;
        }

        return ShortcutGestureHelper.IsPressed(ViewModel.SnapModifierKey)
            ? SnapAssistMode.SavedLayout
            : SnapAssistMode.None;
    }

    private LayoutDocument GetSnapAssistLayoutDocumentForDisplay(string displayId, SnapAssistMode snapAssistMode)
    {
        return snapAssistMode == SnapAssistMode.RuntimeSession
            ? GetRuntimeSessionLayoutDocumentForDisplay(displayId)
            : ViewModel.GetSnapLayoutDocumentForDisplay(displayId);
    }

    private LayoutDocument GetRuntimeSessionLayoutDocumentForDisplay(string displayId)
    {
        return _sessionSnapLayoutDocuments.TryGetValue(displayId, out var document)
            ? document
            : ViewModel.GetSnapLayoutDocumentForDisplay(displayId);
    }

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

    private void DetachMovingWindowFromSnapGroup()
    {
        if (_movingWindowHandle == IntPtr.Zero || _movingWindowDetachedFromSnapGroup)
        {
            return;
        }

        _movingWindowDetachedFromSnapGroup = true;
        _movingWindowDetachCandidate = false;
        _detachedRestoreSettled = false;
        _lastDetachedRestoreApplyAt = DateTimeOffset.MinValue;
        _snapSuppressUntilByWindow[_movingWindowHandle] = DateTimeOffset.UtcNow.AddMilliseconds(700);
        var hasBinding = _snapBindings.TryGetValue(_movingWindowHandle, out var binding);
        _snapBindings.Remove(_movingWindowHandle);
        _snapRuntimeBounds.Remove(_movingWindowHandle);
        _snapWindowInfoCache.Remove(_movingWindowHandle);
        if (hasBinding)
        {
            PaneWorksLog.Info($"Detach snapped window and restore: 0x{_movingWindowHandle.ToInt64():X}");
            var restoreBounds = GetDetachedRestoreBounds(_movingWindowHandle, binding!.RestoreBounds);
            _pendingDetachedRestoreBounds = restoreBounds;
            _manualDetachedDragActive = true;
            _manualDetachedDragLastBounds = null;
            _workspaceApplyService.CancelWindowDrag(_movingWindowHandle);
            _workspaceApplyService.RestoreWindowToBounds(_movingWindowHandle, restoreBounds);
            _manualDetachedDragFrameAdjustment = _workspaceApplyService.GetWindowFrameAdjustment(_movingWindowHandle);
            StartManualDetachedDragLoop(_movingWindowHandle, restoreBounds, _manualDetachedDragFrameAdjustment);
        }
        else
        {
            _workspaceApplyService.RestoreRoundedCorners(_movingWindowHandle);
        }
        ReapplySnapBindings();
    }

    private void RestoreDetachedWindowAfterMove()
    {
        if (_movingWindowHandle == IntPtr.Zero || _pendingDetachedRestoreBounds is null)
        {
            return;
        }

        var restoreBounds = GetDetachedRestoreBounds(_movingWindowHandle, _pendingDetachedRestoreBounds.Value);
        PaneWorksLog.Info($"Finalize detached restore: 0x{_movingWindowHandle.ToInt64():X}, restore={restoreBounds.Width:0}x{restoreBounds.Height:0}");
        ScheduleDetachedWindowRestore(_movingWindowHandle, restoreBounds);
    }

    private void MaintainDetachedWindowRestoreDuringMove()
    {
        if (_movingWindowHandle == IntPtr.Zero || _pendingDetachedRestoreBounds is null || _detachedRestoreSettled)
        {
            return;
        }

        var restoreBounds = GetDetachedRestoreBounds(_movingWindowHandle, _pendingDetachedRestoreBounds.Value);
        if (_workspaceApplyService.TryGetVisibleWindowBounds(_movingWindowHandle, out var currentBounds)
            && IsSizeClose(currentBounds, restoreBounds, WindowBoundsTolerance))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - _lastDetachedRestoreApplyAt < DetachedRestoreApplyInterval)
        {
            return;
        }

        _lastDetachedRestoreApplyAt = now;
        _workspaceApplyService.RestoreWindowToBounds(_movingWindowHandle, restoreBounds);
    }

    private void UpdateManualDetachedDrag()
    {
        if (_movingWindowHandle == IntPtr.Zero || _pendingDetachedRestoreBounds is null)
        {
            FinishManualDetachedDrag();
            return;
        }

        if (!IsPrimaryMouseButtonPressed())
        {
            FinishManualDetachedDrag();
            return;
        }

        var targetBounds = GetDetachedRestoreBounds(_movingWindowHandle, _pendingDetachedRestoreBounds.Value);
        if (_manualDetachedDragLastBounds is not null
            && AreBoundsClose(_manualDetachedDragLastBounds.Value, targetBounds, 1))
        {
            return;
        }

        _manualDetachedDragLastBounds = targetBounds;
        _workspaceApplyService.MoveWindowToBounds(_movingWindowHandle, targetBounds, _manualDetachedDragFrameAdjustment);
    }

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

        PaneWorksLog.Info($"Runtime linked resize started: 0x{windowHandle.ToInt64():X}, edge={session.Edge}, neighbors={session.Neighbors.Count}");
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
                    var edgePosition = ClampRuntimeLinkedEdgePosition(session, rawEdgePosition);
                    var isEdgeClamped = Math.Abs(edgePosition - rawEdgePosition) >= 0.5;

                    if (double.IsNaN(lastEdgePosition) || Math.Abs(edgePosition - lastEdgePosition) >= 1 || isEdgeClamped)
                    {
                        var updates = BuildRuntimeLinkedResizeUpdates(session, edgePosition, includeSourceWindow: isEdgeClamped);
                        if (updates.Count > 0)
                        {
                            var stopAtLockedEdge = isEdgeClamped;
                            _suppressMoveEventsUntil = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(stopAtLockedEdge ? 350 : 120);
                            if (stopAtLockedEdge)
                            {
                                _workspaceApplyService.CancelWindowDrag(session.SourceWindowHandle);
                            }

                            _workspaceApplyService.MoveSnappedWindowsToBounds(updates);
                            moved = true;

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
                    FinishRuntimeLinkedResizeGesture(session);
                }
            });
        }
    }

    private bool TryCreateRuntimeLinkedResizeSession(
        IntPtr windowHandle,
        out RuntimeLinkedResizeSession session)
    {
        session = default!;
        if (!_snapBindings.TryGetValue(windowHandle, out var binding)
            || _movingWindowInitialBounds is null
            || !TryGetCursorPosition(out var cursor))
        {
            return false;
        }

        var edge = GetResizeEdge(_movingWindowInitialBounds.Value, cursor);
        if (edge is null)
        {
            return false;
        }

        var sourceBounds = _movingWindowInitialBounds.Value;
        var neighbors = _snapBindings
            .Where(item => item.Key != windowHandle
                && string.Equals(item.Value.DisplayId, binding.DisplayId, StringComparison.OrdinalIgnoreCase))
            .Select(item =>
            {
                if (!_workspaceApplyService.TryGetVisibleWindowBounds(item.Key, out var bounds))
                {
                    return null;
                }

                return TryGetRuntimeResizeNeighborSide(sourceBounds, bounds, edge.Value, out var side)
                    ? new RuntimeLinkedResizeNeighbor(
                        item.Key,
                        bounds,
                        side,
                        _workspaceApplyService.GetWindowMinimumVisibleSize(item.Key),
                        _workspaceApplyService.GetWindowFrameAdjustment(item.Key))
                    : null;
            })
            .Where(item => item is not null)
            .Cast<RuntimeLinkedResizeNeighbor>()
            .ToList();

        if (neighbors.Count == 0)
        {
            return false;
        }

        var minEdgePosition = double.NegativeInfinity;
        var maxEdgePosition = double.PositiveInfinity;
        var sourceMinimumSize = _workspaceApplyService.GetWindowMinimumVisibleSize(windowHandle);
        AddRuntimeLinkedEdgeConstraints(sourceBounds, sourceMinimumSize, edge.Value, ref minEdgePosition, ref maxEdgePosition);
        foreach (var neighbor in neighbors)
        {
            AddRuntimeLinkedEdgeConstraints(
                neighbor.InitialBounds,
                neighbor.MinimumSize,
                GetRuntimeLinkedResizeEdge(edge.Value, neighbor.Side),
                ref minEdgePosition,
                ref maxEdgePosition);
        }

        var initialEdgePosition = GetEdgePosition(sourceBounds, edge.Value);
        if (minEdgePosition > maxEdgePosition)
        {
            minEdgePosition = initialEdgePosition;
            maxEdgePosition = initialEdgePosition;
        }

        session = new RuntimeLinkedResizeSession(
            windowHandle,
            binding.DisplayId,
            sourceBounds,
            sourceMinimumSize,
            _workspaceApplyService.GetWindowFrameAdjustment(windowHandle),
            edge.Value,
            minEdgePosition,
            maxEdgePosition,
            neighbors);
        return true;
    }

    private List<WindowBoundsUpdate> BuildRuntimeLinkedResizeUpdates(
        RuntimeLinkedResizeSession session,
        double edgePosition,
        bool includeSourceWindow)
    {
        var updates = new List<WindowBoundsUpdate>(session.Neighbors.Count + (includeSourceWindow ? 1 : 0));
        if (includeSourceWindow)
        {
            updates.Add(new WindowBoundsUpdate(
                session.SourceWindowHandle,
                GetBoundsForRuntimeLinkedResizeEdge(session.SourceInitialBounds, session.SourceMinimumSize, session.Edge, edgePosition),
                session.SourceFrameAdjustment));
        }

        foreach (var neighbor in session.Neighbors)
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

    private static bool TryGetRuntimeResizeNeighborSide(
        PaneRect sourceBounds,
        PaneRect candidateBounds,
        ResizeEdge edge,
        out RuntimeLinkedResizeSide side)
    {
        side = default;
        var edgePosition = GetEdgePosition(sourceBounds, edge);

        if (IsSameSideRuntimeResizeEdge(candidateBounds, edge, edgePosition))
        {
            side = RuntimeLinkedResizeSide.SameSide;
            return true;
        }

        if (IsOppositeSideRuntimeResizeEdge(candidateBounds, edge, edgePosition))
        {
            side = RuntimeLinkedResizeSide.OppositeSide;
            return true;
        }

        return false;
    }

    private static bool IsSameSideRuntimeResizeEdge(PaneRect bounds, ResizeEdge edge, double edgePosition)
    {
        return edge switch
        {
            ResizeEdge.Left => AreEdgesClose(bounds.X, edgePosition),
            ResizeEdge.Right => AreEdgesClose(GetRight(bounds), edgePosition),
            ResizeEdge.Top => AreEdgesClose(bounds.Y, edgePosition),
            _ => AreEdgesClose(GetBottom(bounds), edgePosition)
        };
    }

    private static bool IsOppositeSideRuntimeResizeEdge(PaneRect bounds, ResizeEdge edge, double edgePosition)
    {
        return edge switch
        {
            ResizeEdge.Left => AreEdgesClose(GetRight(bounds), edgePosition),
            ResizeEdge.Right => AreEdgesClose(bounds.X, edgePosition),
            ResizeEdge.Top => AreEdgesClose(GetBottom(bounds), edgePosition),
            _ => AreEdgesClose(bounds.Y, edgePosition)
        };
    }

    private static PaneRect GetRuntimeLinkedNeighborBounds(
        PaneRect initialBounds,
        WindowMinimumSize minimumSize,
        ResizeEdge edge,
        RuntimeLinkedResizeSide side,
        double edgePosition)
    {
        return GetBoundsForRuntimeLinkedResizeEdge(
            initialBounds,
            minimumSize,
            GetRuntimeLinkedResizeEdge(edge, side),
            edgePosition);
    }

    private static ResizeEdge GetRuntimeLinkedResizeEdge(ResizeEdge edge, RuntimeLinkedResizeSide side)
    {
        if (side == RuntimeLinkedResizeSide.SameSide)
        {
            return edge;
        }

        return edge switch
        {
            ResizeEdge.Left => ResizeEdge.Right,
            ResizeEdge.Right => ResizeEdge.Left,
            ResizeEdge.Top => ResizeEdge.Bottom,
            _ => ResizeEdge.Top
        };
    }

    private static PaneRect GetBoundsForRuntimeLinkedResizeEdge(
        PaneRect initialBounds,
        WindowMinimumSize minimumSize,
        ResizeEdge edge,
        double edgePosition)
    {
        var right = GetRight(initialBounds);
        var bottom = GetBottom(initialBounds);
        return edge switch
        {
            ResizeEdge.Left => GetLeftEdgeBounds(initialBounds, minimumSize, right, edgePosition),
            ResizeEdge.Right => GetRightEdgeBounds(initialBounds, minimumSize, edgePosition),
            ResizeEdge.Top => GetTopEdgeBounds(initialBounds, minimumSize, bottom, edgePosition),
            _ => GetBottomEdgeBounds(initialBounds, minimumSize, edgePosition)
        };
    }

    private static PaneRect GetLeftEdgeBounds(
        PaneRect initialBounds,
        WindowMinimumSize minimumSize,
        double right,
        double edgePosition)
    {
        var width = Math.Max(GetRuntimeLinkedMinWidth(initialBounds, minimumSize), right - edgePosition);
        return new PaneRect(right - width, initialBounds.Y, width, initialBounds.Height);
    }

    private static PaneRect GetRightEdgeBounds(
        PaneRect initialBounds,
        WindowMinimumSize minimumSize,
        double edgePosition)
    {
        return new PaneRect(
            initialBounds.X,
            initialBounds.Y,
            Math.Max(GetRuntimeLinkedMinWidth(initialBounds, minimumSize), edgePosition - initialBounds.X),
            initialBounds.Height);
    }

    private static PaneRect GetTopEdgeBounds(
        PaneRect initialBounds,
        WindowMinimumSize minimumSize,
        double bottom,
        double edgePosition)
    {
        var height = Math.Max(GetRuntimeLinkedMinHeight(initialBounds, minimumSize), bottom - edgePosition);
        return new PaneRect(initialBounds.X, bottom - height, initialBounds.Width, height);
    }

    private static PaneRect GetBottomEdgeBounds(
        PaneRect initialBounds,
        WindowMinimumSize minimumSize,
        double edgePosition)
    {
        return new PaneRect(
            initialBounds.X,
            initialBounds.Y,
            initialBounds.Width,
            Math.Max(GetRuntimeLinkedMinHeight(initialBounds, minimumSize), edgePosition - initialBounds.Y));
    }

    private static void AddRuntimeLinkedEdgeConstraints(
        PaneRect bounds,
        WindowMinimumSize minimumSize,
        ResizeEdge edge,
        ref double minEdgePosition,
        ref double maxEdgePosition)
    {
        switch (edge)
        {
            case ResizeEdge.Left:
                maxEdgePosition = Math.Min(maxEdgePosition, GetRight(bounds) - GetRuntimeLinkedMinWidth(bounds, minimumSize));
                break;
            case ResizeEdge.Right:
                minEdgePosition = Math.Max(minEdgePosition, bounds.X + GetRuntimeLinkedMinWidth(bounds, minimumSize));
                break;
            case ResizeEdge.Top:
                maxEdgePosition = Math.Min(maxEdgePosition, GetBottom(bounds) - GetRuntimeLinkedMinHeight(bounds, minimumSize));
                break;
            case ResizeEdge.Bottom:
                minEdgePosition = Math.Max(minEdgePosition, bounds.Y + GetRuntimeLinkedMinHeight(bounds, minimumSize));
                break;
        }
    }

    private static double ClampRuntimeLinkedEdgePosition(
        RuntimeLinkedResizeSession session,
        double edgePosition)
    {
        if (session.MinEdgePosition > session.MaxEdgePosition)
        {
            return Math.Abs(edgePosition - session.MinEdgePosition) < Math.Abs(edgePosition - session.MaxEdgePosition)
                ? session.MinEdgePosition
                : session.MaxEdgePosition;
        }

        return Math.Clamp(edgePosition, session.MinEdgePosition, session.MaxEdgePosition);
    }

    private static double GetRuntimeLinkedMinWidth(PaneRect bounds, WindowMinimumSize minimumSize)
    {
        return Math.Min(Math.Max(RuntimeLinkedResizeMinWidth, minimumSize.Width), Math.Max(1, bounds.Width));
    }

    private static double GetRuntimeLinkedMinHeight(PaneRect bounds, WindowMinimumSize minimumSize)
    {
        return Math.Min(Math.Max(RuntimeLinkedResizeMinHeight, minimumSize.Height), Math.Max(1, bounds.Height));
    }

    private static double GetEdgePosition(PaneRect bounds, ResizeEdge edge)
    {
        return edge switch
        {
            ResizeEdge.Left => bounds.X,
            ResizeEdge.Right => GetRight(bounds),
            ResizeEdge.Top => bounds.Y,
            _ => GetBottom(bounds)
        };
    }

    private static bool AreEdgesClose(double first, double second)
    {
        return Math.Abs(first - second) <= RuntimeLinkedResizeTolerance;
    }

    private static double GetRight(PaneRect bounds)
    {
        return bounds.X + bounds.Width;
    }

    private static double GetBottom(PaneRect bounds)
    {
        return bounds.Y + bounds.Height;
    }

    private void StartManualDetachedDragLoop(
        IntPtr windowHandle,
        PaneRect restoreBounds,
        WindowFrameAdjustment frameAdjustment)
    {
        StopManualDetachedDragLoop();
        _manualDetachedDragCancellation = new CancellationTokenSource();
        var cancellationToken = _manualDetachedDragCancellation.Token;
        var dragGeneration = Interlocked.Increment(ref _manualDetachedDragGeneration);
        var anchorRatioX = _movingWindowDragAnchorRatioX;
        var anchorOffsetY = _movingWindowDragAnchorOffsetY;

        _ = Task.Factory.StartNew(
            () => RunManualDetachedDragLoop(
                windowHandle,
                restoreBounds,
                frameAdjustment,
                dragGeneration,
                anchorRatioX,
                anchorOffsetY,
                cancellationToken),
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private void StopManualDetachedDragLoop()
    {
        if (_manualDetachedDragCancellation is null)
        {
            return;
        }

        _manualDetachedDragCancellation.Cancel();
        _manualDetachedDragCancellation = null;
        _ = Interlocked.Increment(ref _manualDetachedDragGeneration);
    }

    private void RunManualDetachedDragLoop(
        IntPtr windowHandle,
        PaneRect restoreBounds,
        WindowFrameAdjustment frameAdjustment,
        long dragGeneration,
        double anchorRatioX,
        double anchorOffsetY,
        CancellationToken cancellationToken)
    {
        PaneRect? lastBounds = null;
        Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        _ = timeBeginPeriod(1);
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsPrimaryMouseButtonPressed())
            {
                var moved = false;
                if (TryGetCursorPosition(out var cursor))
                {
                    var targetBounds = GetDetachedRestoreBoundsFromCursor(cursor, restoreBounds, anchorRatioX, anchorOffsetY);
                    if (lastBounds is null || !AreBoundsClose(lastBounds.Value, targetBounds, 0.5))
                    {
                        _workspaceApplyService.MoveWindowToBounds(windowHandle, targetBounds, frameAdjustment);
                        lastBounds = targetBounds;
                        moved = true;
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
            Dispatcher.BeginInvoke(() => PaneWorksLog.Error("Manual detached drag loop failed", exception));
        }
        finally
        {
            _ = timeEndPeriod(1);
            Dispatcher.BeginInvoke(() =>
            {
                if (_manualDetachedDragGeneration == dragGeneration
                    && _movingWindowHandle == windowHandle
                    && _manualDetachedDragActive)
                {
                    FinishManualDetachedDrag();
                }
            });
        }
    }

    private void FinishManualDetachedDrag()
    {
        if (_movingWindowHandle != IntPtr.Zero)
        {
            PaneWorksLog.Info($"Finish manual detached drag: 0x{_movingWindowHandle.ToInt64():X}");
        }

        ResetMovingWindowState();
        EnsureSnapOverlayHidden();
    }

    private bool RecoverStaleMoveStateIfNeeded()
    {
        if (_movingWindowHandle == IntPtr.Zero || _movingWindowStartedAt is null)
        {
            return false;
        }

        if (IsPrimaryMouseButtonPressed())
        {
            _movingWindowMouseReleasedAt = null;
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        _movingWindowMouseReleasedAt ??= now;
        var settleDelay = _movingWindowStartedByForegroundFallback
            ? TimeSpan.FromMilliseconds(80)
            : TimeSpan.FromMilliseconds(800);
        if (now - _movingWindowMouseReleasedAt.Value < settleDelay)
        {
            return false;
        }

        PaneWorksLog.Info($"Recover stale move state: 0x{_movingWindowHandle.ToInt64():X}");
        _snapAssistTimer.Stop();
        FinalizeMovingWindowAfterRelease(_movingWindowStartedByForegroundFallback ? "foreground-fallback" : "stale");
        ResetMovingWindowState();
        EnsureSnapOverlayHidden();
        return true;
    }

    private void ScheduleDetachedWindowRestore(IntPtr windowHandle, PaneRect restoreBounds)
    {
        RestoreDetachedWindowIfIdle(windowHandle, restoreBounds, "immediate");

        ScheduleDelayedDetachedWindowRestore(windowHandle, restoreBounds, TimeSpan.FromMilliseconds(80), "delay-80");
        ScheduleDelayedDetachedWindowRestore(windowHandle, restoreBounds, TimeSpan.FromMilliseconds(220), "delay-220");
    }

    private void ScheduleDetachedWindowRestoreDuringMove(IntPtr windowHandle, PaneRect restoreBounds)
    {
        ScheduleDelayedDetachedWindowRestoreDuringMove(windowHandle, restoreBounds, TimeSpan.FromMilliseconds(35), "move-delay-35");
        ScheduleDelayedDetachedWindowRestoreDuringMove(windowHandle, restoreBounds, TimeSpan.FromMilliseconds(90), "move-delay-90");
    }

    private void ScheduleDelayedDetachedWindowRestoreDuringMove(IntPtr windowHandle, PaneRect restoreBounds, TimeSpan delay, string phase)
    {
        var timer = new DispatcherTimer
        {
            Interval = delay
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RestoreDetachedWindowWhileMoving(windowHandle, restoreBounds, phase);
        };

        timer.Start();
    }

    private void RestoreDetachedWindowWhileMoving(IntPtr windowHandle, PaneRect restoreBounds, string phase)
    {
        if (windowHandle == IntPtr.Zero
            || _movingWindowHandle != windowHandle
            || !_movingWindowDetachedFromSnapGroup)
        {
            return;
        }

        var finalBounds = GetDetachedRestoreBounds(windowHandle, restoreBounds);
        PaneWorksLog.Info($"Apply detached restore {phase}: 0x{windowHandle.ToInt64():X}, restore={finalBounds.Width:0}x{finalBounds.Height:0}");
        _workspaceApplyService.RestoreWindowToBounds(windowHandle, finalBounds);
    }

    private void ScheduleDelayedDetachedWindowRestore(IntPtr windowHandle, PaneRect restoreBounds, TimeSpan delay, string phase)
    {
        var timer = new DispatcherTimer
        {
            Interval = delay
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RestoreDetachedWindowIfIdle(windowHandle, restoreBounds, phase);
        };

        timer.Start();
    }

    private void RestoreDetachedWindowIfIdle(IntPtr windowHandle, PaneRect restoreBounds, string phase)
    {
        if (windowHandle == IntPtr.Zero || _movingWindowHandle == windowHandle)
        {
            return;
        }

        var finalBounds = GetDetachedRestoreBounds(windowHandle, restoreBounds);
        PaneWorksLog.Info($"Apply detached restore {phase}: 0x{windowHandle.ToInt64():X}, restore={finalBounds.Width:0}x{finalBounds.Height:0}");
        _workspaceApplyService.RestoreWindowToBounds(windowHandle, finalBounds);
    }

    private PaneRect GetDetachedRestoreBounds(IntPtr windowHandle, PaneRect savedBounds)
    {
        var restoreWidth = Math.Max(120, savedBounds.Width);
        var restoreHeight = Math.Max(80, savedBounds.Height);

        if (TryGetCursorPosition(out var cursor))
        {
            return GetDetachedRestoreBoundsFromCursor(
                cursor,
                new PaneRect(savedBounds.X, savedBounds.Y, restoreWidth, restoreHeight),
                _movingWindowDragAnchorRatioX,
                _movingWindowDragAnchorOffsetY);
        }

        if (!_workspaceApplyService.TryGetVisibleWindowBounds(windowHandle, out var currentBounds))
        {
            return savedBounds;
        }

        return new PaneRect(
            currentBounds.X,
            currentBounds.Y,
            restoreWidth,
            restoreHeight);
    }

    private static PaneRect GetDetachedRestoreBoundsFromCursor(
        WpfPoint cursor,
        PaneRect savedBounds,
        double anchorRatioX,
        double anchorOffsetY)
    {
        var restoreWidth = Math.Max(120, savedBounds.Width);
        var restoreHeight = Math.Max(80, savedBounds.Height);
        var anchorX = Math.Clamp(anchorRatioX, DragAnchorMinRatio, DragAnchorMaxRatio) * restoreWidth;
        var anchorY = Math.Clamp(
            anchorOffsetY,
            DragAnchorMinOffsetY,
            Math.Min(DragAnchorMaxOffsetY, Math.Max(DragAnchorMinOffsetY, restoreHeight - 8)));

        return new PaneRect(
            cursor.X - anchorX,
            cursor.Y - anchorY,
            restoreWidth,
            restoreHeight);
    }

    private void CaptureMovingWindowDragAnchor(PaneRect? bounds)
    {
        _movingWindowDragAnchorRatioX = 0.5;
        _movingWindowDragAnchorOffsetY = 24;

        if (bounds is null || bounds.Value.Width <= 0 || bounds.Value.Height <= 0 || !TryGetCursorPosition(out var cursor))
        {
            return;
        }

        _movingWindowDragAnchorRatioX = Math.Clamp(
            (cursor.X - bounds.Value.X) / bounds.Value.Width,
            DragAnchorMinRatio,
            DragAnchorMaxRatio);
        _movingWindowDragAnchorOffsetY = Math.Clamp(
            cursor.Y - bounds.Value.Y,
            DragAnchorMinOffsetY,
            Math.Min(DragAnchorMaxOffsetY, Math.Max(DragAnchorMinOffsetY, bounds.Value.Height - 8)));
    }

    private void ResetMovingWindowState()
    {
        StopManualDetachedDragLoop();
        StopRuntimeLinkedResizeLoop();
        _snapAssistTimer.Stop();
        _movingWindowHandle = IntPtr.Zero;
        _hoveredSnapRegion = null;
        _hoveredSnapDisplayId = null;
        _movingWindowInitialBounds = null;
        _pendingDetachedRestoreBounds = null;
        _movingWindowStartedAt = null;
        _movingWindowMouseReleasedAt = null;
        _lastDetachedRestoreApplyAt = DateTimeOffset.MinValue;
        _manualDetachedDragLastBounds = null;
        _manualDetachedDragFrameAdjustment = default;
        _runtimeLinkedResizeSession = null;
        _detachedRestoreSettled = false;
        _manualDetachedDragActive = false;
        _runtimeLinkedResizeActive = false;
        _movingWindowWasSnapped = false;
        _movingWindowDetachedFromSnapGroup = false;
        _movingWindowDetachCandidate = false;
        _movingWindowSnapResizeGesture = false;
        _movingWindowStartedByForegroundFallback = false;
    }

    private bool IsResizeGesture(PaneRect? initialBounds)
    {
        if (initialBounds is null || !TryGetCursorPosition(out var cursor))
        {
            return false;
        }

        var bounds = initialBounds.Value;
        var nearLeft = Math.Abs(cursor.X - bounds.X) <= ResizeGripThreshold;
        var nearRight = Math.Abs(cursor.X - (bounds.X + bounds.Width)) <= ResizeGripThreshold;
        var nearTop = Math.Abs(cursor.Y - bounds.Y) <= ResizeGripThreshold;
        var nearBottom = Math.Abs(cursor.Y - (bounds.Y + bounds.Height)) <= ResizeGripThreshold;

        return nearLeft || nearRight || nearTop || nearBottom;
    }

    private static ResizeEdge? GetResizeEdge(PaneRect bounds, WpfPoint cursor)
    {
        ResizeEdge? edge = null;
        var bestDistance = ResizeGripThreshold + 1;

        ConsiderEdge(ResizeEdge.Left, Math.Abs(cursor.X - bounds.X));
        ConsiderEdge(ResizeEdge.Right, Math.Abs(cursor.X - (bounds.X + bounds.Width)));
        ConsiderEdge(ResizeEdge.Top, Math.Abs(cursor.Y - bounds.Y));
        ConsiderEdge(ResizeEdge.Bottom, Math.Abs(cursor.Y - (bounds.Y + bounds.Height)));

        return edge;

        void ConsiderEdge(ResizeEdge candidateEdge, double distance)
        {
            if (distance > ResizeGripThreshold || distance >= bestDistance)
            {
                return;
            }

            edge = candidateEdge;
            bestDistance = distance;
        }
    }

    private bool IsSnapTemporarilySuppressed(IntPtr windowHandle)
    {
        if (!_snapSuppressUntilByWindow.TryGetValue(windowHandle, out var suppressUntil))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow <= suppressUntil)
        {
            return true;
        }

        _snapSuppressUntilByWindow.Remove(windowHandle);
        return false;
    }

    private bool IsInternalWindowLayoutUpdateActive()
    {
        return _internalWindowLayoutUpdateDepth > 0
            || DateTimeOffset.UtcNow <= _suppressMoveEventsUntil;
    }

    private void BeginInternalWindowLayoutUpdate()
    {
        _internalWindowLayoutUpdateDepth++;
        _suppressMoveEventsUntil = DateTimeOffset.UtcNow + InternalLayoutMoveSuppressDuration;
        _snapAssistTimer.Stop();
        if (_windowMoveMonitor.IsRunning)
        {
            PaneWorksLog.Info("Pause snap assist hook for internal layout update");
            _windowMoveMonitor.Stop();
        }
    }

    private void EndInternalWindowLayoutUpdate()
    {
        _internalWindowLayoutUpdateDepth = Math.Max(0, _internalWindowLayoutUpdateDepth - 1);
        _suppressMoveEventsUntil = DateTimeOffset.UtcNow + InternalLayoutMoveSuppressDuration;
        if (_isSnapAssistArmed && !_windowMoveMonitor.IsRunning)
        {
            PaneWorksLog.Info("Resume snap assist hook after internal layout update");
            _windowMoveMonitor.Start();
        }
    }

    private void ReapplySnapBindingsSafely(Action? completed = null)
    {
        var updates = BuildSnapBindingUpdates(skipWindowHandle: null, removeDeadBindings: false);
        PaneWorksLog.Info($"Safe snap binding reapply requested: updates={updates.Count}, bindings={_snapBindings.Count}");
        if (updates.Count == 0)
        {
            completed?.Invoke();
            return;
        }

        BeginInternalWindowLayoutUpdate();
        _ = Task.Run(() =>
        {
            try
            {
                _workspaceApplyService.MoveSnappedWindowsToBounds(updates);
                return (Exception?)null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }).ContinueWith(task =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                EndInternalWindowLayoutUpdate();
                if (task.Result is { } exception)
                {
                    PaneWorksLog.Error("Safe snap binding reapply failed", exception);
                }
                else
                {
                    PaneWorksLog.Info($"Safe snap binding reapply completed: bindings={_snapBindings.Count}");
                }

                completed?.Invoke();
            });
        }, TaskScheduler.Default);
    }

    private List<WindowBoundsUpdate> BuildSnapBindingUpdates(IntPtr? skipWindowHandle, bool removeDeadBindings)
    {
        var updates = new List<WindowBoundsUpdate>();
        if (_snapBindings.Count == 0)
        {
            return updates;
        }

        var deadHandles = new List<IntPtr>();
        var geometryByDisplay = new Dictionary<string, IReadOnlyDictionary<string, ComputedRegion>>(StringComparer.OrdinalIgnoreCase);

        foreach (var binding in _snapBindings)
        {
            if (skipWindowHandle.HasValue && binding.Key == skipWindowHandle.Value)
            {
                continue;
            }

            if (!ViewModel.TryGetDisplayById(binding.Value.DisplayId, out var display))
            {
                deadHandles.Add(binding.Key);
                continue;
            }

            if (!geometryByDisplay.TryGetValue(display.Id, out var regionsById))
            {
                var geometry = _geometryCalculator.Compute(
                    ViewModel.GetSnapLayoutDocumentForDisplay(display.Id),
                    GetSnapTargetStageBounds(display),
                    SnapTargetSplitterThickness);
                regionsById = geometry.Regions.ToDictionary(item => item.NodeId, StringComparer.Ordinal);
                geometryByDisplay[display.Id] = regionsById;
            }

            if (!regionsById.TryGetValue(binding.Value.NodeId, out var region))
            {
                continue;
            }

            var targetBounds = _snapRuntimeBounds.TryGetValue(binding.Key, out var runtimeBounds)
                ? runtimeBounds
                : region.Bounds;

            if (!_workspaceApplyService.TryGetVisibleWindowBounds(binding.Key, out var currentBounds))
            {
                deadHandles.Add(binding.Key);
                continue;
            }

            if (AreBoundsClose(currentBounds, targetBounds, WindowBoundsTolerance))
            {
                continue;
            }

            updates.Add(new WindowBoundsUpdate(
                binding.Key,
                targetBounds,
                _workspaceApplyService.GetWindowFrameAdjustment(binding.Key)));
        }

        if (removeDeadBindings)
        {
            foreach (var handle in deadHandles)
            {
                _snapBindings.Remove(handle);
                _snapRuntimeBounds.Remove(handle);
                _snapWindowInfoCache.Remove(handle);
            }
        }

        return updates;
    }

    private void ReapplySnapBindings(IntPtr? skipWindowHandle = null)
    {
        var updates = BuildSnapBindingUpdates(skipWindowHandle, removeDeadBindings: true);
        foreach (var update in updates)
        {
            _workspaceApplyService.MoveSnappedWindowToBounds(
                update.WindowHandle,
                update.Bounds,
                update.FrameAdjustment);
        }
    }

    private static bool AreBoundsClose(PaneRect currentBounds, PaneRect targetBounds, double tolerance)
    {
        return Math.Abs(currentBounds.X - targetBounds.X) <= tolerance
            && Math.Abs(currentBounds.Y - targetBounds.Y) <= tolerance
            && IsSizeClose(currentBounds, targetBounds, tolerance);
    }

    private static bool IsSizeClose(PaneRect currentBounds, PaneRect targetBounds, double tolerance)
    {
        return Math.Abs(currentBounds.Width - targetBounds.Width) <= tolerance
            && Math.Abs(currentBounds.Height - targetBounds.Height) <= tolerance;
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
        IReadOnlyList<RuntimeLinkedResizeNeighbor> Neighbors);

    private sealed record RuntimeLinkedResizeNeighbor(
        IntPtr WindowHandle,
        PaneRect InitialBounds,
        RuntimeLinkedResizeSide Side,
        WindowMinimumSize MinimumSize,
        WindowFrameAdjustment FrameAdjustment);

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

    private PaneRect ResolveRestoreBoundsForSnap(IntPtr windowHandle)
    {
        if (_snapBindings.TryGetValue(windowHandle, out var existingBinding))
        {
            return existingBinding.RestoreBounds;
        }

        if (_movingWindowInitialBounds.HasValue)
        {
            return _movingWindowInitialBounds.Value;
        }

        return _workspaceApplyService.TryGetVisibleWindowBounds(windowHandle, out var currentBounds)
            ? currentBounds
            : new PaneRect();
    }

    private sealed record SnapBindingState(string NodeId, string DisplayId, PaneRect RestoreBounds);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private static bool TryGetCursorPosition(out WpfPoint point)
    {
        if (GetCursorPos(out var nativePoint))
        {
            point = new WpfPoint(nativePoint.X, nativePoint.Y);
            return true;
        }

        point = default;
        return false;
    }

    private static bool IsPrimaryMouseButtonPressed()
    {
        return GetAsyncKeyState(VirtualKeyLeftButton) < 0;
    }

    private bool MatchesMinimizeShortcut(WpfKeyEventArgs e)
    {
        return ShortcutGestureHelper.MatchesKeyEvent(e, ViewModel.MinimizeShortcut);
    }

    private void SwitchSnapLayoutAndResetRuntimeState(string layoutId)
    {
        ViewModel.SwitchSnapLayout(layoutId, notifyOnSuccess: false);
        ResetSnapRuntimeStateAfterLayoutSwitch();
    }

    private void SwitchWorkspaceProfileAndResetRuntimeState(string profileId, bool notifyOnSuccess)
    {
        if (!ViewModel.TrySwitchWorkspaceProfile(profileId, notifyOnSuccess))
        {
            return;
        }

        ResetSnapRuntimeStateAfterLayoutSwitch();
        Dispatcher.BeginInvoke(
            () => RestoreBoundWindowsForActiveWorkspaceProfile(clearRuntimeState: false, notifyOnResult: notifyOnSuccess, reason: "workspace-profile-switch"),
            DispatcherPriority.Background);
    }

    private void ResetSnapRuntimeStateAfterLayoutSwitch()
    {
        StopManualDetachedDragLoop();
        StopRuntimeLinkedResizeLoop();
        _snapAssistTimer.Stop();
        _snapBindings.Clear();
        _snapRuntimeBounds.Clear();
        _snapWindowInfoCache.Clear();
        _sessionSnapLayoutDocuments.Clear();
        _movingWindowHandle = IntPtr.Zero;
        _hoveredSnapRegion = null;
        _hoveredSnapDisplayId = null;
        _movingWindowInitialBounds = null;
        _pendingDetachedRestoreBounds = null;
        _movingWindowStartedAt = null;
        _movingWindowMouseReleasedAt = null;
        _lastDetachedRestoreApplyAt = DateTimeOffset.MinValue;
        _manualDetachedDragLastBounds = null;
        _manualDetachedDragFrameAdjustment = default;
        _runtimeLinkedResizeSession = null;
        _detachedRestoreSettled = false;
        _manualDetachedDragActive = false;
        _runtimeLinkedResizeActive = false;
        _movingWindowWasSnapped = false;
        _movingWindowDetachedFromSnapGroup = false;
        _movingWindowSnapResizeGesture = false;
        EnsureSnapOverlayHidden();
    }

    private PaneRect DeviceRectToDipRect(PaneRect rect)
    {
        var transform = GetTransformFromDevice();
        var topLeft = transform.Transform(new WpfPoint(rect.X, rect.Y));
        var bottomRight = transform.Transform(new WpfPoint(rect.X + rect.Width, rect.Y + rect.Height));
        return new PaneRect(
            topLeft.X,
            topLeft.Y,
            Math.Max(0, bottomRight.X - topLeft.X),
            Math.Max(0, bottomRight.Y - topLeft.Y));
    }

    private WpfPoint DevicePointToDipPoint(WpfPoint point)
    {
        return GetTransformFromDevice().Transform(point);
    }

    private Matrix GetTransformFromDevice()
    {
        return PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenSettings();
    }

    private void CloseWorkbenchButton_Click(object sender, RoutedEventArgs e)
    {
        MinimizeWorkbenchToTray();
    }

    private void MinimizeWorkbenchButton_Click(object sender, RoutedEventArgs e)
    {
        MinimizeWorkbenchToTaskbar();
    }

    private void SidebarWorkbenchButton_Click(object sender, RoutedEventArgs e)
    {
        MinimizeWorkbenchToSidebar();
    }

    private void RestoreWorkbenchButton_Click(object sender, RoutedEventArgs e)
    {
        RestoreWorkbenchFromSidebar();
    }

    private void MinimizeWorkbenchToSidebar()
    {
        EndWorkbenchPanelDrag(null);
        WorkbenchPanel.Visibility = Visibility.Collapsed;
        WorkbenchMiniBar.Visibility = Visibility.Visible;
        PaneWorksLog.Info("Workbench minimized to sidebar");
    }

    private void MinimizeWorkbenchToTaskbar()
    {
        EndWorkbenchPanelDrag(null);
        ShowInTaskbar = true;
        WindowState = WindowState.Minimized;
        PaneWorksLog.Info("Workbench minimized to taskbar");
    }

    private void MinimizeWorkbenchToTray()
    {
        EndWorkbenchPanelDrag(null);
        ((App)WpfApplication.Current).MinimizeMainWindowToTray();
    }

    private void RestoreWorkbenchFromSidebar()
    {
        WorkbenchMiniBar.Visibility = Visibility.Collapsed;
        WorkbenchPanel.Visibility = Visibility.Visible;
        PaneWorksLog.Info("Workbench restored from sidebar");
    }

    private void BindWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanEditWorkspaceBindings)
        {
            WpfMessageBox.Show(
                this,
                "请先选中工作区方案并点击“编辑绑定”，再给区域绑定窗口。",
                "PaneWorks",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!ViewModel.TryGetSelectedLeafRegion(out var displayId, out var nodeId))
        {
            WpfMessageBox.Show(
                this,
                "请先点击一个区域，再给它绑定窗口。",
                "PaneWorks",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var windows = _workspaceApplyService
            .GetVisibleWindows(new WindowInteropHelper(this).Handle)
            .OrderBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (windows.Count == 0)
        {
            WpfMessageBox.Show(
                this,
                "当前没有可绑定的桌面窗口。请先打开几个普通应用窗口再试。",
                "PaneWorks",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new WindowBindingPickerDialog(
            windows,
            $"当前区域：{nodeId}  |  当前屏幕：{ViewModel.CurrentDisplayName}");
        dialog.PreferredWindowHandle = FindSnappedWindowHandleForRegion(displayId, nodeId);
        dialog.Owner = this;
        dialog.Topmost = Topmost;

        if (dialog.ShowDialog() != true || dialog.SelectedWindow is null)
        {
            return;
        }

        if (!ViewModel.TrySetSelectedRegionWindowBinding(
                dialog.SelectedWindow.ProcessName,
                dialog.SelectedWindow.Title,
                dialog.SelectedWindow.ExecutablePath,
                dialog.SelectedWindow.ExplorerFolderPath,
                out var message))
        {
            WpfMessageBox.Show(
                this,
                message,
                "PaneWorks",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        WpfMessageBox.Show(
            this,
            $"{message} 点击“应用选中工作区”即可测试重新吸附，切换工作区时也会自动应用。",
            "PaneWorks",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void AutoBindSnappedWindowsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanEditWorkspaceBindings)
        {
            ViewModel.SetUserStatusMessage("请先选中工作区方案并点击“编辑绑定”，再一键绑定已吸附窗口。");
            return;
        }

        if (!TryCaptureSnappedWorkspaceWindowBindingRequests(out var requests, out var buildMessage))
        {
            ViewModel.SetUserStatusMessage(buildMessage);
            return;
        }

        var button = sender as System.Windows.Controls.Button;
        if (button is not null)
        {
            button.IsEnabled = false;
        }

        try
        {
            PaneWorksLog.Info($"Auto workspace bind started: requests={requests.Count}");
            var excludedWindowHandle = new WindowInteropHelper(this).Handle;
            var bindings = await Task.Run(() => BuildSnappedWorkspaceWindowBindings(requests, excludedWindowHandle));
            PaneWorksLog.Info($"Auto workspace bind built: bindings={bindings.Count}");

            if (bindings.Count == 0)
            {
                ViewModel.SetUserStatusMessage("没有找到可写入工作区的已吸附窗口。");
                return;
            }

            if (!ViewModel.TryUpsertWorkspaceWindowBindingsFast(bindings, out var message))
            {
                ViewModel.SetUserStatusMessage(message);
                return;
            }

            PaneWorksLog.Info($"Auto workspace bind saved: bindings={bindings.Count}");
            QueueExplorerFolderBindingCompletion(requests);
        }
        catch (Exception exception)
        {
            PaneWorksLog.Error("Auto workspace bind failed", exception);
            ViewModel.SetUserStatusMessage($"一键绑定失败：{exception.Message}");
        }
        finally
        {
            if (button is not null)
            {
                button.IsEnabled = true;
            }
        }
    }

    private void QueueSnappedWindowInfoCache(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || _snapWindowInfoCache.ContainsKey(windowHandle))
        {
            return;
        }

        var excludedWindowHandle = new WindowInteropHelper(this).Handle;
        _ = Task.Run(() =>
        {
            try
            {
                return _workspaceApplyService.TryGetVisibleWindowInfo(
                    windowHandle,
                    excludedWindowHandle,
                    includeExplorerFolderPath: false,
                    out var windowInfo)
                    ? windowInfo
                    : null;
            }
            catch
            {
                return null;
            }
        }).ContinueWith(task =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (task.Result is null || !_snapBindings.ContainsKey(windowHandle))
                {
                    return;
                }

                _snapWindowInfoCache[windowHandle] = task.Result;
            });
        }, TaskScheduler.Default);
    }

    private bool TryCaptureSnappedWorkspaceWindowBindingRequests(
        out List<SnappedWorkspaceWindowBindingRequest> requests,
        out string message)
    {
        requests = new List<SnappedWorkspaceWindowBindingRequest>();
        message = string.Empty;

        if (_snapBindings.Count == 0)
        {
            message = "当前还没有已吸附窗口。请先把窗口吸附到分区区域后再一键绑定。";
            return false;
        }

        foreach (var item in _snapBindings.ToList())
        {
            if (!_snapWindowInfoCache.TryGetValue(item.Key, out var windowInfo))
            {
                QueueSnappedWindowInfoCache(item.Key);
                continue;
            }

            requests.Add(new SnappedWorkspaceWindowBindingRequest(item.Value.DisplayId, item.Value.NodeId, windowInfo, 0));
        }

        if (requests.Count == 0)
        {
            message = "没有找到已缓存的吸附窗口。请把窗口重新吸附一次，或先点“应用选中工作区”后再一键绑定。";
            return false;
        }

        requests = AssignSnappedBindingRequestStackOrder(requests);
        return true;
    }

    private List<WorkspaceWindowBinding> BuildSnappedWorkspaceWindowBindings(
        IReadOnlyList<SnappedWorkspaceWindowBindingRequest> requests,
        IntPtr excludedWindowHandle)
    {
        var bindings = new List<WorkspaceWindowBinding>();
        foreach (var request in requests)
        {
            bindings.Add(CreateWorkspaceWindowBinding(request.DisplayId, request.NodeId, request.WindowInfo, request.StackOrder));
        }

        return bindings;
    }

    private static List<SnappedWorkspaceWindowBindingRequest> AssignSnappedBindingRequestStackOrder(
        IReadOnlyList<SnappedWorkspaceWindowBindingRequest> requests)
    {
        if (requests.Count <= 1)
        {
            return requests.ToList();
        }

        var zOrderRanks = BuildDesktopZOrderRank();
        var orderedRequests = new List<SnappedWorkspaceWindowBindingRequest>();
        foreach (var group in requests.GroupBy(item => GetBindingKey(item.DisplayId, item.NodeId), StringComparer.OrdinalIgnoreCase))
        {
            var stackOrder = 0;
            foreach (var request in group
                .OrderByDescending(item => GetDesktopZOrderRank(item.WindowInfo.Handle, zOrderRanks))
                .ThenBy(item => item.WindowInfo.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.WindowInfo.Title, StringComparer.OrdinalIgnoreCase))
            {
                orderedRequests.Add(request with { StackOrder = stackOrder });
                stackOrder++;
            }
        }

        return orderedRequests;
    }

    private static Dictionary<IntPtr, int> BuildDesktopZOrderRank()
    {
        var ranks = new Dictionary<IntPtr, int>();
        var windowHandle = GetTopWindow(IntPtr.Zero);
        var rank = 0;

        while (windowHandle != IntPtr.Zero && rank < 20000)
        {
            if (!ranks.ContainsKey(windowHandle))
            {
                ranks[windowHandle] = rank;
                rank++;
            }

            windowHandle = GetWindow(windowHandle, GetWindowNext);
        }

        return ranks;
    }

    private static int GetDesktopZOrderRank(
        IntPtr windowHandle,
        IReadOnlyDictionary<IntPtr, int> zOrderRanks)
    {
        return zOrderRanks.TryGetValue(windowHandle, out var rank)
            ? rank
            : int.MaxValue;
    }

    private void QueueExplorerFolderBindingCompletion(IReadOnlyList<SnappedWorkspaceWindowBindingRequest> requests)
    {
        foreach (var request in requests.Where(item => IsExplorerProcess(item.WindowInfo.ProcessName)))
        {
            _ = Task.Run(() =>
            {
                try
                {
                    return _workspaceApplyService.TryGetExplorerFolderPath(request.WindowInfo.Handle, out var folderPath)
                        ? NormalizeFolderPath(folderPath)
                        : string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }).ContinueWith(task =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    var folderPath = task.Result;
                    if (string.IsNullOrWhiteSpace(folderPath))
                    {
                        return;
                    }

                    var completedWindowInfo = request.WindowInfo with { ExplorerFolderPath = folderPath };
                    var binding = CreateWorkspaceWindowBinding(request.DisplayId, request.NodeId, completedWindowInfo, request.StackOrder);
                    if (ViewModel.TryUpsertWorkspaceWindowBindingPatch(
                            binding,
                            $"已补全 Explorer 文件夹绑定：{folderPath}"))
                    {
                        _snapWindowInfoCache[request.WindowInfo.Handle] = completedWindowInfo;
                        PaneWorksLog.Info($"Explorer folder binding completed: {folderPath}");
                    }
                });
            }, TaskScheduler.Default);
        }
    }

    private static WorkspaceWindowBinding CreateWorkspaceWindowBinding(
        string displayId,
        string nodeId,
        VisibleWindowInfo windowInfo,
        int stackOrder = 0)
    {
        var explorerFolderPath = NormalizeFolderPath(windowInfo.ExplorerFolderPath);
        var isExplorerFolder = IsExplorerProcess(windowInfo.ProcessName)
            && !string.IsNullOrWhiteSpace(explorerFolderPath);

        return new WorkspaceWindowBinding(
            displayId,
            nodeId,
            windowInfo.ProcessName,
            windowInfo.Title,
            windowInfo.ExecutablePath,
            string.Empty,
            isExplorerFolder ? explorerFolderPath : TryGetWorkingDirectory(windowInfo.ExecutablePath),
            isExplorerFolder ? "ExplorerFolder" : "Window",
            isExplorerFolder ? "FolderPath" : "Auto",
            isExplorerFolder ? explorerFolderPath : string.Empty,
            Math.Max(0, stackOrder));
    }

    private bool TryGetSnapBindingTargetBounds(
        SnapBindingState binding,
        Dictionary<string, IReadOnlyDictionary<string, ComputedRegion>> geometryByDisplay,
        out PaneRect bounds)
    {
        bounds = default;
        if (!ViewModel.TryGetDisplayById(binding.DisplayId, out var display))
        {
            return false;
        }

        if (!geometryByDisplay.TryGetValue(display.Id, out var regionsById))
        {
            var geometry = _geometryCalculator.Compute(
                ViewModel.GetSnapLayoutDocumentForDisplay(display.Id),
                GetSnapTargetStageBounds(display),
                SnapTargetSplitterThickness);
            regionsById = geometry.Regions.ToDictionary(item => item.NodeId, StringComparer.Ordinal);
            geometryByDisplay[display.Id] = regionsById;
        }

        if (!regionsById.TryGetValue(binding.NodeId, out var region))
        {
            return false;
        }

        bounds = region.Bounds;
        return true;
    }

    private static string TryGetWorkingDirectory(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetDirectoryName(executablePath) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsExplorerProcess(string processName)
    {
        return string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(processName, "explorer.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFolderPath(string value)
    {
        var normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        try
        {
            normalized = Path.GetFullPath(normalized);
        }
        catch
        {
        }

        return TrimTrailingDirectorySeparators(normalized);
    }

    private static string TrimTrailingDirectorySeparators(string path)
    {
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrWhiteSpace(root)
            && string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrWhiteSpace(trimmed) ? path : trimmed;
    }

    private void ClearWindowBindingButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryClearSelectedRegionWindowBinding(out var message))
        {
            ViewModel.SetUserStatusMessage(message);
            return;
        }

        ViewModel.SetUserStatusMessage(message);
    }

    private void ClearAllWindowBindingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryClearAllWorkspaceWindowBindingsFast(out var message))
        {
            ViewModel.SetUserStatusMessage(message);
            return;
        }

        ViewModel.SetUserStatusMessage(message);
    }

    private async void RestoreBoundWindowsForActiveWorkspaceProfile(bool clearRuntimeState, bool notifyOnResult, string reason)
    {
        if (!ViewModel.IsWorkspaceProfileEnabled)
        {
            if (notifyOnResult)
            {
                WpfMessageBox.Show(
                    this,
                    "请先启用一套工作区方案，再恢复绑定窗口。",
                    "PaneWorks",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return;
        }

        var bindings = ViewModel.GetActiveWorkspaceWindowBindings();
        if (bindings.Count == 0)
        {
            if (clearRuntimeState)
            {
                _snapBindings.Clear();
                _snapRuntimeBounds.Clear();
                _snapWindowInfoCache.Clear();
                _sessionSnapLayoutDocuments.Clear();
            }

            if (notifyOnResult)
            {
                WpfMessageBox.Show(
                    this,
                    "当前工作区方案还没有保存任何窗口绑定。",
                    "PaneWorks",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return;
        }

        var excludedWindowHandle = new WindowInteropHelper(this).Handle;
        var visibleWindows = _workspaceApplyService
            .GetVisibleWindows(excludedWindowHandle)
            .ToList();

        var matches = MatchWindowBindings(bindings, visibleWindows);

        if (clearRuntimeState)
        {
            _snapBindings.Clear();
            _snapRuntimeBounds.Clear();
            _snapWindowInfoCache.Clear();
            _sessionSnapLayoutDocuments.Clear();
        }

        var restoredCount = 0;
        var restoredMatches = new List<MatchedWindowBinding>();
        var geometryByDisplay = new Dictionary<string, IReadOnlyDictionary<string, ComputedRegion>>(StringComparer.OrdinalIgnoreCase);

        BeginInternalWindowLayoutUpdate();
        try
        {
            foreach (var match in matches)
            {
                if (!TryGetBindingRegion(match.Binding, geometryByDisplay, out var region))
                {
                    continue;
                }

                var restoreBounds = ResolveRestoreBoundsForSnap(match.Window.Handle);
                _snapBindings[match.Window.Handle] = new SnapBindingState(
                    match.Binding.NodeId,
                    match.Binding.DisplayId,
                    restoreBounds);
                _snapRuntimeBounds[match.Window.Handle] = region.Bounds;
                _snapWindowInfoCache[match.Window.Handle] = match.Window;
                _ = TrySnapWindowToBoundsWithStatus(match.Window.Handle, region.Bounds, "workspace-restore");
                restoredCount++;
                restoredMatches.Add(match);
            }
        }
        finally
        {
            EndInternalWindowLayoutUpdate();
        }

        RestoreWorkspaceWindowStackOrder(restoredMatches);

        var matchedBindingKeys = matches
            .Select(match => GetBindingInstanceKey(match.Binding))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingBindings = bindings
            .Where(binding => !matchedBindingKeys.Contains(GetBindingInstanceKey(binding)))
            .ToList();
        var launchedCount = LaunchMissingWorkspaceWindows(missingBindings);
        var launchedRestoredCount = 0;
        if (launchedCount > 0)
        {
            ViewModel.SetUserStatusMessage($"已启动 {launchedCount} 个缺失窗口，正在等待窗口就绪并吸附...");
            try
            {
                launchedRestoredCount = await RestoreLaunchedWorkspaceWindowsAsync(missingBindings, excludedWindowHandle);
            }
            catch (Exception exception)
            {
                PaneWorksLog.Error("Restore launched workspace windows failed", exception);
            }
        }

        restoredCount += launchedRestoredCount;
        RestoreRuntimeWorkspaceStackOrder(bindings);
        ScheduleWorkspaceStackOrderStabilization(bindings, reason);
        if (launchedRestoredCount > 0)
        {
            ViewModel.SetUserStatusMessage($"已等待并吸附 {launchedRestoredCount} 个刚启动的窗口。");
        }

        PaneWorksLog.Info($"Window binding restore: reason={reason}, bindings={bindings.Count}, matched={matches.Count}, launched={launchedCount}, restored={restoredCount}");

        if (notifyOnResult)
        {
            WpfMessageBox.Show(
                this,
                restoredCount > 0
                    ? launchedCount > 0
                        ? $"已按当前工作区恢复 {restoredCount} 个窗口，其中启动了 {launchedCount} 个未打开窗口。"
                        : $"已按当前工作区重新吸附 {restoredCount} 个已打开窗口。"
                    : "这次没有找到可吸附或可启动的工作区窗口。",
                "PaneWorks",
                MessageBoxButton.OK,
                restoredCount > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
    }

    private bool TryGetBindingRegion(
        WorkspaceWindowBinding binding,
        Dictionary<string, IReadOnlyDictionary<string, ComputedRegion>> geometryByDisplay,
        out ComputedRegion region)
    {
        region = default!;

        if (!ViewModel.TryGetDisplayById(binding.DisplayId, out var display))
        {
            return false;
        }

        if (!geometryByDisplay.TryGetValue(display.Id, out var regionsById))
        {
            var geometry = _geometryCalculator.Compute(
                ViewModel.GetSnapLayoutDocumentForDisplay(display.Id),
                GetSnapTargetStageBounds(display),
                SnapTargetSplitterThickness);
            regionsById = geometry.Regions.ToDictionary(item => item.NodeId, StringComparer.Ordinal);
            geometryByDisplay[display.Id] = regionsById;
        }

        if (regionsById is null)
        {
            return false;
        }

        if (!regionsById.TryGetValue(binding.NodeId, out var foundRegion))
        {
            return false;
        }

        region = foundRegion;
        return true;
    }

    private int LaunchMissingWorkspaceWindows(IReadOnlyList<WorkspaceWindowBinding> bindings)
    {
        var launchedCount = 0;
        foreach (var binding in bindings)
        {
            if (!TryCreateWorkspaceLaunchInfo(binding, out var startInfo))
            {
                continue;
            }

            try
            {
                PaneWorksLog.Info($"Launch workspace binding: {binding.ProcessName}, file={startInfo.FileName}, args={startInfo.Arguments}");
                Process.Start(startInfo);
                launchedCount++;
            }
            catch (Exception exception)
            {
                PaneWorksLog.Error($"Launch workspace binding failed: {binding.ProcessName}", exception);
            }
        }

        return launchedCount;
    }

    private async Task<int> RestoreLaunchedWorkspaceWindowsAsync(
        IReadOnlyList<WorkspaceWindowBinding> bindings,
        IntPtr excludedWindowHandle)
    {
        var watchBindings = bindings
            .Where(CanLaunchWorkspaceBinding)
            .ToList();
        var restoredBindingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deadline = DateTimeOffset.UtcNow + WorkspaceLaunchRestoreTimeout;
        var attempt = 0;
        var includeExplorerFolderPath = watchBindings.Any(IsExplorerFolderBinding);

        while (watchBindings.Count > 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(attempt == 0
                ? WorkspaceLaunchInitialRestoreDelay
                : WorkspaceLaunchRestoreRetryInterval);
            attempt++;

            var bindingsToMatch = watchBindings
                .Where(binding => !TryGetLiveRuntimeBindingHandle(binding, out _))
                .ToList();
            if (bindingsToMatch.Count == 0)
            {
                ScheduleLaunchedWorkspaceWindowHandoffWatch(
                    watchBindings,
                    excludedWindowHandle,
                    includeExplorerFolderPath,
                    deadline);
                break;
            }

            var snappedHandles = _snapBindings.Keys.ToHashSet();
            var visibleWindows = _workspaceApplyService
                .GetVisibleWindows(excludedWindowHandle, includeExplorerFolderPath)
                .Where(window => !snappedHandles.Contains(window.Handle))
                .ToList();
            var matches = MatchWindowBindings(bindingsToMatch, visibleWindows);
            if (matches.Count == 0)
            {
                continue;
            }

            _ = ApplyWorkspaceWindowMatches(matches);
            RestoreRuntimeWorkspaceStackOrder(watchBindings);
            foreach (var match in matches)
            {
                restoredBindingKeys.Add(GetBindingInstanceKey(match.Binding));
            }
            ScheduleWorkspaceStackOrderStabilization(watchBindings, "workspace-launch-restore");

            if (watchBindings.All(binding => TryGetLiveRuntimeBindingHandle(binding, out _)))
            {
                ScheduleLaunchedWorkspaceWindowHandoffWatch(
                    watchBindings,
                    excludedWindowHandle,
                    includeExplorerFolderPath,
                    deadline);
                break;
            }
        }

        var liveCount = watchBindings.Count(binding => TryGetLiveRuntimeBindingHandle(binding, out _));
        PaneWorksLog.Info($"Launched workspace restore finished: attempts={attempt}, restored={restoredBindingKeys.Count}, live={liveCount}, watched={watchBindings.Count}");
        return restoredBindingKeys.Count;
    }

    private void ScheduleLaunchedWorkspaceWindowHandoffWatch(
        IReadOnlyList<WorkspaceWindowBinding> bindings,
        IntPtr excludedWindowHandle,
        bool includeExplorerFolderPath,
        DateTimeOffset deadline)
    {
        var watchBindings = bindings.ToList();
        if (watchBindings.Count == 0 || DateTimeOffset.UtcNow >= deadline)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var handoffCount = 0;
                while (DateTimeOffset.UtcNow < deadline)
                {
                    await Task.Delay(WorkspaceLaunchRestoreRetryInterval);

                    List<WorkspaceWindowBinding> bindingsToMatch = [];
                    HashSet<IntPtr> snappedHandles = [];

                    await Dispatcher.InvokeAsync(() =>
                    {
                        bindingsToMatch = watchBindings
                            .Where(binding => !TryGetLiveRuntimeBindingHandle(binding, out _))
                            .ToList();
                        snappedHandles = _snapBindings.Keys.ToHashSet();
                    });

                    if (bindingsToMatch.Count == 0)
                    {
                        continue;
                    }

                    var visibleWindows = _workspaceApplyService
                        .GetVisibleWindows(excludedWindowHandle, includeExplorerFolderPath)
                        .Where(window => !snappedHandles.Contains(window.Handle))
                        .ToList();
                    var matches = MatchWindowBindings(bindingsToMatch, visibleWindows);
                    if (matches.Count == 0)
                    {
                        continue;
                    }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        handoffCount += ApplyWorkspaceWindowMatches(matches);
                        RestoreRuntimeWorkspaceStackOrder(watchBindings);
                        ScheduleWorkspaceStackOrderStabilization(watchBindings, "workspace-launch-handoff");
                    });
                }

                if (handoffCount > 0)
                {
                    PaneWorksLog.Info($"Launched workspace handoff finished: restored={handoffCount}");
                }
            }
            catch (Exception exception)
            {
                PaneWorksLog.Error("Launched workspace handoff watch failed", exception);
            }
        });
    }

    private int ApplyWorkspaceWindowMatches(IReadOnlyList<MatchedWindowBinding> matches)
    {
        if (matches.Count == 0)
        {
            return 0;
        }

        var restoredCount = 0;
        var restoredMatches = new List<MatchedWindowBinding>();
        var geometryByDisplay = new Dictionary<string, IReadOnlyDictionary<string, ComputedRegion>>(StringComparer.OrdinalIgnoreCase);

        BeginInternalWindowLayoutUpdate();
        try
        {
            foreach (var match in matches)
            {
                if (!TryGetBindingRegion(match.Binding, geometryByDisplay, out var region))
                {
                    continue;
                }

                var restoreBounds = ResolveRestoreBoundsForSnap(match.Window.Handle);
                _snapBindings[match.Window.Handle] = new SnapBindingState(
                    match.Binding.NodeId,
                    match.Binding.DisplayId,
                    restoreBounds);
                _snapRuntimeBounds[match.Window.Handle] = region.Bounds;
                _snapWindowInfoCache[match.Window.Handle] = match.Window;
                _ = TrySnapWindowToBoundsWithStatus(match.Window.Handle, region.Bounds, "workspace-launch-restore");
                ScheduleLaunchedWindowSnapStabilization(
                    match.Window.Handle,
                    match.Binding.DisplayId,
                    match.Binding.NodeId,
                    region.Bounds);
                restoredCount++;
                restoredMatches.Add(match);
            }
        }
        finally
        {
            EndInternalWindowLayoutUpdate();
        }

        RestoreWorkspaceWindowStackOrder(restoredMatches);
        return restoredCount;
    }

    private void ScheduleLaunchedWindowSnapStabilization(
        IntPtr windowHandle,
        string displayId,
        string nodeId,
        PaneRect targetBounds)
    {
        foreach (var delay in WorkspaceLaunchSnapStabilizationDelays)
        {
            _ = Task.Delay(delay).ContinueWith(_ =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        if (!_snapBindings.TryGetValue(windowHandle, out var binding)
                            || !string.Equals(binding.DisplayId, displayId, StringComparison.OrdinalIgnoreCase)
                            || !string.Equals(binding.NodeId, nodeId, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }

                        if (_snapRuntimeBounds.TryGetValue(windowHandle, out var runtimeBounds)
                            && !AreBoundsClose(runtimeBounds, targetBounds, 0.5))
                        {
                            return;
                        }

                        BeginInternalWindowLayoutUpdate();
                        try
                        {
                            if (!_workspaceApplyService.TrySnapWindowToBounds(windowHandle, targetBounds, out var errorCode))
                            {
                                PaneWorksLog.Info($"Launched window snap stabilization skipped: 0x{windowHandle.ToInt64():X}, error={errorCode}");
                                return;
                            }
                        }
                        finally
                        {
                            EndInternalWindowLayoutUpdate();
                        }

                        PaneWorksLog.Info($"Launched window snap stabilized: 0x{windowHandle.ToInt64():X}, delay={delay.TotalSeconds:0}s");
                        RestoreRuntimeWorkspaceStackOrder(ViewModel.GetActiveWorkspaceWindowBindings());
                    }
                    catch (Exception exception)
                    {
                        PaneWorksLog.Error("Launched window snap stabilization failed", exception);
                    }
                });
            }, TaskScheduler.Default);
        }
    }

    private void ScheduleWorkspaceStackOrderStabilization(
        IReadOnlyList<WorkspaceWindowBinding> bindings,
        string reason)
    {
        var bindingsSnapshot = bindings.ToList();
        if (bindingsSnapshot.Count <= 1)
        {
            return;
        }

        foreach (var delay in WorkspaceLaunchSnapStabilizationDelays)
        {
            _ = Task.Delay(delay).ContinueWith(_ =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        RestoreRuntimeWorkspaceStackOrder(bindingsSnapshot);
                    }
                    catch (Exception exception)
                    {
                        PaneWorksLog.Error($"Workspace stack order stabilization failed: {reason}", exception);
                    }
                });
            }, TaskScheduler.Default);
        }
    }

    private static bool TryCreateWorkspaceLaunchInfo(
        WorkspaceWindowBinding binding,
        out ProcessStartInfo startInfo)
    {
        startInfo = default!;

        if (IsExplorerFolderBinding(binding)
            && !string.IsNullOrWhiteSpace(binding.LaunchTarget)
            && Directory.Exists(binding.LaunchTarget))
        {
            startInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = QuoteProcessArgument(binding.LaunchTarget),
                WorkingDirectory = binding.LaunchTarget,
                UseShellExecute = true
            };
            return true;
        }

        if (TryCreatePackagedAppAliasLaunchInfo(binding, out startInfo))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(binding.ExecutablePath) && File.Exists(binding.ExecutablePath))
        {
            startInfo = new ProcessStartInfo
            {
                FileName = binding.ExecutablePath,
                Arguments = ResolveWorkspaceLaunchArguments(binding),
                WorkingDirectory = ResolveWorkspaceWorkingDirectory(binding),
                UseShellExecute = true
            };
            return true;
        }

        if (!string.IsNullOrWhiteSpace(binding.LaunchTarget))
        {
            startInfo = new ProcessStartInfo
            {
                FileName = binding.LaunchTarget,
                UseShellExecute = true
            };
            return true;
        }

        return false;
    }

    private static bool CanLaunchWorkspaceBinding(WorkspaceWindowBinding binding)
    {
        if (IsExplorerFolderBinding(binding)
            && !string.IsNullOrWhiteSpace(binding.LaunchTarget)
            && Directory.Exists(binding.LaunchTarget))
        {
            return true;
        }

        return IsLaunchablePackagedAppBinding(binding)
            || (!string.IsNullOrWhiteSpace(binding.ExecutablePath) && File.Exists(binding.ExecutablePath))
            || !string.IsNullOrWhiteSpace(binding.LaunchTarget);
    }

    private static bool TryCreatePackagedAppAliasLaunchInfo(
        WorkspaceWindowBinding binding,
        out ProcessStartInfo startInfo)
    {
        startInfo = default!;

        if (!IsLaunchablePackagedAppBinding(binding))
        {
            return false;
        }

        if (TryGetSystemNotepadPath(binding, out var notepadPath))
        {
            startInfo = new ProcessStartInfo
            {
                FileName = notepadPath,
                Arguments = ResolveWorkspaceLaunchArguments(binding),
                WorkingDirectory = Path.GetDirectoryName(notepadPath) ?? string.Empty,
                UseShellExecute = true
            };
            return true;
        }

        startInfo = new ProcessStartInfo
        {
            FileName = GetProcessExecutableAlias(binding.ProcessName),
            Arguments = ResolveWorkspaceLaunchArguments(binding),
            UseShellExecute = true
        };
        return true;
    }

    private static bool IsLaunchablePackagedAppBinding(WorkspaceWindowBinding binding)
    {
        return !string.IsNullOrWhiteSpace(binding.ProcessName)
            && IsWindowsAppsExecutablePath(binding.ExecutablePath);
    }

    private static bool TryGetSystemNotepadPath(
        WorkspaceWindowBinding binding,
        out string notepadPath)
    {
        notepadPath = string.Empty;
        if (!IsNotepadBinding(binding))
        {
            return false;
        }

        var systemNotepadPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "notepad.exe");
        if (!File.Exists(systemNotepadPath))
        {
            return false;
        }

        notepadPath = systemNotepadPath;
        return true;
    }

    private static bool IsNotepadBinding(WorkspaceWindowBinding binding)
    {
        return string.Equals(binding.ProcessName, "Notepad", StringComparison.OrdinalIgnoreCase)
            || string.Equals(binding.ProcessName, "Notepad.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetProcessExecutableAlias(string processName)
    {
        var normalizedProcessName = processName.Trim();
        return normalizedProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? normalizedProcessName
            : $"{normalizedProcessName}.exe";
    }

    private static bool IsWindowsAppsExecutablePath(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            var normalizedPath = Path.GetFullPath(executablePath);
            var windowsAppsPath = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "WindowsApps"));

            return normalizedPath.StartsWith(
                windowsAppsPath + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return executablePath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string ResolveWorkspaceLaunchArguments(WorkspaceWindowBinding binding)
    {
        if (!string.IsNullOrWhiteSpace(binding.LaunchArguments))
        {
            return binding.LaunchArguments;
        }

        return string.IsNullOrWhiteSpace(binding.LaunchTarget) ? string.Empty : binding.LaunchTarget;
    }

    private static string ResolveWorkspaceWorkingDirectory(WorkspaceWindowBinding binding)
    {
        if (!string.IsNullOrWhiteSpace(binding.WorkingDirectory) && Directory.Exists(binding.WorkingDirectory))
        {
            return binding.WorkingDirectory;
        }

        return TryGetWorkingDirectory(binding.ExecutablePath);
    }

    private static string QuoteProcessArgument(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private void RestoreWorkspaceWindowStackOrder(IReadOnlyList<MatchedWindowBinding> matches)
    {
        if (matches.Count <= 1)
        {
            return;
        }

        foreach (var group in matches.GroupBy(match => GetBindingKey(match.Binding), StringComparer.OrdinalIgnoreCase))
        {
            var windowHandles = group
                .OrderByDescending(match => match.Binding.StackOrder)
                .Select(match => match.Window.Handle)
                .ToList();
            _workspaceApplyService.ArrangeWindowsTopToBottom(windowHandles);
        }
    }

    private void RestoreRuntimeWorkspaceStackOrder(IReadOnlyList<WorkspaceWindowBinding> bindings)
    {
        if (bindings.Count <= 1)
        {
            return;
        }

        foreach (var group in bindings.GroupBy(GetBindingKey, StringComparer.OrdinalIgnoreCase))
        {
            var windowHandles = new List<IntPtr>();
            foreach (var binding in group.OrderByDescending(item => item.StackOrder))
            {
                if (TryGetLiveRuntimeBindingHandle(binding, out var windowHandle))
                {
                    windowHandles.Add(windowHandle);
                }
            }

            _workspaceApplyService.ArrangeWindowsTopToBottom(windowHandles);
        }
    }

    private bool TryGetLiveRuntimeBindingHandle(WorkspaceWindowBinding binding, out IntPtr windowHandle)
    {
        windowHandle = IntPtr.Zero;
        var staleHandles = new List<IntPtr>();
        var candidates = _snapBindings
            .Where(item =>
                string.Equals(item.Value.DisplayId, binding.DisplayId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Value.NodeId, binding.NodeId, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Key)
            .ToList();

        foreach (var candidateHandle in candidates)
        {
            if (!_workspaceApplyService.TryGetVisibleWindowBounds(candidateHandle, out _))
            {
                staleHandles.Add(candidateHandle);
                continue;
            }

            if (!_snapWindowInfoCache.TryGetValue(candidateHandle, out var windowInfo))
            {
                QueueSnappedWindowInfoCache(candidateHandle);
                continue;
            }

            if (ScoreWindowBinding(binding, windowInfo) <= 0)
            {
                continue;
            }

            windowHandle = candidateHandle;
            break;
        }

        foreach (var staleHandle in staleHandles)
        {
            _snapBindings.Remove(staleHandle);
            _snapRuntimeBounds.Remove(staleHandle);
            _snapWindowInfoCache.Remove(staleHandle);
        }

        return windowHandle != IntPtr.Zero;
    }

    private static List<MatchedWindowBinding> MatchWindowBindings(
        IReadOnlyList<WorkspaceWindowBinding> bindings,
        IReadOnlyList<VisibleWindowInfo> windows)
    {
        var remainingWindows = windows.ToList();
        var matches = new List<MatchedWindowBinding>();

        foreach (var binding in bindings)
        {
            var match = remainingWindows
                .Select(window => new
                {
                    Window = window,
                    Score = ScoreWindowBinding(binding, window)
                })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Window.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Window.Title, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (match is null)
            {
                continue;
            }

            matches.Add(new MatchedWindowBinding(binding, match.Window));
            remainingWindows.Remove(match.Window);
        }

        return matches;
    }

    private static int ScoreWindowBinding(WorkspaceWindowBinding binding, VisibleWindowInfo window)
    {
        if (IsExplorerFolderBinding(binding))
        {
            var targetFolderPath = NormalizeFolderPath(binding.LaunchTarget);
            var windowFolderPath = NormalizeFolderPath(window.ExplorerFolderPath);
            if (string.IsNullOrWhiteSpace(targetFolderPath)
                || string.IsNullOrWhiteSpace(windowFolderPath)
                || !string.Equals(targetFolderPath, windowFolderPath, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            return 200 + (IsExplorerProcess(window.ProcessName) ? 20 : 0);
        }

        var score = 0;
        if (!string.IsNullOrWhiteSpace(binding.ExecutablePath)
            && !string.IsNullOrWhiteSpace(window.ExecutablePath)
            && string.Equals(binding.ExecutablePath, window.ExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }

        if (string.Equals(binding.ProcessName, window.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        if (score == 0)
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(binding.WindowTitleSnapshot))
        {
            return score;
        }

        if (string.Equals(binding.WindowTitleSnapshot, window.Title, StringComparison.OrdinalIgnoreCase))
        {
            return score + 30;
        }

        if (window.Title.Contains(binding.WindowTitleSnapshot, StringComparison.OrdinalIgnoreCase)
            || binding.WindowTitleSnapshot.Contains(window.Title, StringComparison.OrdinalIgnoreCase))
        {
            return score + 20;
        }

        return score;
    }

    private static string GetBindingKey(WorkspaceWindowBinding binding)
    {
        return GetBindingKey(binding.DisplayId, binding.NodeId);
    }

    private static string GetBindingInstanceKey(WorkspaceWindowBinding binding)
    {
        return string.Join(
            "::",
            binding.DisplayId,
            binding.NodeId,
            binding.ProcessName,
            binding.WindowTitleSnapshot,
            binding.ExecutablePath,
            binding.LaunchTarget,
            binding.StackOrder.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string GetBindingKey(string displayId, string nodeId)
    {
        return $"{displayId}::{nodeId}";
    }

    private static bool IsExplorerFolderBinding(WorkspaceWindowBinding binding)
    {
        return string.Equals(binding.MatchKind, "ExplorerFolder", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(binding.LaunchTarget);
    }

    private sealed record MatchedWindowBinding(WorkspaceWindowBinding Binding, VisibleWindowInfo Window);

    private sealed record SnappedWorkspaceWindowBindingRequest(
        string DisplayId,
        string NodeId,
        VisibleWindowInfo WindowInfo,
        int StackOrder);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetTopWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [System.Runtime.InteropServices.DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [System.Runtime.InteropServices.DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint uPeriod);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    private static void WaitForDesktopFrame()
    {
        if (DwmFlush() != 0)
        {
            Thread.Sleep(1);
        }
    }

    private enum SnapAssistMode
    {
        None,
        SavedLayout,
        RuntimeSession
    }
}
