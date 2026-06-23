using System.Text.Json;

namespace PaneWorks.Infrastructure.Persistence;

public sealed class JsonAppSettingsRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public JsonAppSettingsRepository(string filePath)
    {
        _filePath = filePath;
    }

    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return AppSettings.Default;
        }

        try
        {
            using var stream = File.OpenRead(_filePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(stream, SerializerOptions);
            return settings ?? AppSettings.Default;
        }
        catch
        {
            return AppSettings.Default;
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(_filePath);
        JsonSerializer.Serialize(stream, settings, SerializerOptions);
    }
}
