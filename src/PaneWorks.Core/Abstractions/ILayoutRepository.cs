using PaneWorks.Core.Models;

namespace PaneWorks.Core.Abstractions;

public interface ILayoutRepository
{
    Task<IReadOnlyList<LayoutListItem>> ListAsync(CancellationToken cancellationToken);
    Task<WorkspaceLayoutDocument> LoadAsync(string id, CancellationToken cancellationToken);
    Task SaveAsync(string id, WorkspaceLayoutDocument document, CancellationToken cancellationToken);
    Task DeleteAsync(string id, CancellationToken cancellationToken);
    Task RenameAsync(string id, string newName, CancellationToken cancellationToken);
}
