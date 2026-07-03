using System.Text.Json;
using PaneWorks.Core.Abstractions;
using PaneWorks.Core.Models;

namespace PaneWorks.Infrastructure.Persistence;

public sealed class JsonWorkspaceProfileRepository : IWorkspaceProfileRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _profilesDirectory;
    private readonly string _layoutsDirectory;

    public JsonWorkspaceProfileRepository(string profilesDirectory, string layoutsDirectory)
    {
        _profilesDirectory = profilesDirectory;
        _layoutsDirectory = layoutsDirectory;
    }

    public Task<IReadOnlyList<WorkspaceProfileListItem>> ListAsync(CancellationToken cancellationToken)
    {
        EnsureLegacyBindingsMigrated();
        Directory.CreateDirectory(_profilesDirectory);

        var items = Directory
            .EnumerateFiles(_profilesDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Select(file =>
            {
                var id = Path.GetFileNameWithoutExtension(file.Name);
                var profile = ReadProfile(file.FullName);
                var name = profile?.Name;
                var layoutId = profile?.LayoutId ?? string.Empty;
                var bindingCount = profile?.WindowBindings?.Count ?? 0;

                return new WorkspaceProfileListItem(
                    id,
                    string.IsNullOrWhiteSpace(name) ? id : name,
                    layoutId,
                    bindingCount,
                    file.FullName,
                    file.LastWriteTimeUtc);
            })
            .OrderByDescending(item => item.LastModified)
            .ToList();

        return Task.FromResult<IReadOnlyList<WorkspaceProfileListItem>>(items);
    }

    public async Task<WorkspaceProfileDocument> LoadAsync(string id, CancellationToken cancellationToken)
    {
        EnsureLegacyBindingsMigrated();
        var path = GetPath(id);
        await using var stream = File.OpenRead(path);
        var profile = await JsonSerializer.DeserializeAsync<WorkspaceProfileDocument>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null)
        {
            throw new InvalidOperationException($"Unable to load workspace profile '{id}'.");
        }

        return NormalizeProfile(profile);
    }

    public async Task SaveAsync(string id, WorkspaceProfileDocument document, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_profilesDirectory);
        var path = GetPath(id);

        await using var stream = File.Create(path);
        await JsonSerializer
            .SerializeAsync(stream, NormalizeProfile(document), SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        var path = GetPath(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetPath(string id)
    {
        return Path.Combine(_profilesDirectory, $"{id}.json");
    }

    private void EnsureLegacyBindingsMigrated()
    {
        Directory.CreateDirectory(_profilesDirectory);

        if (!Directory.Exists(_layoutsDirectory))
        {
            return;
        }

        foreach (var layoutPath in Directory.EnumerateFiles(_layoutsDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var legacy = ReadLegacyLayout(layoutPath);
                if (legacy is null || legacy.WindowBindings is null || legacy.WindowBindings.Count == 0)
                {
                    continue;
                }

                var layoutId = Path.GetFileNameWithoutExtension(layoutPath);
                var profilePath = GetPath(layoutId);
                if (File.Exists(profilePath))
                {
                    continue;
                }

                var importedProfile = new WorkspaceProfileDocument(
                    2,
                    $"{NormalizeName(legacy.Name, layoutId)} 工作区方案",
                    layoutId,
                    legacy.WindowBindings);

                File.WriteAllText(profilePath, JsonSerializer.Serialize(NormalizeProfile(importedProfile), SerializerOptions));
            }
            catch
            {
            }
        }
    }

    private static WorkspaceProfileDocument NormalizeProfile(WorkspaceProfileDocument document)
    {
        return document with
        {
            Version = Math.Max(2, document.Version),
            Name = NormalizeName(document.Name, "工作区方案"),
            LayoutId = document.LayoutId?.Trim() ?? string.Empty,
            WindowBindings = NormalizeBindings(document.WindowBindings)
        };
    }

    private static List<WorkspaceWindowBinding> NormalizeBindings(IEnumerable<WorkspaceWindowBinding>? bindings)
    {
        return bindings?
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.DisplayId)
                && !string.IsNullOrWhiteSpace(item.NodeId)
                && !string.IsNullOrWhiteSpace(item.ProcessName))
            .Select(item => item with
            {
                DisplayId = item.DisplayId.Trim(),
                NodeId = item.NodeId.Trim(),
                ProcessName = item.ProcessName.Trim(),
                WindowTitleSnapshot = item.WindowTitleSnapshot?.Trim() ?? string.Empty,
                ExecutablePath = item.ExecutablePath?.Trim() ?? string.Empty,
                LaunchArguments = item.LaunchArguments?.Trim() ?? string.Empty,
                WorkingDirectory = item.WorkingDirectory?.Trim() ?? string.Empty,
                MatchKind = NormalizeToken(item.MatchKind, "Window"),
                MatchMode = NormalizeToken(item.MatchMode, "Auto"),
                LaunchTarget = item.LaunchTarget?.Trim() ?? string.Empty,
                StackOrder = Math.Max(0, item.StackOrder)
            })
            .ToList()
            ?? new List<WorkspaceWindowBinding>();
    }

    private static WorkspaceProfileDocument? ReadProfile(string path)
    {
        try
        {
            return NormalizeProfile(JsonSerializer.Deserialize<WorkspaceProfileDocument>(File.ReadAllBytes(path), SerializerOptions)!);
        }
        catch
        {
            return null;
        }
    }

    private static LegacyLayoutWithBindingsDocument? ReadLegacyLayout(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<LegacyLayoutWithBindingsDocument>(File.ReadAllBytes(path), SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeName(string? value, string fallback)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string NormalizeToken(string? value, string fallback)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private sealed record LegacyLayoutWithBindingsDocument(
        int Version,
        string Name,
        Dictionary<string, LayoutDocument>? DisplayLayouts,
        List<WorkspaceWindowBinding>? WindowBindings);
}
