using System.Text.Json;
using PaneWorks.Core.Abstractions;
using PaneWorks.Core.Models;

namespace PaneWorks.Infrastructure.Persistence;

public sealed class JsonLayoutRepository : ILayoutRepository
{
    private const string LegacyDisplayKey = "__legacy__";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _layoutsDirectory;

    public JsonLayoutRepository(string layoutsDirectory)
    {
        _layoutsDirectory = layoutsDirectory;
    }

    public Task<IReadOnlyList<LayoutListItem>> ListAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_layoutsDirectory);

        var items = Directory
            .EnumerateFiles(_layoutsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Select(file =>
            {
                var id = Path.GetFileNameWithoutExtension(file.Name);
                var name = ReadWorkspaceLayoutName(file.FullName) ?? id;

                return new LayoutListItem(
                    id,
                    name,
                    file.FullName,
                    file.LastWriteTimeUtc);
            })
            .OrderByDescending(item => item.LastModified)
            .ToList();

        return Task.FromResult<IReadOnlyList<LayoutListItem>>(items);
    }

    public async Task<WorkspaceLayoutDocument> LoadAsync(string id, CancellationToken cancellationToken)
    {
        var path = GetPath(id);
        await using var stream = File.OpenRead(path);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        var jsonBytes = memory.ToArray();

        var workspace = TryDeserializeWorkspace(jsonBytes);
        if (workspace is not null)
        {
            return NormalizeWorkspace(workspace);
        }

        var legacy = TryDeserializeLegacy(jsonBytes);
        if (legacy is not null)
        {
            return new WorkspaceLayoutDocument(
                legacy.Version,
                legacy.Name,
                new Dictionary<string, LayoutDocument>(StringComparer.OrdinalIgnoreCase)
                {
                    [LegacyDisplayKey] = legacy
                });
        }

        throw new InvalidOperationException($"Unable to load layout '{id}'.");
    }

    public async Task SaveAsync(string id, WorkspaceLayoutDocument document, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_layoutsDirectory);
        var path = GetPath(id);

        await using var stream = File.Create(path);
        await JsonSerializer
            .SerializeAsync(stream, NormalizeWorkspace(document), SerializerOptions, cancellationToken)
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

    public Task RenameAsync(string id, string newName, CancellationToken cancellationToken)
    {
        var oldPath = GetPath(id);
        if (!File.Exists(oldPath))
        {
            return Task.CompletedTask;
        }

        var workspace = LoadAsync(id, cancellationToken).GetAwaiter().GetResult();
        var renamedWorkspace = workspace with { Name = newName };
        var newId = Slugify(newName);
        var newPath = GetPath(newId);

        File.WriteAllText(newPath, JsonSerializer.Serialize(renamedWorkspace, SerializerOptions));

        if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
        {
            File.Delete(oldPath);
        }

        return Task.CompletedTask;
    }

    private string GetPath(string id)
    {
        return Path.Combine(_layoutsDirectory, $"{id}.json");
    }

    private static WorkspaceLayoutDocument? TryDeserializeWorkspace(byte[] jsonBytes)
    {
        try
        {
            return JsonSerializer.Deserialize<WorkspaceLayoutDocument>(jsonBytes, SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private static LayoutDocument? TryDeserializeLegacy(byte[] jsonBytes)
    {
        try
        {
            return JsonSerializer.Deserialize<LayoutDocument>(jsonBytes, SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadWorkspaceLayoutName(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var workspace = TryDeserializeWorkspace(bytes);
            if (workspace is not null && !string.IsNullOrWhiteSpace(workspace.Name))
            {
                return workspace.Name;
            }

            var legacy = TryDeserializeLegacy(bytes);
            return legacy?.Name;
        }
        catch
        {
            return null;
        }
    }

    private static WorkspaceLayoutDocument NormalizeWorkspace(WorkspaceLayoutDocument document)
    {
        return document with
        {
            DisplayLayouts = document.DisplayLayouts is null
                ? new Dictionary<string, LayoutDocument>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, LayoutDocument>(document.DisplayLayouts, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static string Slugify(string value)
    {
        var safe = value.Trim().Replace(' ', '-');
        return string.IsNullOrWhiteSpace(safe) ? "layout" : safe.ToLowerInvariant();
    }
}
