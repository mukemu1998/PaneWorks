using System.Windows;
using PaneWorks.App.Views;
using WpfApplication = System.Windows.Application;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private static string? PromptForLayoutName(string title, string message, string initialValue)
    {
        var dialog = new LayoutNameDialog(title, message, initialValue);
        var owner = GetOwnerWindow();
        PrepareSecondaryDialog(dialog, owner);

        return dialog.ShowDialog() == true ? dialog.LayoutName : null;
    }

    private static MessageBoxResult ShowMessage(string message, MessageBoxButton buttons, MessageBoxImage image)
    {
        var dialog = new PaneMessageDialog("PaneWorks", message, buttons, image);
        var owner = GetOwnerWindow();
        PrepareSecondaryDialog(dialog, owner);

        _ = dialog.ShowDialog();
        return dialog.Result == MessageBoxResult.None
            ? GetFallbackMessageResult(buttons)
            : dialog.Result;
    }

    private static void ShowInfoMessage(string message)
    {
        ShowMessage(message, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void ShowErrorMessage(string message)
    {
        ShowMessage(message, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static Window? GetOwnerWindow()
    {
        return WpfApplication.Current?.Windows
            .OfType<Window>()
            .OrderByDescending(window => window.IsActive)
            .ThenByDescending(window => window.Topmost)
            .FirstOrDefault(window => window.IsVisible)
            ?? WpfApplication.Current?.MainWindow;
    }

    private static void PrepareOwnerWindow(Window? owner)
    {
        if (owner is null)
        {
            return;
        }

        if (owner.WindowState == WindowState.Minimized)
        {
            owner.WindowState = WindowState.Normal;
        }

        owner.Activate();
        owner.Focus();
    }

    private static void PrepareSecondaryDialog(Window dialog, Window? owner)
    {
        if (owner is null)
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        PrepareOwnerWindow(owner);

        if (owner is MainWindow mainWindow)
        {
            mainWindow.PrepareSecondaryDialog(dialog);
            return;
        }

        dialog.Owner = owner;
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.Topmost = owner.Topmost;
    }

    private static MessageBoxResult GetFallbackMessageResult(MessageBoxButton buttons)
    {
        return buttons switch
        {
            MessageBoxButton.OK => MessageBoxResult.OK,
            MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
            _ => MessageBoxResult.None
        };
    }
}
