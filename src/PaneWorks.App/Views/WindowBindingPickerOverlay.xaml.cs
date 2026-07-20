using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PaneWorks.Infrastructure.Windows;
using DrawingIcon = System.Drawing.Icon;

namespace PaneWorks.App.Views;

public partial class WindowBindingPickerOverlay : System.Windows.Controls.UserControl
{
    public WindowBindingPickerOverlay()
    {
        InitializeComponent();
    }

    public event EventHandler? BindingConfirmed;

    public event EventHandler? Closed;

    public VisibleWindowInfo? SelectedWindow =>
        (WindowListBox.SelectedItem as WindowBindingPickerItem)?.Window;

    public void Open(IReadOnlyList<VisibleWindowInfo> windows, string regionLabel, IntPtr preferredWindowHandle)
    {
        var items = windows.Select(window => new WindowBindingPickerItem(window, TryLoadIcon(window.ExecutablePath))).ToList();
        RegionTextBlock.Text = regionLabel;
        WindowListBox.ItemsSource = items;
        WindowListBox.SelectedIndex = items.Count > 0 ? 0 : -1;
        DetectedHintBorder.Visibility = Visibility.Collapsed;
        ConfirmButton.Content = "绑定到当前区域";

        var preferredItem = items.FirstOrDefault(item => item.Window.Handle == preferredWindowHandle);
        if (preferredItem is not null)
        {
            WindowListBox.SelectedItem = preferredItem;
            DetectedHintBorder.Visibility = Visibility.Visible;
            DetectedHintTextBlock.Text = "已识别当前区域中正在吸附的窗口，直接确认即可完成绑定。";
            ConfirmButton.Content = "绑定当前吸附窗口";
        }

        Visibility = Visibility.Visible;
        WindowListBox.Focus();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedWindow is null)
        {
            return;
        }

        Visibility = Visibility.Collapsed;
        BindingConfirmed?.Invoke(this, EventArgs.Empty);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void WindowListBox_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (e.Delta < 0)
        {
            WindowListBox.SelectedIndex = Math.Min(WindowListBox.Items.Count - 1, WindowListBox.SelectedIndex + 1);
        }
        else
        {
            WindowListBox.SelectedIndex = Math.Max(0, WindowListBox.SelectedIndex - 1);
        }

        WindowListBox.ScrollIntoView(WindowListBox.SelectedItem);
        e.Handled = true;
    }

    private static ImageSource? TryLoadIcon(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            using var icon = DrawingIcon.ExtractAssociatedIcon(executablePath);
            if (icon is null)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    private sealed class WindowBindingPickerItem
    {
        public WindowBindingPickerItem(VisibleWindowInfo window, ImageSource? iconSource)
        {
            Window = window;
            IconSource = iconSource;
            ProcessLabel = string.IsNullOrWhiteSpace(window.ExplorerFolderPath)
                ? $"{window.ProcessName}.exe"
                : $"{window.ProcessName}.exe - 文件夹";
            Title = string.IsNullOrWhiteSpace(window.Title) ? "未命名窗口" : window.Title;
            ExecutablePath = !string.IsNullOrWhiteSpace(window.ExplorerFolderPath)
                ? $"文件夹：{window.ExplorerFolderPath}"
                : string.IsNullOrWhiteSpace(window.ExecutablePath) ? "未读取到程序路径" : window.ExecutablePath;
            FallbackGlyph = string.IsNullOrWhiteSpace(window.ProcessName) ? "?" : window.ProcessName[..1].ToUpperInvariant();
        }

        public VisibleWindowInfo Window { get; }
        public ImageSource? IconSource { get; }
        public string ProcessLabel { get; }
        public string Title { get; }
        public string ExecutablePath { get; }
        public string FallbackGlyph { get; }
    }
}
