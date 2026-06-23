using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PaneWorks.App.Controls;
using PaneWorks.App.ViewModels;
using PaneWorks.Core.Models;
using PaneWorks.Core.Services;
using PaneWorks.Infrastructure.Windows;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;
using WpfPoint = System.Windows.Point;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
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
    private readonly WindowMoveMonitor _windowMoveMonitor = new();
    private readonly WorkspaceApplyService _workspaceApplyService = new();
    private readonly LayoutGeometryCalculator _geometryCalculator = new();
    private readonly SplitResizeService _splitResizeService = new();
    private readonly DispatcherTimer _snapAssistTimer;
    private readonly Dictionary<IntPtr, string> _snapBindings = new();
    private SnapOverlayWindow? _snapOverlayWindow;
    private IntPtr _movingWindowHandle;
    private ComputedRegion? _hoveredSnapRegion;
    private PaneRect? _movingWindowInitialBounds;
    private bool _isSnapAssistArmed;
    private bool _movingWindowWasSnapped;
    private bool _movingWindowDetachedFromSnapGroup;

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        _snapAssistTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        _snapAssistTimer.Tick += SnapAssistTimer_Tick;
        EditorCanvas.NodeSelected += EditorCanvas_NodeSelected;
        EditorCanvas.SplitterRatioChanged += EditorCanvas_SplitterRatioChanged;
        EditorCanvas.CanvasContextActionRequested += EditorCanvas_CanvasContextActionRequested;
        EditorCanvas.SnapLayoutSwitchRequested += EditorCanvas_SnapLayoutSwitchRequested;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    public IReadOnlyList<LayoutListItemViewModel> GetTrayLayoutItems()
    {
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
        return ViewModel.ActiveSnapLayoutId;
    }

    public void SwitchSnapLayoutFromTray(string layoutId)
    {
        ViewModel.SwitchSnapLayout(layoutId, notifyOnSuccess: false);
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
        ViewModel.SwitchSnapLayout(e.LayoutId, notifyOnSuccess: false);
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
    }

    private void MainWindow_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (!IsTextInputFocused() && MatchesMinimizeShortcut(e))
        {
            e.Handled = true;
            ((App)WpfApplication.Current).MinimizeMainWindowToTray();
            return;
        }

        if (HandleCommandShortcut(e))
        {
            e.Handled = true;
            return;
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
        _windowMoveMonitor.MoveStarted += WindowMoveMonitor_MoveStarted;
        _windowMoveMonitor.MoveEnded += WindowMoveMonitor_MoveEnded;
        EnsureSnapAssistStarted();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized && _isSnapAssistArmed)
        {
            if (WindowState != WindowState.Maximized)
            {
                WindowState = WindowState.Maximized;
            }

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
        ViewModel.PreviewNodeId = null;
    }

    private void DisarmSnapAssist(bool restoreWindow)
    {
        _isSnapAssistArmed = false;
        _windowMoveMonitor.Stop();
        _snapAssistTimer.Stop();
        _movingWindowHandle = IntPtr.Zero;
        _hoveredSnapRegion = null;
        _movingWindowInitialBounds = null;
        _movingWindowWasSnapped = false;
        _movingWindowDetachedFromSnapGroup = false;
        ViewModel.ResetSnapLayoutPreview();
        ViewModel.PreviewNodeId = null;
        EnsureSnapOverlayHidden();

        if (restoreWindow)
        {
            WindowState = WindowState.Maximized;
            Activate();
        }
    }

    private void WindowMoveMonitor_MoveStarted(object? sender, WindowMoveStateChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_isSnapAssistArmed || e.WindowHandle == IntPtr.Zero)
            {
                return;
            }

            _movingWindowHandle = e.WindowHandle;
            _hoveredSnapRegion = null;
            _movingWindowInitialBounds = _workspaceApplyService.TryGetVisibleWindowBounds(_movingWindowHandle, out var initialBounds)
                ? DeviceBoundsToDipBounds(initialBounds)
                : null;
            _movingWindowWasSnapped = _snapBindings.ContainsKey(_movingWindowHandle);
            _movingWindowDetachedFromSnapGroup = _movingWindowWasSnapped && !IsResizeGesture(_movingWindowInitialBounds);
            ViewModel.ResetSnapLayoutPreview();
            ViewModel.PreviewNodeId = null;

            if (_movingWindowDetachedFromSnapGroup)
            {
                _snapBindings.Remove(_movingWindowHandle);
                _workspaceApplyService.RestoreRoundedCorners(_movingWindowHandle);
                ReapplySnapBindings();
            }

            _snapAssistTimer.Start();
        });
    }

    private void WindowMoveMonitor_MoveEnded(object? sender, WindowMoveStateChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_movingWindowHandle == IntPtr.Zero || e.WindowHandle != _movingWindowHandle)
            {
                return;
            }

            _snapAssistTimer.Stop();

            if (_hoveredSnapRegion is not null)
            {
                _snapBindings[_movingWindowHandle] = _hoveredSnapRegion.NodeId;
                _workspaceApplyService.SnapWindowToBounds(
                    _movingWindowHandle,
                    DipBoundsToDeviceBounds(_hoveredSnapRegion.Bounds));
            }
            else if (!_movingWindowDetachedFromSnapGroup && _snapBindings.ContainsKey(_movingWindowHandle))
            {
                ViewModel.ResetSnapLayoutPreview();
                if (TryUpdateSnapLayoutFromWindowBounds(_movingWindowHandle))
                {
                    ReapplySnapBindings();
                }
                else
                {
                    ReapplySnapBindings();
                }
            }

            _movingWindowHandle = IntPtr.Zero;
            _hoveredSnapRegion = null;
            _movingWindowInitialBounds = null;
            _movingWindowWasSnapped = false;
            _movingWindowDetachedFromSnapGroup = false;
            ViewModel.ResetSnapLayoutPreview();
            ViewModel.PreviewNodeId = null;
            EnsureSnapOverlayHidden();
        });
    }

    private void SnapAssistTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isSnapAssistArmed || _movingWindowHandle == IntPtr.Zero)
        {
            return;
        }

        if (!IsSnapModifierPressed())
        {
            if (!_movingWindowDetachedFromSnapGroup && _snapBindings.ContainsKey(_movingWindowHandle))
            {
                ViewModel.ResetSnapLayoutPreview();
                ViewModel.PreviewNodeId = null;
                EnsureSnapOverlayHidden();

                if (TryUpdateSnapLayoutFromWindowBounds(_movingWindowHandle))
                {
                    ReapplySnapBindings(skipWindowHandle: _movingWindowHandle);
                }

                return;
            }

            ViewModel.ResetSnapLayoutPreview();
            _hoveredSnapRegion = null;
            ViewModel.PreviewNodeId = null;
            EnsureSnapOverlayHidden();
            return;
        }

        var screenBounds = GetPrimaryScreenBounds();
        var visualStageBounds = GetSnapVisualStageBounds(screenBounds);
        var visualGeometry = _geometryCalculator.Compute(
            ViewModel.SnapLayoutDocument,
            visualStageBounds,
            SnapVisualSplitterThickness);
        var targetStageBounds = GetSnapTargetStageBounds(screenBounds);
        var targetGeometry = _geometryCalculator.Compute(
            ViewModel.SnapLayoutDocument,
            targetStageBounds,
            SnapTargetSplitterThickness);

        if (!TryGetCursorPosition(out var cursorInDevicePixels))
        {
            return;
        }

        var cursor = DevicePointToDip(cursorInDevicePixels);
        var hoveredVisualRegion = visualGeometry.Regions.FirstOrDefault(region => Contains(region.Bounds, cursor));
        _hoveredSnapRegion = hoveredVisualRegion is null
            ? null
            : targetGeometry.Regions.FirstOrDefault(region => region.NodeId == hoveredVisualRegion.NodeId);
        ViewModel.PreviewNodeId = hoveredVisualRegion?.NodeId;

        EnsureSnapOverlayVisible(screenBounds);
    }

    private void EnsureSnapOverlayVisible(PaneRect screenBounds)
    {
        _snapOverlayWindow ??= new SnapOverlayWindow
        {
            DataContext = ViewModel
        };

        _snapOverlayWindow.Left = screenBounds.X;
        _snapOverlayWindow.Top = screenBounds.Y;
        _snapOverlayWindow.Width = screenBounds.Width;
        _snapOverlayWindow.Height = screenBounds.Height;

        if (!_snapOverlayWindow.IsVisible)
        {
            _snapOverlayWindow.Show();
        }
    }

    private void EnsureSnapOverlayHidden()
    {
        if (_snapOverlayWindow?.IsVisible == true)
        {
            _snapOverlayWindow.Hide();
        }
    }

    private static PaneRect GetPrimaryScreenBounds()
    {
        return new PaneRect(
            0,
            0,
            SystemParameters.PrimaryScreenWidth,
            SystemParameters.PrimaryScreenHeight);
    }

    private static PaneRect GetSnapVisualStageBounds(PaneRect screenBounds)
    {
        return new PaneRect(
            screenBounds.X + SnapOverlayInset,
            screenBounds.Y + SnapOverlayInset,
            Math.Max(0, screenBounds.Width - (SnapOverlayInset * 2)),
            Math.Max(0, screenBounds.Height - (SnapOverlayInset * 2)));
    }

    private static PaneRect GetSnapTargetStageBounds(PaneRect screenBounds)
    {
        return screenBounds;
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

    private WpfPoint DevicePointToDip(WpfPoint point)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new WpfPoint(point.X / dpi.DpiScaleX, point.Y / dpi.DpiScaleY);
    }

    private PaneRect DipBoundsToDeviceBounds(PaneRect bounds)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new PaneRect(
            bounds.X * dpi.DpiScaleX,
            bounds.Y * dpi.DpiScaleY,
            bounds.Width * dpi.DpiScaleX,
            bounds.Height * dpi.DpiScaleY);
    }

    private PaneRect DeviceBoundsToDipBounds(PaneRect bounds)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new PaneRect(
            bounds.X / dpi.DpiScaleX,
            bounds.Y / dpi.DpiScaleY,
            bounds.Width / dpi.DpiScaleX,
            bounds.Height / dpi.DpiScaleY);
    }

    private bool TryUpdateSnapLayoutFromWindowBounds(IntPtr windowHandle)
    {
        if (!TryResolveSnapResize(windowHandle, out var resizeCandidate, out var clampedRatio))
        {
            return false;
        }

        ViewModel.UpdateSnapLayoutSplitRatio(resizeCandidate.Splitter.SplitNodeId, clampedRatio);
        return true;
    }

    private bool TryPreviewSnapLayoutFromWindowBounds(IntPtr windowHandle)
    {
        if (!TryResolveSnapResize(windowHandle, out var resizeCandidate, out var clampedRatio))
        {
            return false;
        }

        return ViewModel.PreviewSnapLayoutSplitRatio(resizeCandidate.Splitter.SplitNodeId, clampedRatio);
    }

    private bool TryResolveSnapResize(IntPtr windowHandle, out ResizeCandidate resizeCandidate, out double clampedRatio)
    {
        resizeCandidate = new ResizeCandidate(
            new ComputedSplitter(string.Empty, new PaneRect(), SplitDirection.Vertical, new PaneRect(), 0.5),
            SplitDirection.Vertical,
            usesLeadingEdge: false);
        clampedRatio = default;

        if (!_snapBindings.TryGetValue(windowHandle, out var nodeId)
            || !_workspaceApplyService.TryGetVisibleWindowBounds(windowHandle, out var visibleBounds))
        {
            return false;
        }

        var currentBounds = DeviceBoundsToDipBounds(visibleBounds);
        if (!HasMeaningfulResize(currentBounds))
        {
            return false;
        }

        var stageBounds = GetSnapTargetStageBounds(GetPrimaryScreenBounds());
        var geometry = _geometryCalculator.Compute(ViewModel.SnapLayoutDocument, stageBounds, SnapTargetSplitterThickness);
        var region = geometry.Regions.FirstOrDefault(item => item.NodeId == nodeId);
        if (region is null)
        {
            return false;
        }

        var splittersById = geometry.Splitters.ToDictionary(item => item.SplitNodeId, StringComparer.Ordinal);
        var matchedCandidate = FindResizeCandidate(
            ViewModel.SnapLayoutDocument.Root,
            nodeId,
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

    private bool IsResizeGesture(PaneRect? initialBounds)
    {
        if (initialBounds is null || !TryGetCursorPosition(out var cursorInDevicePixels))
        {
            return false;
        }

        var cursor = DevicePointToDip(cursorInDevicePixels);
        var bounds = initialBounds.Value;

        var nearLeft = Math.Abs(cursor.X - bounds.X) <= ResizeGripThreshold;
        var nearRight = Math.Abs(cursor.X - (bounds.X + bounds.Width)) <= ResizeGripThreshold;
        var nearTop = Math.Abs(cursor.Y - bounds.Y) <= ResizeGripThreshold;
        var nearBottom = Math.Abs(cursor.Y - (bounds.Y + bounds.Height)) <= ResizeGripThreshold;

        return nearLeft || nearRight || nearTop || nearBottom;
    }

    private void ReapplySnapBindings(IntPtr? skipWindowHandle = null)
    {
        if (_snapBindings.Count == 0)
        {
            return;
        }

        var stageBounds = GetSnapTargetStageBounds(GetPrimaryScreenBounds());
        var geometry = _geometryCalculator.Compute(ViewModel.SnapLayoutDocument, stageBounds, SnapTargetSplitterThickness);
        var regionsById = geometry.Regions.ToDictionary(item => item.NodeId, StringComparer.Ordinal);
        var deadHandles = new List<IntPtr>();

        foreach (var binding in _snapBindings)
        {
            if (skipWindowHandle.HasValue && binding.Key == skipWindowHandle.Value)
            {
                continue;
            }

            if (!regionsById.TryGetValue(binding.Value, out var region))
            {
                continue;
            }

            if (!_workspaceApplyService.TryGetVisibleWindowBounds(binding.Key, out _))
            {
                deadHandles.Add(binding.Key);
                continue;
            }

            _workspaceApplyService.SnapWindowToBounds(binding.Key, DipBoundsToDeviceBounds(region.Bounds));
        }

        foreach (var handle in deadHandles)
        {
            _snapBindings.Remove(handle);
        }
    }

    private ResizeCandidate? FindResizeCandidate(
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

        path.Add(new ResizeCandidate(splitter, split.Direction, usesLeadingEdge: false));
        var firstResult = FindResizeCandidate(split.First, targetNodeId, originalBounds, currentBounds, splittersById, path);
        path.RemoveAt(path.Count - 1);
        if (firstResult is not null)
        {
            return firstResult;
        }

        path.Add(new ResizeCandidate(splitter, split.Direction, usesLeadingEdge: true));
        var secondResult = FindResizeCandidate(split.Second, targetNodeId, originalBounds, currentBounds, splittersById, path);
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
        public ResizeCandidate(ComputedSplitter splitter, SplitDirection direction, bool usesLeadingEdge)
        {
            Splitter = splitter;
            Direction = direction;
            UsesLeadingEdge = usesLeadingEdge;
        }

        public ComputedSplitter Splitter { get; }

        public SplitDirection Direction { get; }

        public bool UsesLeadingEdge { get; }
    }

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

    private bool MatchesMinimizeShortcut(WpfKeyEventArgs e)
    {
        return ShortcutGestureHelper.MatchesKeyEvent(e, ViewModel.MinimizeShortcut);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenSettings();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);
}
