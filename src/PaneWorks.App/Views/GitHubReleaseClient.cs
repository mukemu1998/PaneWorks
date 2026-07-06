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
        return new GitHubReleaseInfo(
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
}

public sealed record GitHubReleaseInfo(string TagName, string DisplayName, string HtmlUrl);
