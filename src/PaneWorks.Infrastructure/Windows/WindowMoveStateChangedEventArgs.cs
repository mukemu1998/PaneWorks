namespace PaneWorks.Infrastructure.Windows;

public sealed class WindowMoveStateChangedEventArgs : EventArgs
{
    public WindowMoveStateChangedEventArgs(IntPtr windowHandle)
    {
        WindowHandle = windowHandle;
    }

    public IntPtr WindowHandle { get; }
}
