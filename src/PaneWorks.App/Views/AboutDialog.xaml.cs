using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using WpfMessageBox = System.Windows.MessageBox;

namespace PaneWorks.App.Views;

public partial class AboutDialog : System.Windows.Window
{
    private const string RepositoryUrl = "https://github.com/mukemu1998/PaneWorks";
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/mukemu1998/PaneWorks/releases/latest";
    private const string ReleasesUrl = "https://github.com/mukemu1998/PaneWorks/releases";
    private const string IssuesUrl = "https://github.com/mukemu1998/PaneWorks/issues";
    private readonly string _versionLabel;
    private bool _checkingUpdates;

    public AboutDialog(string versionLabel)
    {
        InitializeComponent();
        _versionLabel = versionLabel;
        VersionTextBlock.Text = versionLabel;
        ReleaseHighlightsTitleTextBlock.Text = $"{versionLabel} 更新要点";
        ReleaseHighlightsTextBlock.Text = GetReleaseHighlights(versionLabel);
    }

    private void OpenGitHubButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        OpenUrl(RepositoryUrl);
    }

    private void OpenReleasesButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        OpenUrl(ReleasesUrl);
    }

    private void OpenIssuesButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        OpenUrl(IssuesUrl);
    }

    private async void CheckUpdatesButton_Click(object sender, System.Windows.RoutedEventArgs e)
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
            var latestRelease = await GetLatestReleaseAsync();
            if (latestRelease is null)
            {
                WpfMessageBox.Show(this, "没有读取到 GitHub 最新版本信息，请稍后再试。", "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var currentVersion = ParseVersion(_versionLabel);
            var latestVersion = ParseVersion(latestRelease.TagName);
            if (currentVersion is null || latestVersion is null)
            {
                var openReleases = WpfMessageBox.Show(
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
                WpfMessageBox.Show(this, $"当前已经是最新版本：{_versionLabel}", "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = WpfMessageBox.Show(
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
            WpfMessageBox.Show(this, $"检查更新失败：{ex.Message}", "检查更新", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _checkingUpdates = false;
            CheckUpdatesButton.IsEnabled = true;
            CheckUpdatesButton.Content = previousContent;
        }
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

    private static async Task<LatestReleaseInfo?> GetLatestReleaseAsync()
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PaneWorks");
        await using var stream = await client.GetStreamAsync(LatestReleaseApiUrl);
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        var tagName = GetJsonString(root, "tag_name");
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return null;
        }

        var releaseName = GetJsonString(root, "name");
        var htmlUrl = GetJsonString(root, "html_url");
        return new LatestReleaseInfo(
            tagName,
            string.IsNullOrWhiteSpace(releaseName) ? tagName : releaseName,
            string.IsNullOrWhiteSpace(htmlUrl) ? ReleasesUrl : htmlUrl);
    }

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static Version? ParseVersion(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        var metadataIndex = normalized.IndexOfAny(['+', '-']);
        if (metadataIndex >= 0)
        {
            normalized = normalized[..metadataIndex];
        }

        return Version.TryParse(normalized, out var version) ? version : null;
    }

    private static string GetReleaseHighlights(string versionLabel)
    {
        return ParseVersion(versionLabel) switch
        {
            { Major: 0, Minor: 2, Build: 3 } =>
                "· 关于窗口新增当前版本要点与检查更新入口。\n" +
                "· 主菜单、设置窗口与侧边悬浮条交互继续打磨。\n" +
                "· 源码外壳 UI 拆分整理，便于后续版本维护。",
            { Major: 0, Minor: 2, Build: 2 } =>
                "· 临时拓扑吸附：窗口退出后，剩余窗口可重新靠近并恢复联动。\n" +
                "· 平行参考线锁止：拖动到屏幕边缘或平行线后不再越界。\n" +
                "· 细节打磨：优化吸附识别、置顶补偿和画布分割线显示。",
            _ => "· 当前版本持续围绕桌面分区、窗口吸附和工作区恢复体验进行稳定性打磨。"
        };
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private sealed record LatestReleaseInfo(string TagName, string DisplayName, string HtmlUrl);
}
