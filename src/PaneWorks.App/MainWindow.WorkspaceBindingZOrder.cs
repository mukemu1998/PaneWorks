namespace PaneWorks.App;

public partial class MainWindow
{
    private static Dictionary<IntPtr, int> BuildDesktopZOrderRank()
    {
        var ranks = new Dictionary<IntPtr, int>();
        var windowHandle = GetTopWindow(IntPtr.Zero);
        var rank = 0;

        while (windowHandle != IntPtr.Zero && rank < 20000)
        {
            if (!ranks.ContainsKey(windowHandle))
            {
                ranks[windowHandle] = rank;
                rank++;
            }

            windowHandle = GetWindow(windowHandle, GetWindowNext);
        }

        return ranks;
    }

    private static int GetDesktopZOrderRank(
        IntPtr windowHandle,
        IReadOnlyDictionary<IntPtr, int> zOrderRanks)
    {
        return zOrderRanks.TryGetValue(windowHandle, out var rank)
            ? rank
            : int.MaxValue;
    }
}
