using PaneWorks.App.Diagnostics;
using Drawing = System.Drawing;
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
