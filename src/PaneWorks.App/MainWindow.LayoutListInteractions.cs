using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfListBoxItem = System.Windows.Controls.ListBoxItem;
using WpfScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using WpfSlider = System.Windows.Controls.Slider;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfToggleButton = System.Windows.Controls.Primitives.ToggleButton;

namespace PaneWorks.App;

public partial class MainWindow
{
    private void MainWindow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInteractiveMenuElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        ViewModel.ClearSelectedLayout();
        ViewModel.ClearSelectedWorkspaceProfile();
    }

    private static bool IsInteractiveMenuElement(DependencyObject? source)
    {
        return FindVisualAncestor<WpfButton>(source) is not null
            || FindVisualAncestor<WpfListBoxItem>(source) is not null
            || FindVisualAncestor<WpfTextBox>(source) is not null
            || FindVisualAncestor<WpfComboBox>(source) is not null
            || FindVisualAncestor<WpfToggleButton>(source) is not null
            || FindVisualAncestor<WpfScrollBar>(source) is not null
            || FindVisualAncestor<WpfSlider>(source) is not null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}
