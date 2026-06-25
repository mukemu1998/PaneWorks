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
            return SessionState.Default;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var state = JsonSerializer.Deserialize<SessionState>(json, SerializerOptions);
            return Normalize(state);
        }
        catch
        {
            return SessionState.Default;
        }
    }

    public void Save(SessionState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var json = JsonSerializer.Serialize(Normalize(state), SerializerOptions);
        File.WriteAllText(_filePath, json);
    }

    private static SessionState Normalize(SessionState? state)
    {
        state ??= SessionState.Default;

        return state with
        {
            DisplayLayoutIds = state.DisplayLayoutIds ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
            DisplaySnapLayoutIds = state.DisplaySnapLayoutIds ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        };
    }
}
