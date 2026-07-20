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
        var resolvedDisplay = ResolveCurrentDisplay(displayId);
        if (resolvedDisplay is not null)
        {
            display = resolvedDisplay;
            return true;
        }

        display = GetSelectedDisplay();
        return false;
    }

    public bool ReconcileDisplaysAfterStartup()
    {
        var previousTopology = GetDisplayTopologyFingerprint(_displaysById.Values);
        var previousSelectedDisplayId = SelectedDisplayItem?.Id;

        RefreshDisplays();

        var currentTopology = GetDisplayTopologyFingerprint(_displaysById.Values);
        var changed = !string.Equals(previousTopology, currentTopology, StringComparison.Ordinal);

        _currentWorkspaceDocument = NormalizeWorkspaceForCurrentDisplays(_currentWorkspaceDocument);
        _savedState = new PersistedWorkspaceState(
            NormalizeWorkspaceForCurrentDisplays(_savedState.WorkspaceDocument),
            _savedState.LayoutId);

        if (ShouldSyncActiveSnapWithCurrentWorkspace())
        {
            SetActiveSnapWorkspace(_currentLayoutId, _currentWorkspaceDocument.Name, _currentWorkspaceDocument);
        }
        else
        {
            _activeSnapWorkspaceDocument = NormalizeWorkspaceForCurrentDisplays(_activeSnapWorkspaceDocument);
            RaisePropertyChanged(nameof(ActiveSnapLayoutName));
            RaisePropertyChanged(nameof(ActiveSnapLayoutId));
            RaisePropertyChanged(nameof(DisplayedLayoutName));
            RaisePropertyChanged(nameof(SnapLayoutLabel));
            RaisePropertyChanged(nameof(WorkspaceProfileLabel));
            RaiseWindowBindingStatusChanged();
        }

        var selectedDisplayId = !string.IsNullOrWhiteSpace(previousSelectedDisplayId)
                                && _displaysById.ContainsKey(previousSelectedDisplayId)
            ? previousSelectedDisplayId
            : GetPrimaryDisplayId();

        CurrentDocument = GetDisplayLayout(_currentWorkspaceDocument, selectedDisplayId);
        SelectedNodeId = CurrentDocument.Root.Id;
        SetSelectedDisplayItemSilently(selectedDisplayId);
        UpdateDirtyState();
        SaveSessionState();

        return changed;
    }

    public void SelectDisplayForLayoutEditing(string displayId)
    {
        if (!IsLayoutEditMode || !TryGetDisplayById(displayId, out var display))
        {
            return;
        }

        if (string.Equals(SelectedDisplayItem?.Id, display.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ActivateDisplay(display.Id, resetHistory: true);
        SaveSessionState();
        SetStatusMessage($"正在编辑{display.Name}的分区布局");
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
        var resolvedDisplay = ResolveCurrentDisplay(displayId);
        if (resolvedDisplay is not null)
        {
            return resolvedDisplay;
        }

        return _displayDiscoveryService.GetPrimaryDisplay();
    }

    private static string GetDisplayTopologyFingerprint(IEnumerable<DisplayInfo> displays)
    {
        return string.Join(
            ";",
            displays
                .OrderBy(display => display.Id, StringComparer.OrdinalIgnoreCase)
                .Select(display => string.Join(
                    "|",
                    display.Id,
                    display.Bounds.X,
                    display.Bounds.Y,
                    display.Bounds.Width,
                    display.Bounds.Height,
                    display.IsPrimary,
                    display.Orientation)));
    }
}
