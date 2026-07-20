using System.Windows;
using PaneWorks.App.Controls;
using PaneWorks.App.Diagnostics;
using PaneWorks.Core.Models;
using PaneWorks.Core.Services;
using PaneWorks.Infrastructure.Windows;
using WpfPoint = System.Windows.Point;

namespace PaneWorks.App;

public partial class MainWindow
{
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
        var selectedWorkAreaDipBounds = DeviceRectToDipRect(selectedDisplay.WorkArea);
        EditorCanvas.WorkAreaBounds = new PaneRect(
            selectedWorkAreaDipBounds.X - _virtualDesktopBounds.X,
            selectedWorkAreaDipBounds.Y - _virtualDesktopBounds.Y,
            selectedWorkAreaDipBounds.Width,
            selectedWorkAreaDipBounds.Height);
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

}
