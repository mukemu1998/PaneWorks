namespace PaneWorks.Infrastructure.Windows;

public sealed record WorkspaceApplyResult(
    int RegionCount,
    int CandidateWindowCount,
    int AppliedWindowCount,
    int UnusedRegionCount,
    int SkippedWindowCount);
