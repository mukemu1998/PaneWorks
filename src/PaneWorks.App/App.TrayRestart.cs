using PaneWorks.App.Diagnostics;
using PaneWorks.App.Views;
using Wpf = System.Windows;

namespace PaneWorks.App;

public partial class App
{
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
                    PaneMessageService.Show(
                        _mainWindow,
                        "没有找到当前 PaneWorks 程序路径，无法重新启动。",
                        buttons: Wpf.MessageBoxButton.OK,
                        image: Wpf.MessageBoxImage.Warning);
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
                PaneMessageService.Show(
                    _mainWindow,
                    $"以管理员身份重新启动失败：{exception.Message}",
                    buttons: Wpf.MessageBoxButton.OK,
                    image: Wpf.MessageBoxImage.Error);
            }
        });
    }
}
