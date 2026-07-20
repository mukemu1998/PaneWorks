using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PaneWorks.Infrastructure.Persistence;

namespace PaneWorks.App.Views;

public partial class SettingsDialog : System.Windows.Window
{
    private RecordingTarget _recordingTarget;
    private string _snapShortcut;
    private string _runtimeSessionShortcut;
    private string _minimizeShortcut;
    private readonly string _initialSnapShortcut;
    private readonly string _initialRuntimeSessionShortcut;
    private readonly string _initialMinimizeShortcut;
    private readonly bool _initialLaunchAtStartup;
    private readonly bool _initialLaunchElevatedAtStartup;
    private readonly bool _initialAutoCheckForUpdates;
    private bool _isInitializing;

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        _isInitializing = true;

        _snapShortcut = ShortcutGestureHelper.NormalizeShortcut(
            settings.SnapModifierKey,
            ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.SnapModifierKey, "Shift"));
        _runtimeSessionShortcut = ShortcutGestureHelper.NormalizeShortcut(
            settings.RuntimeSessionModifierKey,
            ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.RuntimeSessionModifierKey, "Ctrl + Shift"));
        _minimizeShortcut = ShortcutGestureHelper.NormalizeShortcut(
            settings.MinimizeShortcut,
            ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.MinimizeShortcut, "Esc"));
        _initialSnapShortcut = _snapShortcut;
        _initialRuntimeSessionShortcut = _runtimeSessionShortcut;
        _initialMinimizeShortcut = _minimizeShortcut;
        _initialLaunchAtStartup = settings.LaunchAtStartup;
        _initialLaunchElevatedAtStartup = settings.LaunchAtStartup && settings.LaunchElevatedAtStartup;
        _initialAutoCheckForUpdates = settings.AutoCheckForUpdates;

        SnapModifierTextBox.Text = _snapShortcut;
        RuntimeSessionShortcutTextBox.Text = _runtimeSessionShortcut;
        MinimizeShortcutTextBox.Text = _minimizeShortcut;
        LaunchAtStartupCheckBox.IsChecked = settings.LaunchAtStartup;
        LaunchElevatedAtStartupCheckBox.IsChecked = settings.LaunchElevatedAtStartup;
        UpdateElevatedStartupOption();
        AutoCheckForUpdatesCheckBox.IsChecked = settings.AutoCheckForUpdates;
        _isInitializing = false;
        UpdateSaveButtonState();
    }

    public SettingsDialogResult? Result { get; private set; }

    private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Close();
    }

    private void HeaderDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1 || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (IsButtonSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        DragMove();
        e.Handled = true;
    }

    private void ConfirmButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var snapShortcut = ShortcutGestureHelper.NormalizeShortcut(
            _snapShortcut,
            ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.SnapModifierKey, "Shift"));
        var runtimeSessionShortcut = ShortcutGestureHelper.NormalizeShortcut(
            _runtimeSessionShortcut,
            ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.RuntimeSessionModifierKey, "Ctrl + Shift"));
        var minimizeShortcut = ShortcutGestureHelper.NormalizeShortcut(
            _minimizeShortcut,
            ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.MinimizeShortcut, "Esc"));
        var launchAtStartup = LaunchAtStartupCheckBox.IsChecked == true;
        var launchElevatedAtStartup = launchAtStartup && LaunchElevatedAtStartupCheckBox.IsChecked == true;
        var autoCheckForUpdates = AutoCheckForUpdatesCheckBox.IsChecked == true;

        Result = new SettingsDialogResult(snapShortcut, runtimeSessionShortcut, minimizeShortcut, launchAtStartup, autoCheckForUpdates, launchElevatedAtStartup);
        DialogResult = true;
    }

    private void LaunchAtStartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateElevatedStartupOption();
        UpdateSaveButtonState();
    }

    private void SettingsInputChanged(object sender, RoutedEventArgs e)
    {
        UpdateSaveButtonState();
    }

    private void UpdateElevatedStartupOption()
    {
        var isEnabled = LaunchAtStartupCheckBox.IsChecked == true;
        LaunchElevatedAtStartupCheckBox.IsEnabled = isEnabled;
        if (!isEnabled)
        {
            LaunchElevatedAtStartupCheckBox.IsChecked = false;
        }
    }

    private void UpdateSaveButtonState()
    {
        if (_isInitializing || SaveSettingsButton is null)
        {
            return;
        }

        var launchAtStartup = LaunchAtStartupCheckBox.IsChecked == true;
        var launchElevatedAtStartup = launchAtStartup && LaunchElevatedAtStartupCheckBox.IsChecked == true;
        var autoCheckForUpdates = AutoCheckForUpdatesCheckBox.IsChecked == true;
        var hasChanges = !string.Equals(_snapShortcut, _initialSnapShortcut, StringComparison.Ordinal)
            || !string.Equals(_runtimeSessionShortcut, _initialRuntimeSessionShortcut, StringComparison.Ordinal)
            || !string.Equals(_minimizeShortcut, _initialMinimizeShortcut, StringComparison.Ordinal)
            || launchAtStartup != _initialLaunchAtStartup
            || launchElevatedAtStartup != _initialLaunchElevatedAtStartup
            || autoCheckForUpdates != _initialAutoCheckForUpdates;

        SaveSettingsButton.IsEnabled = hasChanges;
    }

    private static bool IsButtonSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

}

public sealed record SettingsDialogResult(
    string SnapModifierKey,
    string RuntimeSessionModifierKey,
    string MinimizeShortcut,
    bool LaunchAtStartup,
    bool AutoCheckForUpdates,
    bool LaunchElevatedAtStartup);
