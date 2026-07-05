using System.Linq;
using PaneWorks.Core.Models;
using PaneWorks.Core.Services;
using PaneWorks.Infrastructure.Persistence;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private void SaveSessionState()
    {
        _sessionStateRepository.Save(new SessionState
        {
            LastLayoutId = _currentLayoutId,
            LastSnapLayoutId = _activeSnapLayoutId,
            LastWorkspaceProfileId = _activeWorkspaceProfileId ?? SelectedWorkspaceProfileItem?.Id,
            SelectedDisplayId = SelectedDisplayItem?.Id
        });
    }

    private void SetStatusMessage(string message)
    {
        _lastStatusMessage = message;
        RaisePropertyChanged(nameof(StatusLine));
    }

    public void SetUserStatusMessage(string message)
    {
        SetStatusMessage(message);
    }

    private void RaiseWindowBindingStatusChanged()
    {
        RaisePropertyChanged(nameof(SelectedRegionBindingSummary));
        RaisePropertyChanged(nameof(SelectedRegionBindingDescription));
        RaisePropertyChanged(nameof(IsCurrentLayoutDrivingSnapLayout));
        RaisePropertyChanged(nameof(ActiveWorkspaceWindowBindings));
    }

    private static string NormalizeLayoutName(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "未命名布局" : value.Trim();
    }

    private static string Slugify(string value)
    {
        var cleaned = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ch == ' ' || ch == '-' || ch == '_' ? '-' : '\0')
            .Where(ch => ch != '\0')
            .ToArray());

        while (cleaned.Contains("--", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);
        }

        cleaned = cleaned.Trim('-');
        return string.IsNullOrWhiteSpace(cleaned) ? "layout" : cleaned;
    }

    private static List<WorkspaceWindowBinding> NormalizeWindowBindings(IEnumerable<WorkspaceWindowBinding>? bindings)
    {
        return WorkspaceWindowBindingNormalizer.NormalizeMany(bindings);
    }

    private sealed record PersistedWorkspaceState(WorkspaceLayoutDocument WorkspaceDocument, string? LayoutId);
}
