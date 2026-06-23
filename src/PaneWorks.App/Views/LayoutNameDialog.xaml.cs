using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;

namespace PaneWorks.App.Views;

public partial class LayoutNameDialog : Window
{
    public LayoutNameDialog(string title, string message, string initialValue)
    {
        InitializeComponent();
        Title = title;
        MessageTextBlock.Text = message;
        NameTextBox.Text = initialValue;
        NameTextBox.SelectAll();
        Loaded += (_, _) => NameTextBox.Focus();
    }

    public string LayoutName => NameTextBox.Text.Trim();

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LayoutName))
        {
            var owner = Owner ?? this;
            WpfMessageBox.Show(
                owner,
                "请输入布局名称。",
                "PaneWorks",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
