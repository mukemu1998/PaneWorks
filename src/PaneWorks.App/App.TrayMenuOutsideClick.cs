using PaneWorks.App.Diagnostics;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfMedia = System.Windows.Media;
using WpfPrimitives = System.Windows.Controls.Primitives;

namespace PaneWorks.App;

public partial class App
{
    private void TrayMenuOutsideClickTimer_Tick(object? sender, EventArgs e)
    {
        var menu = _trayContextMenu;
        if (menu is null || !menu.IsOpen)
        {
            _trayMenuOutsideClickTimer.Stop();
            return;
        }

        if (DateTimeOffset.UtcNow < _trayMenuOutsideClickIgnoreUntil || !IsAnyMouseButtonPressed())
        {
            return;
        }

        if (IsPointInsideMenuTree(menu, Forms.Control.MousePosition))
        {
            return;
        }

        PaneWorksLog.Info("WPF tray menu closed by outside click");
        menu.IsOpen = false;
    }

    private static bool IsAnyMouseButtonPressed()
    {
        return (Forms.Control.MouseButtons
            & (Forms.MouseButtons.Left | Forms.MouseButtons.Right | Forms.MouseButtons.Middle)) != Forms.MouseButtons.None;
    }

    private static bool IsPointInsideMenuTree(WpfControls.ContextMenu menu, Drawing.Point screenPoint)
    {
        if (IsScreenPointInsideElement(menu, screenPoint))
        {
            return true;
        }

        foreach (var item in EnumerateMenuItems(menu))
        {
            if (!item.IsSubmenuOpen)
            {
                continue;
            }

            if (item.Template.FindName("PART_Popup", item) is WpfPrimitives.Popup popup
                && popup.Child is Wpf.FrameworkElement child
                && IsScreenPointInsideElement(child, screenPoint))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<WpfControls.MenuItem> EnumerateMenuItems(WpfControls.ItemsControl owner)
    {
        foreach (var item in owner.Items.OfType<WpfControls.MenuItem>())
        {
            yield return item;

            foreach (var child in EnumerateMenuItems(item))
            {
                yield return child;
            }
        }
    }

    private static bool IsScreenPointInsideElement(Wpf.FrameworkElement element, Drawing.Point screenPoint)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        var topLeft = element.PointToScreen(new Wpf.Point(0, 0));
        var transformToDevice = Wpf.PresentationSource.FromVisual(element)?.CompositionTarget?.TransformToDevice
            ?? WpfMedia.Matrix.Identity;
        var width = element.ActualWidth * transformToDevice.M11;
        var height = element.ActualHeight * transformToDevice.M22;

        return screenPoint.X >= topLeft.X
            && screenPoint.X <= topLeft.X + width
            && screenPoint.Y >= topLeft.Y
            && screenPoint.Y <= topLeft.Y + height;
    }
}
