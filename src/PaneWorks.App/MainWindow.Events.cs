using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using PaneWorks.App.Controls;
using PaneWorks.App.Diagnostics;
using PaneWorks.App.ViewModels;
using WpfApplication = System.Windows.Application;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfTextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace PaneWorks.App;

public partial class MainWindow
{
    private const int StartupDisplayReconcileAttempts = 180;
    private const int StartupDisplayStableSamplesRequired = 3;
    private static readonly TimeSpan StartupDisplayReconcileInitialDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StartupDisplayReconcileRetryDelay = TimeSpan.FromSeconds(1);
    private string? _lastStartupDisplayTopologyFingerprint;
    private int _startupDisplayStableSamples;

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        EnsureMainWindowCoversVirtualDesktop();
        UpdateWorkbenchPanelPosition();
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

    private void EditorCanvas_DisplayEditRequested(object? sender, DisplayEditRequestedEventArgs e)
    {
        ViewModel.SelectDisplayForLayoutEditing(e.DisplayId);
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

        // Runtime insertion is deliberately process-local and must never survive a real exit.
        ClearSnapRuntimeCollections();

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

    private bool MatchesMinimizeShortcut(WpfKeyEventArgs e)
    {
        return ShortcutGestureHelper.MatchesKeyEvent(e, ViewModel.MinimizeShortcut);
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
        _ = ReconcileStartupDisplaysAsync();
        Dispatcher.BeginInvoke(
            () => Opacity = 1,
            DispatcherPriority.Render);
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

    public void CompleteMainWindowPresentation()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(CompleteMainWindowPresentation);
            return;
        }

        EnsureMainWindowCoversVirtualDesktop();
        UpdateEditorStageBounds();
        UpdateWorkbenchPanelPosition();
        Dispatcher.BeginInvoke(
            () => Opacity = 1,
            DispatcherPriority.Render);
    }

    private async Task ReconcileStartupDisplaysAsync()
    {
        for (var attempt = 0; attempt < StartupDisplayReconcileAttempts; attempt++)
        {
            await Task.Delay(attempt == 0
                ? StartupDisplayReconcileInitialDelay
                : StartupDisplayReconcileRetryDelay);

            if (!IsLoaded || !IsVisible)
            {
                return;
            }

            if (ViewModel.IsLayoutEditMode || ViewModel.IsWorkspaceBindingMode || ViewModel.IsDirty)
            {
                return;
            }

            var topologyFingerprint = GetCurrentDisplayTopologyFingerprint();
            if (!string.Equals(
                    topologyFingerprint,
                    _lastStartupDisplayTopologyFingerprint,
                    StringComparison.Ordinal))
            {
                _lastStartupDisplayTopologyFingerprint = topologyFingerprint;
                _startupDisplayStableSamples = 1;
                continue;
            }

            _startupDisplayStableSamples++;
            if (_startupDisplayStableSamples < StartupDisplayStableSamplesRequired)
            {
                continue;
            }

            if (!ViewModel.ReconcileDisplaysAfterStartup())
            {
                continue;
            }

            UpdateEditorStageBounds();
            UpdateWorkbenchPanelPosition();
            PaneWorksLog.Info($"Startup display reconciliation applied on attempt {attempt + 1}");
        }
    }

    private string GetCurrentDisplayTopologyFingerprint()
    {
        return string.Join(
            ";",
            _displayDiscoveryService
                .GetDisplays()
                .OrderBy(display => display.Id, StringComparer.OrdinalIgnoreCase)
                .Select(display => string.Join(
                    "|",
                    display.Id,
                    display.Bounds.X,
                    display.Bounds.Y,
                    display.Bounds.Width,
                    display.Bounds.Height,
                    display.IsPrimary,
                    display.Orientation)));
    }
}
