using PaneWorks.Core.Models;

namespace PaneWorks.App.Controls;

public sealed record EditorReferenceLayout(
    string DisplayId,
    LayoutDocument Document,
    PaneRect StageBounds);
