using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using PaneWorks.Infrastructure.Persistence;

namespace PaneWorks.App.Diagnostics;

public static class PaneWorksDiagnosticReport
{
    public static string Build()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "未知";

        return string.Join(Environment.NewLine, new[]
        {
            "PaneWorks 诊断信息",
            $"版本：{version}",
            $"进程：{Environment.ProcessPath ?? "未知"}",
            $"管理员权限：{(IsCurrentProcessElevated() ? "是" : "否")}",
            $"系统：{RuntimeInformation.OSDescription}",
            $".NET：{RuntimeInformation.FrameworkDescription}",
            $"架构：{RuntimeInformation.ProcessArchitecture}",
            $"配置文件：{LayoutStoragePaths.GetDefaultAppSettingsFilePath()}",
            $"分区布局目录：{LayoutStoragePaths.GetDefaultLayoutsDirectory()}",
            $"工作区方案目录：{LayoutStoragePaths.GetDefaultWorkspaceProfilesDirectory()}",
            $"会话状态文件：{LayoutStoragePaths.GetDefaultSessionStateFilePath()}",
            $"日志目录：{PaneWorksLog.LogDirectoryPath}",
            $"日志文件：{PaneWorksLog.LogFilePath}",
            $"上一轮日志：{PaneWorksLog.PreviousLogFilePath}",
            $"最近工作区恢复报告：{PaneWorksDiagnosticState.LastWorkspaceRestoreReportFilePath}",
            $"诊断生成时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}"
        }.Concat(BuildOptionalSections()));
    }

    private static IEnumerable<string> BuildOptionalSections()
    {
        var lastWorkspaceRestoreReport = PaneWorksDiagnosticState.GetLastWorkspaceRestoreReport();
        if (!string.IsNullOrWhiteSpace(lastWorkspaceRestoreReport))
        {
            yield return string.Empty;
            yield return lastWorkspaceRestoreReport;
        }
    }

    private static bool IsCurrentProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
