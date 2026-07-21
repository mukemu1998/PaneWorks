using System.Runtime.InteropServices;
using System.Windows.Interop;
using PaneWorks.App.Diagnostics;
using Forms = System.Windows.Forms;
using Wpf = System.Windows;
using WpfDispatcherPriority = System.Windows.Threading.DispatcherPriority;

namespace PaneWorks.App;

public partial class App
{
    private const int SwRestore = 9;

    public void ExitForUpdate()
    {
        _isExitRequested = true;
        ShutdownCompletely();
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
            var wasHidden = !_mainWindow.IsVisible;
            _mainWindow.PrepareForTrayRestore();
            _mainWindow.ShowInTaskbar = true;
            if (wasHidden)
            {
                _mainWindow.Show();
            }

            _mainWindow.WindowState = Wpf.WindowState.Normal;
            _mainWindow.CompleteMainWindowPresentation();
            _mainWindow.Activate();
            _mainWindow.Dispatcher.BeginInvoke(() =>
            {
                _mainWindow.ShowInTaskbar = true;
                _mainWindow.WindowState = Wpf.WindowState.Normal;
                _mainWindow.CompleteMainWindowPresentation();
                _mainWindow.Activate();
                _mainWindow.Focus();
                RestoreNativeWindow(_mainWindow);
            }, WpfDispatcherPriority.Render);
        });
    }

    private void InitializeNotifyIcon()
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = $"PaneWorks v{GetTrayVersionLabel()}",
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

    private static string GetTrayVersionLabel()
    {
        var version = typeof(App).Assembly.GetName().Version;
        return version is null
            ? "0.0.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static void RestoreNativeWindow(Wpf.Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _ = ShowWindow(handle, SwRestore);
        _ = SetForegroundWindow(handle);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

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
            _mainWindow.Opacity = 1;
            _mainWindow.UpdateLayout();
            _mainWindow.Activate();
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

}
