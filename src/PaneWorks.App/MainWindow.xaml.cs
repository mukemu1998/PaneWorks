using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PaneWorks.App.Controls;
using PaneWorks.App.Diagnostics;
using PaneWorks.App.ViewModels;
using PaneWorks.Core.Models;
using PaneWorks.Core.Services;
using PaneWorks.Infrastructure.Windows;
using WpfApplication = System.Windows.Application;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
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
    private static readonly bool EnableRuntimeLinkedResize = true;
    private static readonly TimeSpan DetachedRestoreApplyInterval = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan InternalLayoutMoveSuppressDuration = TimeSpan.FromMilliseconds(450);
    private readonly WindowMoveMonitor _windowMoveMonitor = new();
    private readonly WorkspaceApplyService _workspaceApplyService = new();
    private readonly LayoutGeometryCalculator _geometryCalculator = new();
    private readonly SplitResizeService _splitResizeService = new();
    private readonly DisplayDiscoveryService _displayDiscoveryService = new();
    private readonly DispatcherTimer _snapAssistTimer;
    private readonly DispatcherTimer _snapAssistHealthTimer;
    private readonly Dictionary<IntPtr, SnapBindingState> _snapBindings = new();
    private readonly Dictionary<IntPtr, PaneRect> _snapRuntimeBounds = new();
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

        _snapAssistTimer.Tick += SnapAssistTimer_Tick;
        _snapAssistHealthTimer.Tick += SnapAssistHealthTimer_Tick;
        EditorCanvas.NodeSelected += EditorCanvas_NodeSelected;
        EditorCanvas.SplitterRatioChanged += EditorCanvas_SplitterRatioChanged;
        EditorCanvas.CanvasContextActionRequested += EditorCanvas_CanvasContextActionRequested;
        EditorCanvas.SnapLayoutSwitchRequested += EditorCanvas_SnapLayoutSwitchRequested;
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

    public string GetActiveSnapLayoutId()
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.Invoke(GetActiveSnapLayoutId);
        }

        return ViewModel.ActiveSnapLayoutId;
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

    public void PrepareForTrayRestore()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(PrepareForTrayRestore);
            return;
        }

        PaneWorksLog.Info("Prepare main window restore from tray");
        EnsureSnapOverlayHidden();
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
            PaneWorksLog.Info("Minimize shortcut pressed");
            ((App)WpfApplication.Current).MinimizeMainWindowToTray();
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
            ViewModel.SaveLayoutCommand.Execute(null);
            return true;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S)
        {
            ViewModel.SaveAsLayoutCommand.Execute(null);
            return true;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            ViewModel.NewLayoutCommand.Execute(null);
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
            ViewModel.DeleteSelectedSplitCommand.Execute(null);
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
    }

    private void DisarmSnapAssist(bool restoreWindow)
    {
        _isSnapAssistArmed = false;
        _snapAssistHealthTimer.Stop();
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
        ViewModel.ResetSnapLayoutPreview();
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

                _movingWindowHandle = e.WindowHandle;
                _movingWindowStartedAt = now;
                PaneWorksLog.Info($"Move started: 0x{_movingWindowHandle.ToInt64():X}");
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
                ViewModel.ResetSnapLayoutPreview();

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
            catch (Exception exception)
            {
                PaneWorksLog.Error("Move started handler failed", exception);
                ResetMovingWindowState();
                ViewModel.ResetSnapLayoutPreview();
                EnsureSnapOverlayHidden();
            }
        });
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

                if (_movingWindowDetachedFromSnapGroup)
                {
                    RestoreDetachedWindowAfterMove();
                }
                else if (_hoveredSnapRegion is not null && !string.IsNullOrWhiteSpace(_hoveredSnapDisplayId))
                {
                    var restoreBounds = ResolveRestoreBoundsForSnap(_movingWindowHandle);
                    PaneWorksLog.Info($"Snap window: 0x{_movingWindowHandle.ToInt64():X}, restore={restoreBounds.Width:0}x{restoreBounds.Height:0}");
                    _snapBindings[_movingWindowHandle] = new SnapBindingState(_hoveredSnapRegion.NodeId, _hoveredSnapDisplayId, restoreBounds);
                    _snapRuntimeBounds[_movingWindowHandle] = _hoveredSnapRegion.Bounds;
                    _workspaceApplyService.SnapWindowToBounds(_movingWindowHandle, _hoveredSnapRegion.Bounds);
                }
                else if (_movingWindowSnapResizeGesture)
                {
                    PaneWorksLog.Info($"Finalize snapped runtime resize without linked session: 0x{_movingWindowHandle.ToInt64():X}");
                    CaptureCurrentRuntimeBoundsForDisplay(null);
                }
                else if (!_movingWindowDetachedFromSnapGroup && _snapBindings.ContainsKey(_movingWindowHandle))
                {
                    PaneWorksLog.Info($"Finalize snapped resize: 0x{_movingWindowHandle.ToInt64():X}");
                    if (TryUpdateSnapLayoutFromWindowBounds(_movingWindowHandle, persist: true))
                    {
                        PaneWorksLog.Info($"Apply snapped resize bindings: 0x{_movingWindowHandle.ToInt64():X}");
                        ReapplySnapBindings();
                    }
                    else
                    {
                        PaneWorksLog.Info($"Reapply snapped bindings without layout change: 0x{_movingWindowHandle.ToInt64():X}");
                        ReapplySnapBindings();
                    }
                }

                ResetMovingWindowState();
                ViewModel.ResetSnapLayoutPreview();
                EnsureSnapOverlayHidden();
            }
            catch (Exception exception)
            {
                PaneWorksLog.Error("Move ended handler failed", exception);
                ResetMovingWindowState();
                ViewModel.ResetSnapLayoutPreview();
                EnsureSnapOverlayHidden();
            }
        });
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

                if (!IsSnapModifierPressed())
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
            ViewModel.ResetSnapLayoutPreview();
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
            var snapDocument = ViewModel.GetSnapLayoutDocumentForDisplay(activeDisplay.Id);
            var targetStageBounds = GetSnapTargetStageBounds(activeDisplay);
            var targetGeometry = _geometryCalculator.Compute(
                snapDocument,
                targetStageBounds,
                SnapTargetSplitterThickness);

            _hoveredSnapRegion = targetGeometry.Regions.FirstOrDefault(region => Contains(region.Bounds, cursorInDevicePixels));
            _hoveredSnapDisplayId = activeDisplay.Id;

            EnsureSnapOverlaysVisible(activeDisplay.Id, _hoveredSnapRegion?.NodeId);
        }
        catch (Exception exception)
        {
            PaneWorksLog.Error("Snap assist preview failed", exception);
            ResetMovingWindowState();
            ViewModel.ResetSnapLayoutPreview();
            EnsureSnapOverlayHidden();
        }
    }

    private void EnsureSnapOverlaysVisible(string activeDisplayId, string? activePreviewNodeId)
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
            overlayWindow.Document = ViewModel.GetSnapLayoutDocumentForDisplay(display.Id);
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

    private bool IsSnapModifierPressed()
    {
        return ShortcutGestureHelper.IsPressed(ViewModel.SnapModifierKey);
    }

    private bool TryUpdateSnapLayoutFromWindowBounds(IntPtr windowHandle, bool persist)
    {
        if (!TryResolveSnapResize(windowHandle, out var resizeCandidate, out var clampedRatio))
        {
            return false;
        }

        return ViewModel.UpdateSnapLayoutSplitRatioForDisplay(
            resizeCandidate.DisplayId,
            resizeCandidate.Splitter.SplitNodeId,
            clampedRatio,
            persist);
    }

    private bool TryResolveSnapResize(IntPtr windowHandle, out ResizeCandidate resizeCandidate, out double clampedRatio)
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
        var geometry = _geometryCalculator.Compute(
            ViewModel.GetSnapLayoutDocumentForDisplay(binding.DisplayId),
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
            ViewModel.GetSnapLayoutDocumentForDisplay(binding.DisplayId).Root,
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
                    var edgePosition = GetEdgePosition(sourceBounds, session.Edge);
                    if (double.IsNaN(lastEdgePosition) || Math.Abs(edgePosition - lastEdgePosition) >= 1)
                    {
                        var updates = BuildRuntimeLinkedResizeUpdates(session, edgePosition);
                        if (updates.Count > 0)
                        {
                            _suppressMoveEventsUntil = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(120);
                            _workspaceApplyService.MoveSnappedWindowsToBounds(updates);
                            moved = true;
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

                return new RuntimeLinkedResizeNeighbor(
                    item.Key,
                    bounds,
                    _workspaceApplyService.GetWindowFrameAdjustment(item.Key));
            })
            .Where(item => item is not null)
            .Cast<RuntimeLinkedResizeNeighbor>()
            .Where(item => IsRuntimeResizeNeighbor(sourceBounds, item.InitialBounds, edge.Value))
            .ToList();

        if (neighbors.Count == 0)
        {
            return false;
        }

        session = new RuntimeLinkedResizeSession(
            windowHandle,
            binding.DisplayId,
            sourceBounds,
            edge.Value,
            neighbors);
        return true;
    }

    private List<WindowBoundsUpdate> BuildRuntimeLinkedResizeUpdates(
        RuntimeLinkedResizeSession session,
        double edgePosition)
    {
        var updates = new List<WindowBoundsUpdate>(session.Neighbors.Count);
        foreach (var neighbor in session.Neighbors)
        {
            var bounds = GetRuntimeLinkedNeighborBounds(neighbor.InitialBounds, session.Edge, edgePosition);
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
        ResetMovingWindowState();
        ViewModel.ResetSnapLayoutPreview();
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

    private static bool IsRuntimeResizeNeighbor(PaneRect sourceBounds, PaneRect candidateBounds, ResizeEdge edge)
    {
        return edge switch
        {
            ResizeEdge.Left => AreEdgesClose(GetRight(candidateBounds), sourceBounds.X)
                && HasVerticalOverlap(sourceBounds, candidateBounds),
            ResizeEdge.Right => AreEdgesClose(candidateBounds.X, GetRight(sourceBounds))
                && HasVerticalOverlap(sourceBounds, candidateBounds),
            ResizeEdge.Top => AreEdgesClose(GetBottom(candidateBounds), sourceBounds.Y)
                && HasHorizontalOverlap(sourceBounds, candidateBounds),
            _ => AreEdgesClose(candidateBounds.Y, GetBottom(sourceBounds))
                && HasHorizontalOverlap(sourceBounds, candidateBounds)
        };
    }

    private static PaneRect GetRuntimeLinkedNeighborBounds(PaneRect initialBounds, ResizeEdge edge, double edgePosition)
    {
        var right = GetRight(initialBounds);
        var bottom = GetBottom(initialBounds);
        return edge switch
        {
            ResizeEdge.Left => new PaneRect(
                initialBounds.X,
                initialBounds.Y,
                Math.Max(RuntimeLinkedResizeMinWidth, edgePosition - initialBounds.X),
                initialBounds.Height),
            ResizeEdge.Right => GetRightNeighborBounds(initialBounds, right, edgePosition),
            ResizeEdge.Top => new PaneRect(
                initialBounds.X,
                initialBounds.Y,
                initialBounds.Width,
                Math.Max(RuntimeLinkedResizeMinHeight, edgePosition - initialBounds.Y)),
            _ => GetBottomNeighborBounds(initialBounds, bottom, edgePosition)
        };
    }

    private static PaneRect GetRightNeighborBounds(PaneRect initialBounds, double right, double edgePosition)
    {
        var width = Math.Max(RuntimeLinkedResizeMinWidth, right - edgePosition);
        return new PaneRect(right - width, initialBounds.Y, width, initialBounds.Height);
    }

    private static PaneRect GetBottomNeighborBounds(PaneRect initialBounds, double bottom, double edgePosition)
    {
        var height = Math.Max(RuntimeLinkedResizeMinHeight, bottom - edgePosition);
        return new PaneRect(initialBounds.X, bottom - height, initialBounds.Width, height);
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

    private static bool HasVerticalOverlap(PaneRect first, PaneRect second)
    {
        return Math.Min(GetBottom(first), GetBottom(second)) - Math.Max(first.Y, second.Y) > RuntimeLinkedResizeTolerance;
    }

    private static bool HasHorizontalOverlap(PaneRect first, PaneRect second)
    {
        return Math.Min(GetRight(first), GetRight(second)) - Math.Max(first.X, second.X) > RuntimeLinkedResizeTolerance;
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
        ViewModel.ResetSnapLayoutPreview();
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
        if (now - _movingWindowMouseReleasedAt.Value < TimeSpan.FromMilliseconds(800))
        {
            return false;
        }

        PaneWorksLog.Info($"Recover stale move state: 0x{_movingWindowHandle.ToInt64():X}");
        if (_movingWindowDetachedFromSnapGroup)
        {
            RestoreDetachedWindowAfterMove();
        }

        ResetMovingWindowState();
        ViewModel.ResetSnapLayoutPreview();
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
        ResizeEdge Edge,
        IReadOnlyList<RuntimeLinkedResizeNeighbor> Neighbors);

    private sealed record RuntimeLinkedResizeNeighbor(
        IntPtr WindowHandle,
        PaneRect InitialBounds,
        WindowFrameAdjustment FrameAdjustment);

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

    private void ResetSnapRuntimeStateAfterLayoutSwitch()
    {
        StopManualDetachedDragLoop();
        StopRuntimeLinkedResizeLoop();
        _snapAssistTimer.Stop();
        _snapBindings.Clear();
        _snapRuntimeBounds.Clear();
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
        ViewModel.ResetSnapLayoutPreview();
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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

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
}
