using PaneWorks.App.Diagnostics;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfInput = System.Windows.Input;
using WpfMedia = System.Windows.Media;
using WpfPrimitives = System.Windows.Controls.Primitives;

namespace PaneWorks.App;

public partial class App
{
    private void NotifyIcon_MouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button != Forms.MouseButtons.Right)
        {
            return;
        }

        PaneWorksLog.Info("Tray right click");
        BeginInvokeOnMainWindow(ShowTrayContextMenu);
    }

    private void ShowTrayContextMenu()
    {
        PaneWorksLog.Info("WPF tray menu opening");
        RefreshTraySnapshot();

        if (_trayContextMenu is not null)
        {
            _trayContextMenu.IsOpen = false;
        }

        var mousePosition = Forms.Control.MousePosition;
        var menuPoint = ToDipScreenPoint(mousePosition.X, mousePosition.Y);
        _trayContextMenu = CreateWpfTrayMenu(menuPoint.X, menuPoint.Y);
        _trayMenuOutsideClickIgnoreUntil = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(250);
        _trayContextMenu.IsOpen = true;
        _trayContextMenu.Dispatcher.BeginInvoke(() =>
        {
            if (_trayContextMenu?.IsOpen == true)
            {
                _trayContextMenu.Focus();
                WpfInput.Keyboard.Focus(_trayContextMenu);
                _trayMenuOutsideClickTimer.Start();
            }
        });
        PaneWorksLog.Info("WPF tray menu opened");
    }

    private WpfControls.ContextMenu CreateWpfTrayMenu(double x, double y)
    {
        var menu = new WpfControls.ContextMenu
        {
            Placement = WpfPrimitives.PlacementMode.AbsolutePoint,
            PlacementTarget = _mainWindow,
            HorizontalOffset = x,
            VerticalOffset = y,
            MinWidth = 230,
            Focusable = true,
            StaysOpen = false,
            Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(21, 27, 42)),
            Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(248, 251, 255)),
            BorderBrush = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(78, 87, 103)),
            BorderThickness = new Wpf.Thickness(1)
        };

        menu.Items.Add(CreateWpfMenuItem("打开 PaneWorks", RestoreMainWindowFromTray, bold: true));
        menu.Closed += (_, _) =>
        {
            _trayMenuOutsideClickTimer.Stop();
            if (ReferenceEquals(_trayContextMenu, menu))
            {
                _trayContextMenu = null;
            }

            PaneWorksLog.Info("WPF tray menu closed");
        };

        menu.Items.Add(CreateWpfLayoutsMenuItem());
        menu.Items.Add(CreateWpfWorkspaceProfilesMenuItem());
        menu.Items.Add(new WpfControls.Separator());
        menu.Items.Add(CreateWpfMenuItem("以管理员身份重新启动", RestartAsAdministrator));
        menu.Items.Add(CreateWpfMenuItem("退出", ExitFromTray));
        return menu;
    }

    private Wpf.Point ToDipScreenPoint(int x, int y)
    {
        var transform = _mainWindow is null
            ? WpfMedia.Matrix.Identity
            : Wpf.PresentationSource.FromVisual(_mainWindow)?.CompositionTarget?.TransformFromDevice
                ?? WpfMedia.Matrix.Identity;

        return transform.Transform(new Wpf.Point(x, y));
    }

    private static WpfControls.MenuItem CreateWpfMenuItem(string text, Action? onClick, bool bold = false)
    {
        var item = new WpfControls.MenuItem
        {
            Header = text,
            Padding = new Wpf.Thickness(14, 8, 14, 8),
            FontFamily = new WpfMedia.FontFamily("Microsoft YaHei UI"),
            FontSize = bold ? 14 : 13,
            FontWeight = bold ? Wpf.FontWeights.Bold : Wpf.FontWeights.Normal,
            Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(248, 251, 255)),
            Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(21, 27, 42))
        };

        if (onClick is not null)
        {
            item.Click += (_, _) => onClick();
        }

        return item;
    }

    private void RefreshTraySnapshot()
    {
        if (_mainWindow is null)
        {
            return;
        }

        if (!_mainWindow.Dispatcher.CheckAccess())
        {
            BeginInvokeOnMainWindow(RefreshTraySnapshot);
            return;
        }

        _cachedTrayLayouts = _mainWindow.GetTrayLayoutItems();
        _cachedTrayWorkspaceProfiles = _mainWindow.GetTrayWorkspaceProfileItems();
        _cachedActiveSnapLayoutId = _mainWindow.GetActiveSnapLayoutId();
        _cachedActiveWorkspaceProfileId = _mainWindow.GetActiveWorkspaceProfileId();
    }

    private void SwitchSnapLayoutFromTray(string layoutId)
    {
        BeginInvokeOnMainWindow(() =>
        {
            PaneWorksLog.Info($"Switch snap layout from tray: {layoutId}");
            _mainWindow?.SwitchSnapLayoutFromTray(layoutId);
            RefreshTraySnapshot();
        });
    }

    private void SwitchWorkspaceProfileFromTray(string profileId)
    {
        BeginInvokeOnMainWindow(() =>
        {
            PaneWorksLog.Info($"Switch workspace profile from tray: {profileId}");
            _mainWindow?.SwitchWorkspaceProfileFromTray(profileId);
            RefreshTraySnapshot();
        });
    }

}
