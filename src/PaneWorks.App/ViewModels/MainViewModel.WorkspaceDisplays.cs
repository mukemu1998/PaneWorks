using PaneWorks.Core.Models;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private WorkspaceLayoutDocument CreateWorkspaceDocument(string name)
    {
        var normalizedName = NormalizeLayoutName(name);
        var layouts = new Dictionary<string, LayoutDocument>(StringComparer.OrdinalIgnoreCase);

        foreach (var display in _displaysById.Values)
        {
            layouts[display.Id] = _editorService.CreateBlank(normalizedName);
        }

        return new WorkspaceLayoutDocument(1, normalizedName, layouts);
    }

    private WorkspaceLayoutDocument NormalizeWorkspaceForCurrentDisplays(WorkspaceLayoutDocument workspace)
    {
        var normalizedName = NormalizeLayoutName(workspace.Name);
        var layouts = workspace.DisplayLayouts is null
            ? new Dictionary<string, LayoutDocument>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, LayoutDocument>(workspace.DisplayLayouts, StringComparer.OrdinalIgnoreCase);

        var exactMatches = layouts.Keys.Count(key => _displaysById.ContainsKey(key));
        var fallback = layouts.Values.FirstOrDefault();

        foreach (var display in _displaysById.Values)
        {
            if (layouts.ContainsKey(display.Id))
            {
                continue;
            }

            if (fallback is not null && exactMatches == 0 && display.IsPrimary)
            {
                layouts[display.Id] = fallback with { Name = normalizedName };
                continue;
            }

            layouts[display.Id] = _editorService.CreateBlank(normalizedName);
        }

        return new WorkspaceLayoutDocument(
            workspace.Version,
            normalizedName,
            layouts);
    }

    private LayoutDocument GetDisplayLayout(WorkspaceLayoutDocument workspace, string? displayId)
    {
        workspace = NormalizeWorkspaceForCurrentDisplays(workspace);
        var resolvedDisplayId = !string.IsNullOrWhiteSpace(displayId) && workspace.DisplayLayouts.ContainsKey(displayId)
            ? displayId
            : GetPrimaryDisplayId();

        if (workspace.DisplayLayouts.TryGetValue(resolvedDisplayId, out var layout))
        {
            return layout;
        }

        return workspace.DisplayLayouts.Values.FirstOrDefault() ?? _editorService.CreateBlank(workspace.Name);
    }

    private WorkspaceLayoutDocument ReplaceDisplayLayout(WorkspaceLayoutDocument workspace, string? displayId, LayoutDocument document)
    {
        workspace = NormalizeWorkspaceForCurrentDisplays(workspace);
        var resolvedDisplayId = !string.IsNullOrWhiteSpace(displayId) ? displayId : GetPrimaryDisplayId();
        var layouts = new Dictionary<string, LayoutDocument>(workspace.DisplayLayouts, StringComparer.OrdinalIgnoreCase)
        {
            [resolvedDisplayId] = document
        };

        return workspace with { DisplayLayouts = layouts };
    }

    private WorkspaceLayoutDocument RenameWorkspace(WorkspaceLayoutDocument workspace, string newName)
    {
        var normalizedName = NormalizeLayoutName(newName);
        var layouts = workspace.DisplayLayouts.ToDictionary(
            entry => entry.Key,
            entry => entry.Value with { Name = normalizedName },
            StringComparer.OrdinalIgnoreCase);

        return workspace with
        {
            Name = normalizedName,
            DisplayLayouts = layouts
        };
    }
}
