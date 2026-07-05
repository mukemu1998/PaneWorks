using PaneWorks.Core.Models;
using WpfApplication = System.Windows.Application;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private bool TrySaveActiveWorkspaceProfileDocument(out string message)
    {
        message = string.Empty;
        if (!IsWorkspaceProfileEnabled
            || _activeWorkspaceProfileDocument is null
            || string.IsNullOrWhiteSpace(_activeWorkspaceProfileId))
        {
            message = "当前没有可保存的工作区。";
            return false;
        }

        try
        {
            var normalizedProfile = NormalizeWorkspaceProfile(_activeWorkspaceProfileDocument);
            _workspaceProfileRepository
                .SaveAsync(_activeWorkspaceProfileId, normalizedProfile, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            SetActiveWorkspaceProfile(_activeWorkspaceProfileId, normalizedProfile.Name, normalizedProfile);
            RefreshWorkspaceProfiles();
            SelectedWorkspaceProfileItem = WorkspaceProfiles.FirstOrDefault(item =>
                string.Equals(item.Id, _activeWorkspaceProfileId, StringComparison.OrdinalIgnoreCase));
            SaveSessionState();
            SetStatusMessage($"工作区“{normalizedProfile.Name}”已保存");
            return true;
        }
        catch (Exception ex)
        {
            message = $"保存工作区失败：{ex.Message}";
            return false;
        }
    }

    private void SaveWorkspaceProfileDocumentInBackground(
        string profileId,
        WorkspaceProfileDocument profile,
        string successMessage)
    {
        var normalizedProfile = NormalizeWorkspaceProfile(profile);
        _ = Task.Run(() =>
        {
            try
            {
                _workspaceProfileRepository
                    .SaveAsync(profileId, normalizedProfile, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                return (Exception?)null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }).ContinueWith(task =>
        {
            var dispatcher = WpfApplication.Current?.Dispatcher;
            if (dispatcher is null)
            {
                return;
            }

            dispatcher.BeginInvoke(() =>
            {
                if (task.Result is { } exception)
                {
                    SetStatusMessage($"后台保存工作区失败：{exception.Message}");
                    return;
                }

                RefreshWorkspaceProfiles();
                SelectedWorkspaceProfileItem = WorkspaceProfiles.FirstOrDefault(item =>
                    string.Equals(item.Id, profileId, StringComparison.OrdinalIgnoreCase));
                SaveSessionState();
                SetStatusMessage(successMessage);
            });
        }, TaskScheduler.Default);
    }
}
