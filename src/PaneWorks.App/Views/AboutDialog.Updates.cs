using System.Windows;
using PaneWorks.App.Updates;

namespace PaneWorks.App.Views;

public partial class AboutDialog
{
    private const string RepositoryUrl = "https://github.com/mukemu1998/PaneWorks";
    private const string ReleasesUrl = "https://github.com/mukemu1998/PaneWorks/releases";
    private const string IssuesUrl = "https://github.com/mukemu1998/PaneWorks/issues";

    private void OpenGitHubButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(RepositoryUrl);
    }

    private void OpenReleasesButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(ReleasesUrl);
    }

    private void OpenIssuesButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(IssuesUrl);
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_checkingUpdates)
        {
            return;
        }

        _checkingUpdates = true;
        CheckUpdatesButton.IsEnabled = false;

        try
        {
            await _updateCoordinator.CheckAndPromptAsync(
                this,
                _versionLabel,
                showNoUpdateMessage: true,
                showErrors: true);
        }
        finally
        {
            _checkingUpdates = false;
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private static void OpenUrl(string url)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
