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
