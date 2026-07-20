using System.Windows;
using WpfApplication = System.Windows.Application;

namespace PaneWorks.App.Views;

public static class PaneMessageService
{
    public static MessageBoxResult Show(
        Window? owner,
        string message,
        string title = "PaneWorks",
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.Information)
    {
        var dialog = new PaneMessageDialog(title, message, buttons, image);
        PrepareDialog(dialog, owner);
        dialog.Topmost = true;

        _ = dialog.ShowDialog();
        return dialog.Result == MessageBoxResult.None
            ? GetFallbackResult(buttons)
            : dialog.Result;
    }

    public static void PrepareDialog(Window dialog, Window? owner = null)
    {
        PrepareOwnedDialog(dialog, owner ?? GetActiveOwnerWindow());
    }

    private static Window? GetActiveOwnerWindow()
    {
        return WpfApplication.Current?.Windows
            .OfType<Window>()
            .OrderByDescending(window => window.IsActive)
            .ThenByDescending(window => window.Topmost)
            .FirstOrDefault(window => window.IsVisible)
            ?? WpfApplication.Current?.MainWindow;
    }

    private static void PrepareOwnedDialog(Window dialog, Window? owner)
    {
        if (owner is null)
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        if (owner.WindowState == WindowState.Minimized)
        {
            owner.WindowState = WindowState.Normal;
        }

        owner.Activate();
        owner.Focus();

        if (owner is MainWindow mainWindow)
        {
            mainWindow.PrepareSecondaryDialog(dialog);
            return;
        }

        dialog.Owner = owner;
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.Topmost = owner.Topmost;
    }

    private static MessageBoxResult GetFallbackResult(MessageBoxButton buttons)
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
