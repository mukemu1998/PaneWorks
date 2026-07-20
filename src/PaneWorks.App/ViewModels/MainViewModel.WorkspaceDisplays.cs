using System;
using System.Collections.Generic;
using System.Linq;
using PaneWorks.Core.Models;
using PaneWorks.Infrastructure.Windows;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private const string DisplayLayoutKeySeparator = "::";

    private WorkspaceLayoutDocument CreateWorkspaceDocument(string name)
    {
        var normalizedName = NormalizeLayoutName(name);
        var layouts = new Dictionary<string, LayoutDocument>(StringComparer.OrdinalIgnoreCase);

        foreach (var display in _displaysById.Values)
        {
            layouts[GetDisplayLayoutKey(display)] = _editorService.CreateBlank(normalizedName);
        }

        return new WorkspaceLayoutDocument(
            2,
            normalizedName,
            layouts,
            CreateCurrentDisplaySnapshots());
    }

    private WorkspaceLayoutDocument NormalizeWorkspaceForCurrentDisplays(WorkspaceLayoutDocument workspace)
    {
        var normalizedName = NormalizeLayoutName(workspace.Name);
        var layouts = workspace.DisplayLayouts is null
            ? new Dictionary<string, LayoutDocument>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, LayoutDocument>(workspace.DisplayLayouts, StringComparer.OrdinalIgnoreCase);
        var snapshots = workspace.DisplaySnapshots is null
            ? new Dictionary<string, WorkspaceDisplaySnapshot>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, WorkspaceDisplaySnapshot>(workspace.DisplaySnapshots, StringComparer.OrdinalIgnoreCase);

        MigrateLegacyDisplayEntries(layouts, snapshots, normalizedName);

        return new WorkspaceLayoutDocument(
            Math.Max(2, workspace.Version),
            normalizedName,
            layouts.ToDictionary(
                entry => entry.Key,
                entry => entry.Value with { Name = normalizedName },
                StringComparer.OrdinalIgnoreCase),
            MergeCurrentDisplaySnapshots(snapshots));
    }

    private LayoutDocument GetDisplayLayout(WorkspaceLayoutDocument workspace, string? displayId)
    {
        workspace = NormalizeWorkspaceForCurrentDisplays(workspace);
        var display = ResolveCurrentDisplay(displayId) ?? GetPrimaryDisplay();
        var layoutKey = GetDisplayLayoutKey(display);

        if (workspace.DisplayLayouts.TryGetValue(layoutKey, out var exactLayout))
        {
            return exactLayout;
        }

        var orientationFallback = workspace.DisplayLayouts
            .Where(entry => IsLayoutForDisplay(entry.Key, display.Id))
            .Select(entry => entry.Value)
            .FirstOrDefault();

        return orientationFallback ?? _editorService.CreateBlank(workspace.Name);
    }

    private WorkspaceLayoutDocument ReplaceDisplayLayout(WorkspaceLayoutDocument workspace, string? displayId, LayoutDocument document)
    {
        workspace = NormalizeWorkspaceForCurrentDisplays(workspace);
        var display = ResolveCurrentDisplay(displayId) ?? GetPrimaryDisplay();
        var layouts = new Dictionary<string, LayoutDocument>(workspace.DisplayLayouts, StringComparer.OrdinalIgnoreCase)
        {
            [GetDisplayLayoutKey(display)] = document
        };

        return workspace with
        {
            DisplayLayouts = layouts,
            DisplaySnapshots = MergeCurrentDisplaySnapshots(workspace.DisplaySnapshots)
        };
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
            DisplayLayouts = layouts,
            DisplaySnapshots = MergeCurrentDisplaySnapshots(workspace.DisplaySnapshots)
        };
    }

    private DisplayInfo? ResolveCurrentDisplay(string? displayId)
    {
        if (string.IsNullOrWhiteSpace(displayId))
        {
            return null;
        }

        if (_displaysById.TryGetValue(displayId, out var directDisplay))
        {
            return directDisplay;
        }

        if (TryGetDisplayIdFromLayoutKey(displayId, out var keyedDisplayId)
            && _displaysById.TryGetValue(keyedDisplayId, out var keyedDisplay))
        {
            return keyedDisplay;
        }

        var snapshot = _currentWorkspaceDocument.DisplaySnapshots?
            .FirstOrDefault(entry =>
                string.Equals(entry.Key, displayId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Value.DeviceName, displayId, StringComparison.OrdinalIgnoreCase))
            .Value;
        if (!string.IsNullOrWhiteSpace(snapshot?.PhysicalId)
            && _displaysById.TryGetValue(snapshot.PhysicalId, out var snapshotDisplay))
        {
            return snapshotDisplay;
        }

        return _displaysById.Values.FirstOrDefault(display =>
            string.Equals(display.DeviceName, displayId, StringComparison.OrdinalIgnoreCase));
    }

    private DisplayInfo GetPrimaryDisplay()
    {
        return _displaysById.Values.FirstOrDefault(display => display.IsPrimary)
            ?? _displaysById.Values.First();
    }

    private Dictionary<string, WorkspaceDisplaySnapshot> CreateCurrentDisplaySnapshots()
    {
        return _displaysById.Values.ToDictionary(
            GetDisplayLayoutKey,
            CreateDisplaySnapshot,
            StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, WorkspaceDisplaySnapshot> MergeCurrentDisplaySnapshots(
        IReadOnlyDictionary<string, WorkspaceDisplaySnapshot>? existingSnapshots)
    {
        var snapshots = existingSnapshots is null
            ? new Dictionary<string, WorkspaceDisplaySnapshot>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, WorkspaceDisplaySnapshot>(existingSnapshots, StringComparer.OrdinalIgnoreCase);

        foreach (var display in _displaysById.Values)
        {
            snapshots[GetDisplayLayoutKey(display)] = CreateDisplaySnapshot(display);
        }

        return snapshots;
    }

    private static WorkspaceDisplaySnapshot CreateDisplaySnapshot(DisplayInfo display)
    {
        return new WorkspaceDisplaySnapshot(
            display.DeviceName,
            display.Bounds,
            display.IsPrimary,
            display.Id,
            display.Orientation);
    }

    private void MigrateLegacyDisplayEntries(
        Dictionary<string, LayoutDocument> layouts,
        Dictionary<string, WorkspaceDisplaySnapshot> snapshots,
        string normalizedName)
    {
        if (layouts.Count == 0 || _displaysById.Count == 0)
        {
            return;
        }

        var currentDisplays = _displaysById.Values.ToList();
        var canUseTopologyFallback = CanUseLegacyTopologyFallback(layouts, snapshots, currentDisplays);

        foreach (var entry in layouts.ToArray())
        {
            if (TryGetDisplayIdFromLayoutKey(entry.Key, out _))
            {
                continue;
            }

            snapshots.TryGetValue(entry.Key, out var snapshot);
            var targetDisplay = ResolveLegacyDisplayTarget(
                entry.Key,
                snapshot,
                currentDisplays,
                canUseTopologyFallback);
            if (targetDisplay is null)
            {
                continue;
            }

            var orientation = GetSnapshotOrientation(snapshot);
            var targetKey = GetDisplayLayoutKey(targetDisplay.Id, orientation);
            if (layouts.ContainsKey(targetKey))
            {
                continue;
            }

            layouts.Remove(entry.Key);
            layouts[targetKey] = entry.Value with { Name = normalizedName };

            if (snapshot is not null)
            {
                snapshots.Remove(entry.Key);
                snapshots[targetKey] = snapshot with
                {
                    PhysicalId = targetDisplay.Id,
                    Orientation = orientation
                };
            }
            else
            {
                snapshots[targetKey] = CreateDisplaySnapshot(targetDisplay) with { Orientation = orientation };
            }
        }
    }

    private static bool CanUseLegacyTopologyFallback(
        IReadOnlyDictionary<string, LayoutDocument> layouts,
        IReadOnlyDictionary<string, WorkspaceDisplaySnapshot> snapshots,
        IReadOnlyCollection<DisplayInfo> currentDisplays)
    {
        var legacyEntries = layouts.Keys.Count(key => !TryGetDisplayIdFromLayoutKey(key, out _));
        var snapshotDisplays = snapshots
            .Where(entry => !TryGetDisplayIdFromLayoutKey(entry.Key, out _))
            .Select(entry => entry.Value.DeviceName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return legacyEntries > 0
            && snapshotDisplays == legacyEntries
            && legacyEntries == currentDisplays.Count;
    }

    private static DisplayInfo? ResolveLegacyDisplayTarget(
        string storedDisplayId,
        WorkspaceDisplaySnapshot? snapshot,
        IReadOnlyList<DisplayInfo> currentDisplays,
        bool canUseTopologyFallback)
    {
        if (snapshot is not null && !string.IsNullOrWhiteSpace(snapshot.PhysicalId))
        {
            return currentDisplays.FirstOrDefault(display =>
                string.Equals(display.Id, snapshot.PhysicalId, StringComparison.OrdinalIgnoreCase));
        }

        var exactDeviceNameMatch = canUseTopologyFallback
            ? currentDisplays.FirstOrDefault(display =>
                string.Equals(display.DeviceName, storedDisplayId, StringComparison.OrdinalIgnoreCase))
            : null;
        if (exactDeviceNameMatch is not null)
        {
            return exactDeviceNameMatch;
        }

        if (string.Equals(storedDisplayId, "__legacy__", StringComparison.OrdinalIgnoreCase)
            && currentDisplays.Count == 1)
        {
            return currentDisplays[0];
        }

        if (snapshot is null || !canUseTopologyFallback)
        {
            return null;
        }

        var candidate = currentDisplays
            .Select(display => new
            {
                Display = display,
                Score = GetLegacyDisplayMatchScore(display, snapshot),
                Distance = GetDisplayMatchDistance(display.Bounds, snapshot.Bounds)
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Distance)
            .FirstOrDefault();

        return candidate is not null && candidate.Score >= 500
            ? candidate.Display
            : null;
    }

    private static int GetLegacyDisplayMatchScore(DisplayInfo display, WorkspaceDisplaySnapshot snapshot)
    {
        var score = 0;

        if (AreBoundsEquivalent(display.Bounds, snapshot.Bounds))
        {
            score += 1000;
        }
        else
        {
            if (AreOriginsEquivalent(display.Bounds, snapshot.Bounds))
            {
                score += 420;
            }

            if (AreSizesEquivalent(display.Bounds, snapshot.Bounds))
            {
                score += 220;
            }
        }

        if (string.Equals(display.DeviceName, snapshot.DeviceName, StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (display.IsPrimary == snapshot.IsPrimary)
        {
            score += 25;
        }

        return score;
    }

    private static WorkspaceDisplayOrientation GetSnapshotOrientation(WorkspaceDisplaySnapshot? snapshot)
    {
        if (snapshot?.Orientation is { } orientation)
        {
            return orientation;
        }

        return snapshot is not null && snapshot.Bounds.Height > snapshot.Bounds.Width
            ? WorkspaceDisplayOrientation.Portrait
            : WorkspaceDisplayOrientation.Landscape;
    }

    private static string GetDisplayLayoutKey(DisplayInfo display)
    {
        return GetDisplayLayoutKey(display.Id, display.Orientation);
    }

    private static string GetDisplayLayoutKey(string displayId, WorkspaceDisplayOrientation orientation)
    {
        return $"{displayId}{DisplayLayoutKeySeparator}{orientation}";
    }

    private static bool IsLayoutForDisplay(string layoutKey, string displayId)
    {
        return TryGetDisplayIdFromLayoutKey(layoutKey, out var keyedDisplayId)
            && string.Equals(keyedDisplayId, displayId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetDisplayIdFromLayoutKey(string layoutKey, out string displayId)
    {
        var separatorIndex = layoutKey.LastIndexOf(DisplayLayoutKeySeparator, StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            displayId = string.Empty;
            return false;
        }

        var orientationText = layoutKey[(separatorIndex + DisplayLayoutKeySeparator.Length)..];
        if (!Enum.TryParse<WorkspaceDisplayOrientation>(orientationText, ignoreCase: true, out _))
        {
            displayId = string.Empty;
            return false;
        }

        displayId = layoutKey[..separatorIndex];
        return true;
    }

    private static bool AreBoundsEquivalent(PaneRect left, PaneRect right)
    {
        return AreNearlyEqual(left.X, right.X)
            && AreNearlyEqual(left.Y, right.Y)
            && AreNearlyEqual(left.Width, right.Width)
            && AreNearlyEqual(left.Height, right.Height);
    }

    private static bool AreOriginsEquivalent(PaneRect left, PaneRect right)
    {
        return AreNearlyEqual(left.X, right.X)
            && AreNearlyEqual(left.Y, right.Y);
    }

    private static bool AreSizesEquivalent(PaneRect left, PaneRect right)
    {
        return AreNearlyEqual(left.Width, right.Width)
            && AreNearlyEqual(left.Height, right.Height);
    }

    private static bool AreNearlyEqual(double left, double right)
    {
        return Math.Abs(left - right) <= 1;
    }

    private static double GetDisplayMatchDistance(PaneRect left, PaneRect right)
    {
        return Math.Abs(left.X - right.X)
            + Math.Abs(left.Y - right.Y)
            + Math.Abs(left.Width - right.Width)
            + Math.Abs(left.Height - right.Height);
    }
}
