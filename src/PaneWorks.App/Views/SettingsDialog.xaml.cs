using System.Windows.Input;
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

    private void RecordSnapShortcutButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        StartRecording(RecordingTarget.SnapTrigger);
    }

    private void RecordMinimizeShortcutButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        StartRecording(RecordingTarget.Minimize);
    }

    private void RecordRuntimeSessionShortcutButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        StartRecording(RecordingTarget.RuntimeSession);
    }

    private void RestoreDefaultsButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _snapShortcut = ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.SnapModifierKey, "Shift");
        _runtimeSessionShortcut = ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.RuntimeSessionModifierKey, "Ctrl + Shift");
        _minimizeShortcut = ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.MinimizeShortcut, "Esc");
        SnapModifierTextBox.Text = _snapShortcut;
        RuntimeSessionShortcutTextBox.Text = _runtimeSessionShortcut;
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
            StopRecording($"用户分区吸附键已录制为：{captured}");
            return;
        }

        if (_recordingTarget == RecordingTarget.RuntimeSession)
        {
            _runtimeSessionShortcut = captured;
            RuntimeSessionShortcutTextBox.Text = captured;
            StopRecording($"临时调整区吸附键已录制为：{captured}");
            return;
        }

        _minimizeShortcut = captured;
        MinimizeShortcutTextBox.Text = captured;
        StopRecording($"收进托盘快捷键已录制为：{captured}");
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

    private void StartRecording(RecordingTarget target)
    {
        _recordingTarget = target;
        RecordingHintTextBlock.Text = target switch
        {
            RecordingTarget.SnapTrigger => "正在录制用户分区吸附键：现在直接按下组合键。单独按 Shift、Ctrl、Alt 也可以。",
            RecordingTarget.RuntimeSession => "正在录制临时调整区吸附键：建议使用 Ctrl + Shift 这类组合，避免和普通吸附键冲突。",
            _ => "正在录制收进托盘快捷键：现在直接按下组合键。"
        };

        if (target == RecordingTarget.SnapTrigger)
        {
            RecordSnapShortcutButton.Content = "录制中...";
            RecordRuntimeSessionShortcutButton.Content = "开始录制";
            RecordMinimizeShortcutButton.Content = "开始录制";
        }
        else if (target == RecordingTarget.RuntimeSession)
        {
            RecordSnapShortcutButton.Content = "开始录制";
            RecordRuntimeSessionShortcutButton.Content = "录制中...";
            RecordMinimizeShortcutButton.Content = "开始录制";
        }
        else
        {
            RecordSnapShortcutButton.Content = "开始录制";
            RecordRuntimeSessionShortcutButton.Content = "开始录制";
            RecordMinimizeShortcutButton.Content = "录制中...";
        }

        Focus();
    }

    private void StopRecording(string hint)
    {
        _recordingTarget = RecordingTarget.None;
        RecordSnapShortcutButton.Content = "开始录制";
        RecordRuntimeSessionShortcutButton.Content = "开始录制";
        RecordMinimizeShortcutButton.Content = "开始录制";
        RecordingHintTextBlock.Text = hint;
    }

    private enum RecordingTarget
    {
        None,
        SnapTrigger,
        RuntimeSession,
        Minimize
    }
}

public sealed record SettingsDialogResult(
    string SnapModifierKey,
    string RuntimeSessionModifierKey,
    string MinimizeShortcut,
    bool LaunchAtStartup);
