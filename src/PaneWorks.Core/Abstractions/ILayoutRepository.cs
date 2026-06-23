using PaneWorks.Core.Models;

namespace PaneWorks.Core.Abstractions;

public interface ILayoutRepository
{
    Task<IReadOnlyList<LayoutListItem>> ListAsync(CancellationToken cancellationToken);
    Task<LayoutDocument> LoadAsync(string id, CancellationToken cancellationToken);
    Task SaveAsync(string id, LayoutDocument document, CancellationToken cancellationToken);
    Task DeleteAsync(string id, CancellationToken cancellationToken);
    Task RenameAsync(string id, string newName, CancellationToken cancellationToken);
}

