using System.Windows;
using PaneWorks.App.Views;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private static string? PromptForLayoutName(string title, string message, string initialValue)
    {
        var dialog = new LayoutNameDialog(title, message, initialValue);
        PaneMessageService.PrepareDialog(dialog);

        return dialog.ShowDialog() == true ? dialog.LayoutName : null;
    }

    private static MessageBoxResult ShowMessage(string message, MessageBoxButton buttons, MessageBoxImage image)
    {
        return PaneMessageService.Show(null, message, buttons: buttons, image: image);
    }

    private static void ShowInfoMessage(string message)
    {
        ShowMessage(message, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void ShowErrorMessage(string message)
    {
        ShowMessage(message, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
