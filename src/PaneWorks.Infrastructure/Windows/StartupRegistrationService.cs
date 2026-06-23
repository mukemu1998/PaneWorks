using Microsoft.Win32;
using System.Runtime.Versioning;

namespace PaneWorks.Infrastructure.Windows;

[SupportedOSPlatform("windows")]
public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PaneWorks";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return !string.IsNullOrWhiteSpace(key?.GetValue(ValueName) as string);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开开机自启注册表项。");

        if (enabled)
        {
            key.SetValue(ValueName, GetLaunchCommand(), RegistryValueKind.String);
            return;
        }

        if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string GetLaunchCommand()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("无法确定 PaneWorks 的启动路径。");
        }

        return $"\"{executablePath}\"";
    }
}
