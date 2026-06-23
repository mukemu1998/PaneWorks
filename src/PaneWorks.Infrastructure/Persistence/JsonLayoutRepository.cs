using System.Text.Json;
using PaneWorks.Core.Abstractions;
using PaneWorks.Core.Models;

namespace PaneWorks.Infrastructure.Persistence;

public sealed class JsonLayoutRepository : ILayoutRepository
{
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
                var name = ReadLayoutName(file.FullName) ?? id;

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

    public async Task<LayoutDocument> LoadAsync(string id, CancellationToken cancellationToken)
    {
        var path = GetPath(id);
        using var stream = File.OpenRead(path);
        var document = await JsonSerializer
            .DeserializeAsync<LayoutDocument>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
        return document ?? throw new InvalidOperationException($"Unable to load layout '{id}'.");
    }

    public async Task SaveAsync(string id, LayoutDocument document, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_layoutsDirectory);
        var path = GetPath(id);

        using var stream = File.Create(path);
        await JsonSerializer
            .SerializeAsync(stream, document, SerializerOptions, cancellationToken)
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

        var document = JsonSerializer.Deserialize<LayoutDocument>(File.ReadAllText(oldPath), SerializerOptions)
            ?? throw new InvalidOperationException($"Unable to load layout '{id}' for rename.");

        var renamedDocument = document with { Name = newName };
        var newId = Slugify(newName);
        var newPath = GetPath(newId);

        File.WriteAllText(newPath, JsonSerializer.Serialize(renamedDocument, SerializerOptions));

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

    private static string? ReadLayoutName(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var document = JsonSerializer.Deserialize<LayoutDocument>(stream, SerializerOptions);
            return document?.Name;
        }
        catch
        {
            return null;
        }
    }

    private static string Slugify(string value)
    {
        var safe = value.Trim().Replace(' ', '-');
        return string.IsNullOrWhiteSpace(safe) ? "layout" : safe.ToLowerInvariant();
    }
}
