using System.Windows;
using PaneWorks.Infrastructure.Windows;
using WpfMessageBox = System.Windows.MessageBox;

namespace PaneWorks.App.Views;

public partial class WindowBindingPickerDialog : Window
{
    public WindowBindingPickerDialog(IReadOnlyList<VisibleWindowInfo> windows, string regionLabel)
    {
        InitializeComponent();
        RegionTextBlock.Text = regionLabel;
        WindowListBox.ItemsSource = windows;
        WindowListBox.SelectedIndex = windows.Count > 0 ? 0 : -1;
        Loaded += (_, _) => WindowListBox.Focus();
    }

    public VisibleWindowInfo? SelectedWindow => WindowListBox.SelectedItem as VisibleWindowInfo;

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedWindow is null)
        {
            var owner = Owner ?? this;
            WpfMessageBox.Show(
                owner,
                "请先选择一个要绑定的窗口。",
                "PaneWorks",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
