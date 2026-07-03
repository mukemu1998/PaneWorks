using PaneWorks.Core.Models;

namespace PaneWorks.Core.Abstractions;

public interface IWorkspaceProfileRepository
{
    Task<IReadOnlyList<WorkspaceProfileListItem>> ListAsync(CancellationToken cancellationToken);
    Task<WorkspaceProfileDocument> LoadAsync(string id, CancellationToken cancellationToken);
    Task SaveAsync(string id, WorkspaceProfileDocument document, CancellationToken cancellationToken);
    Task DeleteAsync(string id, CancellationToken cancellationToken);
}
