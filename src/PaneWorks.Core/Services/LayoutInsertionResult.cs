using PaneWorks.Core.Models;

namespace PaneWorks.Core.Services;

public sealed record LayoutInsertionResult(
    LayoutDocument Document,
    string InsertedNodeId,
    bool Changed);
