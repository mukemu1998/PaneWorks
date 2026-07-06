using PaneWorks.Core.Models;

namespace PaneWorks.App;

internal enum BindingRegionResolveFailure
{
    None,
    DisplayMissing,
    RegionMissing
}

internal readonly record struct BindingRegionResolveResult(
    bool Success,
    ComputedRegion? Region,
    BindingRegionResolveFailure Failure)
{
    public static BindingRegionResolveResult Found(ComputedRegion region)
    {
        return new BindingRegionResolveResult(true, region, BindingRegionResolveFailure.None);
    }

    public static BindingRegionResolveResult Failed(BindingRegionResolveFailure failure)
    {
        return new BindingRegionResolveResult(false, null, failure);
    }
}
