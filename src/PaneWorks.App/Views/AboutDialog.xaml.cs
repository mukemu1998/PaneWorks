using System.Windows;
using System.Windows.Media;
using PaneWorks.App.Updates;

namespace PaneWorks.App.Views;

public partial class AboutDialog : System.Windows.Window
{
    private readonly UpdateCoordinator _updateCoordinator = new();
    private readonly string _versionLabel;
    private bool _checkingUpdates;

    public AboutDialog(string versionLabel)
    {
        InitializeComponent();
        _versionLabel = versionLabel;
        VersionTextBlock.Text = versionLabel;
        ReleaseHighlightsTitleTextBlock.Text = $"{versionLabel} 更新要点";
        ReleaseHighlightsTextBlock.Text = AboutReleaseHighlights.Get(versionLabel);
    }

    private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Close();
    }

    private void HeaderDragArea_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1 || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            return;
        }

        if (IsButtonSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (e.GetPosition(this).Y > 360)
        {
            return;
        }

        DragMove();
        e.Handled = true;
    }

    private static bool IsButtonSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

}
