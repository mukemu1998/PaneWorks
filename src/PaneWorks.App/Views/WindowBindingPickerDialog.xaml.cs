using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PaneWorks.Infrastructure.Windows;
using DrawingIcon = System.Drawing.Icon;

namespace PaneWorks.App.Views;

public partial class WindowBindingPickerDialog : Window
{
    public event EventHandler? BindingConfirmed;

    public WindowBindingPickerDialog(
        IReadOnlyList<VisibleWindowInfo> windows,
        string regionLabel)
    {
        InitializeComponent();
        RegionTextBlock.Text = regionLabel;

        var items = windows
            .Select(window => new WindowBindingPickerItem(window))
            .ToList();

        WindowListBox.ItemsSource = items;
        WindowListBox.SelectedIndex = items.Count > 0 ? 0 : -1;

        Loaded += (_, _) => WindowListBox.Focus();
    }

    public VisibleWindowInfo? SelectedWindow =>
        (WindowListBox.SelectedItem as WindowBindingPickerItem)?.Window;

    public IntPtr PreferredWindowHandle
    {
        get => _preferredWindowHandle;
        set
        {
            _preferredWindowHandle = value;
            ApplyPreferredSelection();
        }
    }

    private IntPtr _preferredWindowHandle;

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedWindow is null)
        {
            var owner = Owner ?? this;
            PaneMessageService.Show(
                owner,
                "请先选择一个要绑定的窗口。",
                buttons: MessageBoxButton.OK,
                image: MessageBoxImage.Information);
            return;
        }

        BindingConfirmed?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void HeaderDragArea_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1 || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            return;
        }

        var source = e.OriginalSource as DependencyObject;
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase)
            {
                return;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        DragMove();
        e.Handled = true;
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

    private void ApplyPreferredSelection()
    {
        if (_preferredWindowHandle == IntPtr.Zero
            || WindowListBox.ItemsSource is not IEnumerable<WindowBindingPickerItem> items)
        {
            DetectedHintBorder.Visibility = Visibility.Collapsed;
            ConfirmButton.Content = "绑定到当前区域";
            return;
        }

        var preferredItem = items.FirstOrDefault(item => item.Window.Handle == _preferredWindowHandle);
        if (preferredItem is null)
        {
            DetectedHintBorder.Visibility = Visibility.Collapsed;
            ConfirmButton.Content = "绑定到当前区域";
            return;
        }

        WindowListBox.SelectedItem = preferredItem;
        DetectedHintBorder.Visibility = Visibility.Visible;
        DetectedHintTextBlock.Text = "已自动识别当前区域里正在吸附的窗口，直接确认就能完成绑定。";
        ConfirmButton.Content = "绑定当前吸附窗口";
    }

    private static ImageSource? TryLoadIcon(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            using var extractedIcon = DrawingIcon.ExtractAssociatedIcon(executablePath);
            if (extractedIcon is null)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(
                extractedIcon.Handle,
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

    private sealed class WindowBindingPickerItem : System.ComponentModel.INotifyPropertyChanged
    {
        private ImageSource? _iconSource;

        public WindowBindingPickerItem(VisibleWindowInfo window)
        {
            Window = window;
            ProcessLabel = string.IsNullOrWhiteSpace(window.ExplorerFolderPath)
                ? $"{window.ProcessName}.exe"
                : $"{window.ProcessName}.exe · 文件夹";
            Title = string.IsNullOrWhiteSpace(window.Title) ? "未命名窗口" : window.Title;
            ExecutablePath = !string.IsNullOrWhiteSpace(window.ExplorerFolderPath)
                ? $"文件夹：{window.ExplorerFolderPath}"
                : string.IsNullOrWhiteSpace(window.ExecutablePath)
                    ? "未读取到程序路径"
                    : window.ExecutablePath;
            FallbackGlyph = string.IsNullOrWhiteSpace(window.ProcessName)
                ? "?"
                : window.ProcessName[..1].ToUpperInvariant();
        }

        public VisibleWindowInfo Window { get; }

        public ImageSource? IconSource
        {
            get => _iconSource;
            set
            {
                if (ReferenceEquals(_iconSource, value))
                {
                    return;
                }

                _iconSource = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IconSource)));
            }
        }

        public string ProcessLabel { get; }

        public string Title { get; }

        public string ExecutablePath { get; }

        public string FallbackGlyph { get; }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}
