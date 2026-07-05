using System.Windows;
using System.Windows.Input;

namespace PaneWorks.App.Views;

public partial class LayoutNameDialog : Window
{
    public LayoutNameDialog(string title, string message, string initialValue)
    {
        InitializeComponent();
        Title = title;
        TitleTextBlock.Text = title;
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
            ErrorTextBlock.Visibility = Visibility.Visible;
            NameTextBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void HeaderDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1 || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        DragMove();
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        DialogResult = false;
        e.Handled = true;
    }
}
