using Microsoft.Win32;
using System.Diagnostics;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace PaneWorks.Infrastructure.Windows;

[SupportedOSPlatform("windows")]
public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PaneWorks";
    private const string ElevatedTaskName = "PaneWorks Elevated Startup";

    public bool IsEnabled()
    {
        return IsRegistryStartupEnabled() || IsElevatedStartupEnabled();
    }

    public bool IsElevatedStartupEnabled()
    {
        return RunSchtasks("/Query", "/TN", ElevatedTaskName) == 0;
    }

    public void SetEnabled(bool enabled, bool elevated = false)
    {
        if (!enabled)
        {
            SetRegistryStartupEnabled(false);
            DeleteElevatedTask();
            return;
        }

        if (!elevated)
        {
            DeleteElevatedTask();
            SetRegistryStartupEnabled(true);
            return;
        }

        if (!IsCurrentProcessElevated())
        {
            throw new InvalidOperationException("管理员开机自启需要先从托盘菜单选择“以管理员身份重新启动”，再保存设置。");
        }

        SetRegistryStartupEnabled(false);
        CreateElevatedTask();
    }

    private static bool IsRegistryStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return !string.IsNullOrWhiteSpace(key?.GetValue(ValueName) as string);
    }

    private static void SetRegistryStartupEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开开机自启注册表项。");

        if (enabled)
        {
            key.SetValue(ValueName, GetLaunchCommand(), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static void CreateElevatedTask()
    {
        var exitCode = RunSchtasks(
            "/Create",
            "/TN", ElevatedTaskName,
            "/TR", $"\"{GetExecutablePath()}\"",
            "/SC", "ONLOGON",
            "/RL", "HIGHEST",
            "/F");
        if (exitCode != 0)
        {
            throw new InvalidOperationException("无法创建管理员开机自启任务。请确认 PaneWorks 正在以管理员身份运行。");
        }
    }

    private static void DeleteElevatedTask()
    {
        _ = RunSchtasks("/Delete", "/TN", ElevatedTaskName, "/F");
    }

    private static int RunSchtasks(params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            process?.WaitForExit();
            return process?.ExitCode ?? -1;
        }
        catch
        {
            return -1;
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

    private static string GetLaunchCommand()
        => $"\"{GetExecutablePath()}\"";

    private static string GetExecutablePath()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("无法确定 PaneWorks 的启动路径。");
        }
        return executablePath;
    }
}
