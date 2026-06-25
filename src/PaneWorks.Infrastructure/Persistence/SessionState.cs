namespace PaneWorks.Infrastructure.Persistence;

public sealed record SessionState
{
    public static SessionState Default { get; } = new();

    public string? LastLayoutId { get; init; }

    public string? LastSnapLayoutId { get; init; }

    public string? SelectedDisplayId { get; init; }

    public Dictionary<string, string?> DisplayLayoutIds { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string?> DisplaySnapLayoutIds { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
