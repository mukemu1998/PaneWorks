using PaneWorks.App.Diagnostics;
using Forms = System.Windows.Forms;
using Wpf = System.Windows;

namespace PaneWorks.App;

public partial class App
{
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

}
