using System.Text.Json;

namespace PaneWorks.Infrastructure.Persistence;

public sealed class JsonSessionStateRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public JsonSessionStateRepository(string filePath)
    {
        _filePath = filePath;
    }

    public SessionState Load()
    {
        if (!File.Exists(_filePath))
        {
            return new SessionState(null, null);
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<SessionState>(json, SerializerOptions) ?? new SessionState(null, null);
        }
        catch
        {
            return new SessionState(null, null);
        }
    }

    public void Save(SessionState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var json = JsonSerializer.Serialize(state, SerializerOptions);
        File.WriteAllText(_filePath, json);
    }
}
