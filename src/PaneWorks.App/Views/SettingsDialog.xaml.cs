using System.Windows.Input;
using PaneWorks.Infrastructure.Persistence;

namespace PaneWorks.App.Views;

public partial class SettingsDialog : System.Windows.Window
{
    private RecordingTarget _recordingTarget;
    private string _snapShortcut;
    private string _minimizeShortcut;

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();

        _snapShortcut = ShortcutGestureHelper.NormalizeShortcut(
            settings.SnapModifierKey,
            ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.SnapModifierKey, "Shift"));
        _minimizeShortcut = ShortcutGestureHelper.NormalizeShortcut(
            settings.MinimizeShortcut,
            ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.MinimizeShortcut, "Esc"));

        SnapModifierTextBox.Text = _snapShortcut;
        MinimizeShortcutTextBox.Text = _minimizeShortcut;
        LaunchAtStartupCheckBox.IsChecked = settings.LaunchAtStartup;
    }

    public SettingsDialogResult? Result { get; private set; }

    private void RecordSnapShortcutButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        StartRecording(RecordingTarget.SnapTrigger);
    }

    private void RecordMinimizeShortcutButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        StartRecording(RecordingTarget.Minimize);
    }

    private void RestoreDefaultsButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _snapShortcut = ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.SnapModifierKey, "Shift");
        _minimizeShortcut = ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.MinimizeShortcut, "Esc");
        SnapModifierTextBox.Text = _snapShortcut;
        MinimizeShortcutTextBox.Text = _minimizeShortcut;
        StopRecording("已恢复默认快捷键。");
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_recordingTarget == RecordingTarget.None)
        {
            return;
        }

        e.Handled = true;
        var captured = ShortcutGestureHelper.CaptureFromKeyEvent(e);
        if (string.IsNullOrWhiteSpace(captured))
        {
            return;
        }

        if (_recordingTarget == RecordingTarget.SnapTrigger)
        {
            _snapShortcut = captured;
            SnapModifierTextBox.Text = captured;
            StopRecording($"吸附触发键已录制为：{captured}");
            return;
        }

        _minimizeShortcut = captured;
        MinimizeShortcutTextBox.Text = captured;
        StopRecording($"最小化快捷键已录制为：{captured}");
    }

    private void ConfirmButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var snapShortcut = ShortcutGestureHelper.NormalizeShortcut(
            _snapShortcut,
            ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.SnapModifierKey, "Shift"));
        var minimizeShortcut = ShortcutGestureHelper.NormalizeShortcut(
            _minimizeShortcut,
            ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.MinimizeShortcut, "Esc"));
        var launchAtStartup = LaunchAtStartupCheckBox.IsChecked == true;

        Result = new SettingsDialogResult(snapShortcut, minimizeShortcut, launchAtStartup);
        DialogResult = true;
    }

    private void StartRecording(RecordingTarget target)
    {
        _recordingTarget = target;
        RecordingHintTextBlock.Text = target == RecordingTarget.SnapTrigger
            ? "正在录制吸附触发键：现在直接按下组合键。单独按 Shift、Ctrl、Alt 也可以。"
            : "正在录制最小化快捷键：现在直接按下组合键。";

        if (target == RecordingTarget.SnapTrigger)
        {
            RecordSnapShortcutButton.Content = "录制中...";
            RecordMinimizeShortcutButton.Content = "开始录制";
        }
        else
        {
            RecordSnapShortcutButton.Content = "开始录制";
            RecordMinimizeShortcutButton.Content = "录制中...";
        }

        Focus();
    }

    private void StopRecording(string hint)
    {
        _recordingTarget = RecordingTarget.None;
        RecordSnapShortcutButton.Content = "开始录制";
        RecordMinimizeShortcutButton.Content = "开始录制";
        RecordingHintTextBlock.Text = hint;
    }

    private enum RecordingTarget
    {
        None,
        SnapTrigger,
        Minimize
    }
}

public sealed record SettingsDialogResult(string SnapModifierKey, string MinimizeShortcut, bool LaunchAtStartup);
