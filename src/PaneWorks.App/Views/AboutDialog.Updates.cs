using System.Diagnostics;
using System.Windows;

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
        var previousContent = CheckUpdatesButton.Content;
        CheckUpdatesButton.Content = "检查中...";

        try
        {
            var latestRelease = await _releaseClient.GetLatestReleaseAsync();
            if (latestRelease is null)
            {
                PaneMessageService.Show(
                    this,
                    "没有读取到 GitHub 最新版本信息，请稍后再试。",
                    "检查更新",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var currentVersion = AboutReleaseHighlights.ParseVersion(_versionLabel);
            var latestVersion = AboutReleaseHighlights.ParseVersion(latestRelease.TagName);
            if (currentVersion is null || latestVersion is null)
            {
                var openReleases = PaneMessageService.Show(
                    this,
                    $"已读取到最新发布：{latestRelease.DisplayName}\n但无法自动比较版本号，是否打开发布页手动查看？",
                    "检查更新",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (openReleases == MessageBoxResult.Yes)
                {
                    OpenUrl(latestRelease.HtmlUrl);
                }

                return;
            }

            if (latestVersion <= currentVersion)
            {
                PaneMessageService.Show(
                    this,
                    $"当前已经是最新版本：{_versionLabel}",
                    "检查更新",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var result = PaneMessageService.Show(
                this,
                $"发现新版本：{latestRelease.DisplayName}\n当前版本：{_versionLabel}\n\n是否打开 GitHub 发布页下载更新？",
                "发现新版本",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (result == MessageBoxResult.Yes)
            {
                OpenUrl(latestRelease.HtmlUrl);
            }
        }
        catch (Exception ex)
        {
            PaneMessageService.Show(
                this,
                $"检查更新失败：{ex.Message}",
                "检查更新",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _checkingUpdates = false;
            CheckUpdatesButton.IsEnabled = true;
            CheckUpdatesButton.Content = previousContent;
        }
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
