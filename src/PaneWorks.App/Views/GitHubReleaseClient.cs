using System.Net.Http;
using System.Text.Json;

namespace PaneWorks.App.Views;

public sealed class GitHubReleaseClient
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/mukemu1998/PaneWorks/releases/latest";
    private const string ReleasesUrl = "https://github.com/mukemu1998/PaneWorks/releases";

    public async Task<GitHubReleaseInfo?> GetLatestReleaseAsync()
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
        var assets = GetAssets(root);
        var portablePackage = assets.FirstOrDefault(asset =>
            asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            && asset.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase)
            && asset.Name.Contains("portable", StringComparison.OrdinalIgnoreCase));
        var checksum = portablePackage is null
            ? null
            : assets.FirstOrDefault(asset => string.Equals(
                asset.Name,
                $"{portablePackage.Name}.sha256",
                StringComparison.OrdinalIgnoreCase));

        return new GitHubReleaseInfo(
            tagName,
            string.IsNullOrWhiteSpace(releaseName) ? tagName : releaseName,
            string.IsNullOrWhiteSpace(htmlUrl) ? ReleasesUrl : htmlUrl,
            portablePackage,
            checksum);
    }

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static IReadOnlyList<GitHubReleaseAsset> GetAssets(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assetsElement)
            || assetsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<GitHubReleaseAsset>();
        }

        var assets = new List<GitHubReleaseAsset>();
        foreach (var assetElement in assetsElement.EnumerateArray())
        {
            var name = GetJsonString(assetElement, "name");
            var downloadUrl = GetJsonString(assetElement, "browser_download_url");
            if (string.IsNullOrWhiteSpace(name)
                || string.IsNullOrWhiteSpace(downloadUrl)
                || !Uri.TryCreate(downloadUrl, UriKind.Absolute, out var downloadUri))
            {
                continue;
            }

            var size = assetElement.TryGetProperty("size", out var sizeElement)
                && sizeElement.TryGetInt64(out var value)
                    ? value
                    : 0;
            assets.Add(new GitHubReleaseAsset(name, downloadUri, size));
        }

        return assets;
    }
}

public sealed record GitHubReleaseInfo(
    string TagName,
    string DisplayName,
    string HtmlUrl,
    GitHubReleaseAsset? PortablePackage,
    GitHubReleaseAsset? Checksum);

public sealed record GitHubReleaseAsset(string Name, Uri DownloadUrl, long Size);
