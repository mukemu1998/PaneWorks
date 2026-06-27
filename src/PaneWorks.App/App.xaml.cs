using PaneWorks.App.Diagnostics;
using PaneWorks.App.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfMedia = System.Windows.Media;
using WpfPrimitives = System.Windows.Controls.Primitives;

namespace PaneWorks.App;

public partial class App : Wpf.Application
{
    private Forms.NotifyIcon? _notifyIcon;
    private WpfControls.ContextMenu? _trayContextMenu;
    private IReadOnlyList<LayoutListItemViewModel> _cachedTrayLayouts = Array.Empty<LayoutListItemViewModel>();
    private string _cachedActiveSnapLayoutId = string.Empty;
    private MainWindow? _mainWindow;
    private bool _isExitRequested;

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
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        if (_trayContextMenu is not null)
        {
            _trayContextMenu.IsOpen = false;
            _trayContextMenu = null;
        }

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
        _trayContextMenu.IsOpen = true;
        PaneWorksLog.Info("WPF tray menu opened");
    }

    private WpfControls.ContextMenu CreateWpfTrayMenu(double x, double y)
    {
        var menu = new WpfControls.ContextMenu
        {
            Placement = WpfPrimitives.PlacementMode.AbsolutePoint,
            HorizontalOffset = x,
            VerticalOffset = y,
            MinWidth = 230,
            Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(21, 27, 42)),
            Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(248, 251, 255)),
            BorderBrush = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(78, 87, 103)),
            BorderThickness = new Wpf.Thickness(1)
        };

        menu.Items.Add(CreateWpfMenuItem("打开 PaneWorks", RestoreMainWindowFromTray, bold: true));
        menu.Items.Add(CreateWpfLayoutsMenuItem());
        menu.Items.Add(new WpfControls.Separator());
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
        var item = CreateWpfMenuItem("快速切换吸附布局", null);
        if (_mainWindow is null)
        {
            item.IsEnabled = false;
            return item;
        }

        var layouts = _cachedTrayLayouts;
        var activeLayoutId = _cachedActiveSnapLayoutId;

        if (layouts.Count == 0)
        {
            var emptyItem = CreateWpfMenuItem("暂无已保存布局", null);
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
            Shutdown();
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
                Shutdown();
            }
        });
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
        _cachedActiveSnapLayoutId = _mainWindow.GetActiveSnapLayoutId();
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
