using PaneWorks.App.Views;
using PaneWorks.Infrastructure.Persistence;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    public string AppVersionLabel => $"v{GetApplicationVersion()}";

    public void OpenSettings()
    {
        var dialog = new SettingsDialog(_appSettings);
        PaneMessageService.PrepareDialog(dialog);

        if (dialog.ShowDialog() != true || dialog.Result is null)
        {
            return;
        }

        var updatedSettings = NormalizeAppSettings(new AppSettings(
            dialog.Result.SnapModifierKey,
            dialog.Result.RuntimeSessionModifierKey,
            dialog.Result.MinimizeShortcut,
            dialog.Result.LaunchAtStartup,
            dialog.Result.AutoCheckForUpdates));

        try
        {
            _startupRegistrationService.SetEnabled(updatedSettings.LaunchAtStartup);
            _appSettings = updatedSettings with
            {
                LaunchAtStartup = _startupRegistrationService.IsEnabled()
            };

            _appSettingsRepository.Save(_appSettings);
            RaisePropertyChanged(nameof(SnapModifierKey));
            RaisePropertyChanged(nameof(RuntimeSessionModifierKey));
            RaisePropertyChanged(nameof(MinimizeShortcut));
            RaisePropertyChanged(nameof(ShortcutSummary));
            ShowInfoMessage("设置已保存，新的快捷键和开机自启状态已经生效。");
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"保存设置失败：{ex.Message}");
        }
    }

    public void OpenAbout()
    {
        var dialog = new AboutDialog(AppVersionLabel);
        PaneMessageService.PrepareDialog(dialog);

        _ = dialog.ShowDialog();
    }

    private AppSettings LoadAppSettings()
    {
        var savedSettings = NormalizeAppSettings(_appSettingsRepository.Load());
        var actualStartupState = _startupRegistrationService.IsEnabled();
        var normalizedSettings = savedSettings with
        {
            LaunchAtStartup = actualStartupState
        };

        if (!Equals(savedSettings, normalizedSettings))
        {
            _appSettingsRepository.Save(normalizedSettings);
        }

        return normalizedSettings;
    }

    private static AppSettings NormalizeAppSettings(AppSettings settings)
    {
        var snapModifier = ShortcutGestureHelper.NormalizeShortcut(
            settings.SnapModifierKey,
            ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.SnapModifierKey, "Shift"));
        var runtimeSessionModifier = ShortcutGestureHelper.NormalizeShortcut(
            settings.RuntimeSessionModifierKey,
            ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.RuntimeSessionModifierKey, "Ctrl + Shift"));
        var minimizeShortcut = ShortcutGestureHelper.NormalizeShortcut(
            settings.MinimizeShortcut,
            ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.MinimizeShortcut, "Esc"));

        return settings with
        {
            SnapModifierKey = snapModifier,
            RuntimeSessionModifierKey = runtimeSessionModifier,
            MinimizeShortcut = minimizeShortcut
        };
    }

    public bool AutoCheckForUpdates => _appSettings.AutoCheckForUpdates;

    private static string GetApplicationVersion()
    {
        var version = typeof(MainViewModel).Assembly.GetName().Version;
        return version is null
            ? "0.0.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
