using System.Diagnostics;
using System.IO;
using System.Windows;
using PaneWorks.App.Diagnostics;
using PaneWorks.Infrastructure.Persistence;

namespace PaneWorks.App.Views;

public partial class SettingsDialog
{
    private void OpenConfigDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsPath = LayoutStoragePaths.GetDefaultAppSettingsFilePath();
        OpenDirectory(Path.GetDirectoryName(settingsPath));
    }

    private void OpenLayoutsDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        OpenDirectory(LayoutStoragePaths.GetDefaultLayoutsDirectory());
    }

    private void OpenLogsDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        OpenDirectory(PaneWorksLog.LogDirectoryPath);
    }

    private void OpenLastWorkspaceRestoreReportButton_Click(object sender, RoutedEventArgs e)
    {
        var reportPath = PaneWorksDiagnosticState.LastWorkspaceRestoreReportFilePath;
        if (!File.Exists(reportPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(
                reportPath,
                "暂无工作区恢复报告。请先应用一次工作区方案，再回到这里查看最近恢复结果。"
                + Environment.NewLine);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = reportPath,
            UseShellExecute = true
        });
    }

    private void CopyDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(PaneWorksDiagnosticReport.Build());
        StopRecording("诊断信息已复制到剪贴板。");
    }

    private void ClearDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = PaneMessageService.Show(
            this,
            "确定清理诊断日志和最近工作区恢复报告吗？分区布局、工作区方案和个人设置不会被删除。",
            "清理诊断文件",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmed != MessageBoxResult.Yes)
        {
            StopRecording("已取消清理诊断文件。");
            return;
        }

        PaneWorksDiagnosticState.ClearLastWorkspaceRestoreReport();
        PaneWorksLog.ClearLogs();
        StopRecording("诊断日志和最近恢复报告已清理。");
    }

    private static void OpenDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo
        {
            FileName = directory,
            UseShellExecute = true
        });
    }
}
