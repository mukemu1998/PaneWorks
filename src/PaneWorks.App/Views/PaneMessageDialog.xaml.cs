using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;

namespace PaneWorks.App.Views;

public partial class PaneMessageDialog : Window
{
    private readonly MessageBoxButton _buttons;

    public PaneMessageDialog(string title, string message, MessageBoxButton buttons, MessageBoxImage image)
    {
        InitializeComponent();
        _buttons = buttons;
        Title = title;
        TitleTextBlock.Text = title;
        MessageTextBlock.Text = message;
        ConfigureKind(image);
        BuildButtons(buttons, image);
    }

    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    private void ConfigureKind(MessageBoxImage image)
    {
        var (text, foreground, background, border) = image switch
        {
            MessageBoxImage.Warning => ("!", "#FFFFB86B", "#26FFB86B", "#80FFB86B"),
            MessageBoxImage.Error => ("!", "#FFFF7A83", "#2AE35C61", "#86E35C61"),
            MessageBoxImage.Question => ("?", "#FF58AFFF", "#242F80ED", "#6658AFFF"),
            _ => ("i", "#FF58AFFF", "#242F80ED", "#6658AFFF")
        };

        KindTextBlock.Text = text;
        KindTextBlock.Foreground = (WpfBrush)new BrushConverter().ConvertFromString(foreground)!;
        KindBadge.Background = (WpfBrush)new BrushConverter().ConvertFromString(background)!;
        KindBadge.BorderBrush = (WpfBrush)new BrushConverter().ConvertFromString(border)!;
    }

    private void BuildButtons(MessageBoxButton buttons, MessageBoxImage image)
    {
        ButtonPanel.Children.Clear();

        switch (buttons)
        {
            case MessageBoxButton.OK:
                AddButton("确定", MessageBoxResult.OK, isPrimary: true);
                break;
            case MessageBoxButton.OKCancel:
                AddButton("取消", MessageBoxResult.Cancel);
                AddButton("确定", MessageBoxResult.OK, isPrimary: true);
                break;
            case MessageBoxButton.YesNo:
                AddButton("否", MessageBoxResult.No);
                AddButton("是", MessageBoxResult.Yes, isPrimary: image != MessageBoxImage.Warning);
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton("取消", MessageBoxResult.Cancel);
                AddButton("否", MessageBoxResult.No);
                AddButton("是", MessageBoxResult.Yes, isPrimary: true);
                break;
        }
    }

    private void AddButton(string content, MessageBoxResult result, bool isPrimary = false)
    {
        var button = new WpfButton
        {
            Width = 96,
            MinWidth = 96,
            Margin = new Thickness(ButtonPanel.Children.Count == 0 ? 0 : 10, 0, 0, 0),
            Content = content,
            Style = isPrimary
                ? (Style)FindResource("PrimaryButtonStyle")
                : (Style)FindResource("RoundedButtonBaseStyle")
        };

        button.Click += (_, _) =>
        {
            Result = result;
            DialogResult = true;
        };

        ButtonPanel.Children.Add(button);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Result = _buttons switch
        {
            MessageBoxButton.OK => MessageBoxResult.OK,
            MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
            _ => MessageBoxResult.None
        };
        DialogResult = true;
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

        CloseButton_Click(sender, e);
        e.Handled = true;
    }
}
