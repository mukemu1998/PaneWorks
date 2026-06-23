namespace PaneWorks.Core.Models;

public sealed record LayoutGeometryResult(
    IReadOnlyList<ComputedRegion> Regions,
    IReadOnlyList<ComputedSplitter> Splitters);

