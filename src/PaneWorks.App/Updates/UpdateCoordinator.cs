using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows;
using PaneWorks.App.Views;
using WpfApplication = System.Windows.Application;

namespace PaneWorks.App.Updates;

public sealed class UpdateCoordinator
{
    private static readonly Regex Sha256Pattern = new("\\b[a-fA-F0-9]{64}\\b", RegexOptions.Compiled);
    private readonly GitHubReleaseClient _releaseClient = new();
    private static int _isCheckingOrUpdating;

    public static string GetCurrentVersionLabel()
    {
        var version = typeof(UpdateCoordinator).Assembly.GetName().Version;
        return version is null
            ? "v0.0.0"
            : $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    public async Task CheckAndPromptAsync(
        Window owner,
        string currentVersionLabel,
        bool showNoUpdateMessage,
        bool showErrors)
    {
        if (Interlocked.Exchange(ref _isCheckingOrUpdating, 1) != 0)
        {
            return;
        }

        try
        {
            var latestRelease = await _releaseClient.GetLatestReleaseAsync();
            if (latestRelease is null)
            {
                if (showErrors)
                {
                    PaneMessageService.Show(
                        owner,
                        "没有读取到 GitHub 最新版本信息，请稍后再试。",
                        "检查更新",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return;
            }

            var currentVersion = AboutReleaseHighlights.ParseVersion(currentVersionLabel);
            var latestVersion = AboutReleaseHighlights.ParseVersion(latestRelease.TagName);
            if (currentVersion is null || latestVersion is null)
            {
                if (showErrors)
                {
                    PaneMessageService.Show(
                        owner,
                        "无法比较当前版本和最新版本号，请前往 GitHub Releases 手动查看。",
                        "检查更新",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return;
            }

            if (latestVersion <= currentVersion)
            {
                if (showNoUpdateMessage)
                {
                    PaneMessageService.Show(
                        owner,
                        $"当前已经是最新版本：{currentVersionLabel}",
                        "检查更新",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return;
            }

            if (latestRelease.PortablePackage is null || latestRelease.Checksum is null)
            {
                if (showErrors)
                {
                    PaneMessageService.Show(
                        owner,
                        $"发现新版本 {latestRelease.DisplayName}，但发布页缺少可验证的 win-x64 便携包。请稍后重试或前往 GitHub Releases 手动下载。",
                        "发现新版本",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return;
            }

            var result = PaneMessageService.Show(
                owner,
                $"发现新版本：{latestRelease.DisplayName}\n当前版本：{currentVersionLabel}\n\n确认后将自动下载、校验并覆盖更新，完成后会自动重启 PaneWorks。现在更新吗？",
                "发现新版本",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            await DownloadAndApplyAsync(owner, latestRelease);
        }
        catch (Exception exception)
        {
            if (showErrors)
            {
                PaneMessageService.Show(
                    owner,
                    $"检查或安装更新失败：{exception.Message}",
                    "更新失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            Volatile.Write(ref _isCheckingOrUpdating, 0);
        }
    }

    private static async Task DownloadAndApplyAsync(Window owner, GitHubReleaseInfo release)
    {
        var package = release.PortablePackage!;
        var checksum = release.Checksum!;
        var updateDirectory = Path.Combine(
            Path.GetTempPath(),
            "PaneWorks",
            "updates",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateDirectory);
        var packagePath = Path.Combine(updateDirectory, package.Name);

        var progressDialog = new UpdateProgressDialog();
        PaneMessageService.PrepareDialog(progressDialog, owner);
        progressDialog.Topmost = true;
        progressDialog.Show();
        await Task.Yield();

        try
        {
            progressDialog.ReportIndeterminate("正在下载更新", "正在连接 GitHub Release。 ");
            await DownloadPackageAsync(package, packagePath, progressDialog);

            progressDialog.ReportIndeterminate("正在校验更新", "正在验证下载文件的 SHA-256 完整性。 ");
            await VerifyChecksumAsync(packagePath, checksum);

            progressDialog.ReportIndeterminate("准备安装更新", "PaneWorks 将退出并自动完成覆盖更新。 ");
            await Task.Delay(450);
            UpdateBootstrap.LaunchUpdater(packagePath);

            ((App)WpfApplication.Current).ExitForUpdate();
        }
        catch
        {
            progressDialog.Close();
            TryDeleteDirectory(updateDirectory);
            throw;
        }
    }

    private static async Task DownloadPackageAsync(
        GitHubReleaseAsset package,
        string packagePath,
        UpdateProgressDialog progressDialog)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PaneWorks");
        using var response = await client.GetAsync(package.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? package.Size;
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var destination = new FileStream(packagePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long downloadedBytes = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read));
            downloadedBytes += read;
            if (totalBytes > 0)
            {
                var progress = downloadedBytes * 100d / totalBytes;
                progressDialog.ReportProgress(
                    "正在下载更新",
                    $"{FormatFileSize(downloadedBytes)} / {FormatFileSize(totalBytes)}",
                    progress);
            }
            else
            {
                progressDialog.ReportIndeterminate("正在下载更新", $"已下载 {FormatFileSize(downloadedBytes)}");
            }
        }
    }

    private static async Task VerifyChecksumAsync(string packagePath, GitHubReleaseAsset checksumAsset)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PaneWorks");
        var checksumText = await client.GetStringAsync(checksumAsset.DownloadUrl);
        var match = Sha256Pattern.Match(checksumText);
        if (!match.Success)
        {
            throw new InvalidDataException("发布包校验文件格式无效。");
        }

        await using var stream = File.OpenRead(packagePath);
        var hash = await SHA256.HashDataAsync(stream);
        var actualHash = Convert.ToHexString(hash);
        if (!string.Equals(actualHash, match.Value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("下载文件的校验失败，已取消安装。");
        }
    }

    private static string FormatFileSize(long bytes)
    {
        const double kilobyte = 1024;
        const double megabyte = kilobyte * 1024;
        return bytes >= megabyte
            ? $"{bytes / megabyte:0.0} MB"
            : $"{Math.Max(1, bytes / kilobyte):0} KB";
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Temporary update files can be cleaned by the next update attempt.
        }
    }
}
