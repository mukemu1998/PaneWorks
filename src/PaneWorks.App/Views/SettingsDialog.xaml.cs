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

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();

        _snapShortcut = ShortcutGestureHelper.NormalizeShortcut(
            settings.SnapModifierKey,
            ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.SnapModifierKey, "Shift"));
        _runtimeSessionShortcut = ShortcutGestureHelper.NormalizeShortcut(
            settings.RuntimeSessionModifierKey,
            ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.RuntimeSessionModifierKey, "Ctrl + Shift"));
        _minimizeShortcut = ShortcutGestureHelper.NormalizeShortcut(
            settings.MinimizeShortcut,
            ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.MinimizeShortcut, "Esc"));

        SnapModifierTextBox.Text = _snapShortcut;
        RuntimeSessionShortcutTextBox.Text = _runtimeSessionShortcut;
        MinimizeShortcutTextBox.Text = _minimizeShortcut;
        LaunchAtStartupCheckBox.IsChecked = settings.LaunchAtStartup;
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

        Result = new SettingsDialogResult(snapShortcut, runtimeSessionShortcut, minimizeShortcut, launchAtStartup);
        DialogResult = true;
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
    bool LaunchAtStartup);
