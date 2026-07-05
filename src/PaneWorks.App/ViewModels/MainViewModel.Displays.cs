using System;
using System.Collections.Generic;
using System.Linq;
using PaneWorks.Core.Models;
using PaneWorks.Infrastructure.Windows;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    public DisplayInfo GetSelectedDisplay()
    {
        return GetDisplayOrPrimary(SelectedDisplayItem?.Id);
    }

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        return _displaysById.Values
            .OrderBy(display => display.Bounds.X)
            .ThenBy(display => display.Bounds.Y)
            .ToList();
    }

    public bool TryGetDisplayById(string? displayId, out DisplayInfo display)
    {
        if (!string.IsNullOrWhiteSpace(displayId) && _displaysById.TryGetValue(displayId, out display!))
        {
            return true;
        }

        display = GetSelectedDisplay();
        return false;
    }

    private void RefreshDisplays()
    {
        var discoveredDisplays = _displayDiscoveryService.GetDisplays();
        var previousDisplayId = _selectedDisplayItem?.Id;

        _displaysById.Clear();
        Displays.Clear();

        foreach (var display in discoveredDisplays)
        {
            _displaysById[display.Id] = display;
            Displays.Add(new DisplayItemViewModel
            {
                Id = display.Id,
                Name = display.Name,
                Description = $"{(display.IsPrimary ? "主屏" : "扩展屏")}  |  {display.Bounds.Width:0} × {display.Bounds.Height:0}"
            });
        }

        SetSelectedDisplayItemSilently(previousDisplayId ?? GetPrimaryDisplayId());
    }

    private void ActivateDisplay(string? displayId, bool resetHistory)
    {
        if (!TryGetDisplayById(displayId, out var display))
        {
            return;
        }

        _currentWorkspaceDocument = NormalizeWorkspaceForCurrentDisplays(_currentWorkspaceDocument);
        CurrentDocument = GetDisplayLayout(_currentWorkspaceDocument, display.Id);
        SelectedNodeId = CurrentDocument.Root.Id;

        if (resetHistory)
        {
            ResetHistory();
        }

        SetSelectedDisplayItemSilently(display.Id);
        RaisePropertyChanged(nameof(CanvasSubtitle));
        RaisePropertyChanged(nameof(StatusLine));
    }

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

    private void SetSelectedDisplayItemSilently(string displayId)
    {
        var selected = Displays.FirstOrDefault(item => string.Equals(item.Id, displayId, StringComparison.OrdinalIgnoreCase));
        _suppressDisplaySelectionChange = true;
        _selectedDisplayItem = selected;
        RaisePropertyChanged(nameof(SelectedDisplayItem));
        RaisePropertyChanged(nameof(CurrentDisplayName));
        RaisePropertyChanged(nameof(CurrentDisplaySummary));
        RaisePropertyChanged(nameof(CanvasSubtitle));
        RaisePropertyChanged(nameof(StatusLine));
        RaiseWindowBindingStatusChanged();
        _suppressDisplaySelectionChange = false;
    }

    private string GetPrimaryDisplayId()
    {
        return _displaysById.Values.FirstOrDefault(display => display.IsPrimary)?.Id
            ?? _displaysById.Keys.First();
    }

    private DisplayInfo GetDisplayOrPrimary(string? displayId)
    {
        if (!string.IsNullOrWhiteSpace(displayId) && _displaysById.TryGetValue(displayId, out var display))
        {
            return display;
        }

        return _displayDiscoveryService.GetPrimaryDisplay();
    }
}
