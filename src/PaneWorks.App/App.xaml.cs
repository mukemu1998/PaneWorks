using PaneWorks.App.Diagnostics;
using PaneWorks.App.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfInput = System.Windows.Input;
using WpfMedia = System.Windows.Media;
using WpfPrimitives = System.Windows.Controls.Primitives;

namespace PaneWorks.App;

public partial class App : Wpf.Application
{
    private Forms.NotifyIcon? _notifyIcon;
    private WpfControls.ContextMenu? _trayContextMenu;
    private readonly System.Windows.Threading.DispatcherTimer _trayMenuOutsideClickTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(50)
    };
    private IReadOnlyList<LayoutListItemViewModel> _cachedTrayLayouts = Array.Empty<LayoutListItemViewModel>();
    private IReadOnlyList<LayoutListItemViewModel> _cachedTrayWorkspaceProfiles = Array.Empty<LayoutListItemViewModel>();
    private string _cachedActiveSnapLayoutId = string.Empty;
    private string _cachedActiveWorkspaceProfileId = string.Empty;
    private MainWindow? _mainWindow;
    private DateTimeOffset _trayMenuOutsideClickIgnoreUntil;
    private bool _isExitRequested;
    private bool _isForceExitScheduled;

    public App()
    {
        _trayMenuOutsideClickTimer.Tick += TrayMenuOutsideClickTimer_Tick;
    }

    protected override void OnStartup(Wpf.StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = Wpf.ShutdownMode.OnExplicitShutdown;
        PaneWorksLog.Info("App startup");
        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;
        InitializeNotifyIcon();
        _mainWindow.Show();
    }

    protected override void OnExit(Wpf.ExitEventArgs e)
    {
        DisposeTrayResources();

        base.OnExit(e);
    }

    public bool IsExitRequested => _isExitRequested;

    public void CancelExitRequest()
    {
        _isExitRequested = false;
    }

    public void MinimizeMainWindowToTray()
    {
        if (_mainWindow is null)
        {
            return;
        }

        InvokeOnMainWindow(() =>
        {
            PaneWorksLog.Info("Minimize main window to tray");
            _mainWindow.ShowInTaskbar = false;
            _mainWindow.WindowState = Wpf.WindowState.Minimized;
            _mainWindow.Hide();
        });
    }

    public void RestoreMainWindowFromTray()
    {
        if (_mainWindow is null)
        {
            return;
        }

        BeginInvokeOnMainWindow(() =>
        {
            PaneWorksLog.Info("Restore main window from tray");
            _mainWindow.PrepareForTrayRestore();
            _mainWindow.ShowInTaskbar = true;
            _mainWindow.Show();
            _mainWindow.WindowState = Wpf.WindowState.Maximized;
            _mainWindow.Activate();
        });
    }

    private void InitializeNotifyIcon()
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "PaneWorks",
            Icon = LoadNotifyIcon(),
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) =>
        {
            PaneWorksLog.Info("Tray double click");
            RestoreMainWindowFromTray();
        };
        _notifyIcon.MouseUp += NotifyIcon_MouseUp;
        RefreshTraySnapshot();
    }

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

    private WpfControls.MenuItem CreateWpfLayoutsMenuItem()
    {
        var item = CreateWpfMenuItem("切换吸附布局", null);
        if (_mainWindow is null)
        {
            item.IsEnabled = false;
            return item;
        }

        var layouts = _cachedTrayLayouts;
        var activeLayoutId = _cachedActiveSnapLayoutId;

        if (layouts.Count == 0)
        {
            var emptyItem = CreateWpfMenuItem("暂无已保存分区布局", null);
            emptyItem.IsEnabled = false;
            item.Items.Add(emptyItem);
            return item;
        }

        foreach (var layout in layouts)
        {
            var layoutId = layout.Id;
            var layoutItem = CreateWpfMenuItem(layout.Name, () => SwitchSnapLayoutFromTray(layoutId));
            layoutItem.IsCheckable = true;
            layoutItem.IsChecked = string.Equals(layout.Id, activeLayoutId, StringComparison.OrdinalIgnoreCase);
            layoutItem.ToolTip = layout.Description;
            item.Items.Add(layoutItem);
        }

        return item;
    }

    private WpfControls.MenuItem CreateWpfWorkspaceProfilesMenuItem()
    {
        var item = CreateWpfMenuItem("切换并应用工作区方案", null);
        if (_mainWindow is null)
        {
            item.IsEnabled = false;
            return item;
        }

        var profiles = _cachedTrayWorkspaceProfiles;
        var activeProfileId = _cachedActiveWorkspaceProfileId;

        if (profiles.Count == 0)
        {
            var emptyItem = CreateWpfMenuItem("暂无已保存工作区方案", null);
            emptyItem.IsEnabled = false;
            item.Items.Add(emptyItem);
            return item;
        }

        foreach (var profile in profiles)
        {
            var profileId = profile.Id;
            var profileItem = CreateWpfMenuItem(profile.Name, () => SwitchWorkspaceProfileFromTray(profileId));
            profileItem.IsCheckable = true;
            profileItem.IsChecked = string.Equals(profile.Id, activeProfileId, StringComparison.OrdinalIgnoreCase);
            profileItem.ToolTip = profile.Description;
            item.Items.Add(profileItem);
        }

        return item;
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

    private void ExitFromTray()
    {
        if (_mainWindow is null)
        {
            _isExitRequested = true;
            ShutdownCompletely();
            return;
        }

        BeginInvokeOnMainWindow(() =>
        {
            PaneWorksLog.Info("Exit from tray");
            _isExitRequested = true;
            _mainWindow.PrepareForTrayRestore();
            _mainWindow.ShowInTaskbar = true;
            _mainWindow.Show();
            _mainWindow.WindowState = Wpf.WindowState.Maximized;
            _mainWindow.Close();

            if (_isExitRequested)
            {
                ShutdownCompletely();
            }
        });
    }

    private void ShutdownCompletely()
    {
        if (_isForceExitScheduled)
        {
            return;
        }

        _isForceExitScheduled = true;
        DisposeTrayResources();
        Shutdown();

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(500);
            Environment.Exit(0);
        });
    }

    private void DisposeTrayResources()
    {
        _trayMenuOutsideClickTimer.Stop();

        if (_trayContextMenu is not null)
        {
            _trayContextMenu.IsOpen = false;
            _trayContextMenu = null;
        }

        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
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

    private void RestartAsAdministrator()
    {
        BeginInvokeOnMainWindow(() =>
        {
            PaneWorksLog.Info("Restart as administrator requested");
            try
            {
                var processPath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(processPath) || !System.IO.File.Exists(processPath))
                {
                    Wpf.MessageBox.Show(
                        _mainWindow,
                        "没有找到当前 PaneWorks 程序路径，无法重新启动。",
                        "PaneWorks",
                        Wpf.MessageBoxButton.OK,
                        Wpf.MessageBoxImage.Warning);
                    return;
                }

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = processPath,
                    WorkingDirectory = AppContext.BaseDirectory,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                System.Diagnostics.Process.Start(startInfo);
                _isExitRequested = true;
                ShutdownCompletely();
            }
            catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223)
            {
                PaneWorksLog.Info("Restart as administrator canceled by user");
            }
            catch (Exception exception)
            {
                PaneWorksLog.Error("Restart as administrator failed", exception);
                Wpf.MessageBox.Show(
                    _mainWindow,
                    $"以管理员身份重新启动失败：{exception.Message}",
                    "PaneWorks",
                    Wpf.MessageBoxButton.OK,
                    Wpf.MessageBoxImage.Error);
            }
        });
    }

    private void TrayMenuOutsideClickTimer_Tick(object? sender, EventArgs e)
    {
        var menu = _trayContextMenu;
        if (menu is null || !menu.IsOpen)
        {
            _trayMenuOutsideClickTimer.Stop();
            return;
        }

        if (DateTimeOffset.UtcNow < _trayMenuOutsideClickIgnoreUntil || !IsAnyMouseButtonPressed())
        {
            return;
        }

        if (IsPointInsideMenuTree(menu, Forms.Control.MousePosition))
        {
            return;
        }

        PaneWorksLog.Info("WPF tray menu closed by outside click");
        menu.IsOpen = false;
    }

    private static bool IsAnyMouseButtonPressed()
    {
        return (Forms.Control.MouseButtons
            & (Forms.MouseButtons.Left | Forms.MouseButtons.Right | Forms.MouseButtons.Middle)) != Forms.MouseButtons.None;
    }

    private static bool IsPointInsideMenuTree(WpfControls.ContextMenu menu, Drawing.Point screenPoint)
    {
        if (IsScreenPointInsideElement(menu, screenPoint))
        {
            return true;
        }

        foreach (var item in EnumerateMenuItems(menu))
        {
            if (!item.IsSubmenuOpen)
            {
                continue;
            }

            if (item.Template.FindName("PART_Popup", item) is WpfPrimitives.Popup popup
                && popup.Child is Wpf.FrameworkElement child
                && IsScreenPointInsideElement(child, screenPoint))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<WpfControls.MenuItem> EnumerateMenuItems(WpfControls.ItemsControl owner)
    {
        foreach (var item in owner.Items.OfType<WpfControls.MenuItem>())
        {
            yield return item;

            foreach (var child in EnumerateMenuItems(item))
            {
                yield return child;
            }
        }
    }

    private static bool IsScreenPointInsideElement(Wpf.FrameworkElement element, Drawing.Point screenPoint)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        var topLeft = element.PointToScreen(new Wpf.Point(0, 0));
        var transformToDevice = Wpf.PresentationSource.FromVisual(element)?.CompositionTarget?.TransformToDevice
            ?? WpfMedia.Matrix.Identity;
        var width = element.ActualWidth * transformToDevice.M11;
        var height = element.ActualHeight * transformToDevice.M22;

        return screenPoint.X >= topLeft.X
            && screenPoint.X <= topLeft.X + width
            && screenPoint.Y >= topLeft.Y
            && screenPoint.Y <= topLeft.Y + height;
    }

    private void InvokeOnMainWindow(Action action)
    {
        if (_mainWindow is null)
        {
            return;
        }

        if (_mainWindow.Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _mainWindow.Dispatcher.Invoke(action);
    }

    private void BeginInvokeOnMainWindow(Action action)
    {
        if (_mainWindow is null)
        {
            return;
        }

        if (_mainWindow.Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _mainWindow.Dispatcher.BeginInvoke(action);
    }

    private static Drawing.Icon LoadNotifyIcon()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var icon = Drawing.Icon.ExtractAssociatedIcon(processPath);
                if (icon is not null)
                {
                    return (Drawing.Icon)icon.Clone();
                }
            }
        }
        catch
        {
        }

        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }
}
