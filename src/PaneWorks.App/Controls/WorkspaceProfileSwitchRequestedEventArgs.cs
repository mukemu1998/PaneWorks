namespace PaneWorks.App.Controls;

public sealed class WorkspaceProfileSwitchRequestedEventArgs : EventArgs
{
    public WorkspaceProfileSwitchRequestedEventArgs(string profileId)
    {
        ProfileId = profileId;
    }

    public string ProfileId { get; }
}
