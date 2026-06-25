using System.Runtime.InteropServices;

namespace PaneWorks.Infrastructure.Windows;

public sealed class WindowMoveMonitor : IDisposable
{
    private const uint EventSystemMoveSizeStart = 0x000A;
    private const uint EventSystemMoveSizeEnd = 0x000B;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;

    private readonly WinEventProc _callback;
    private IntPtr _moveStartHook;
    private IntPtr _moveEndHook;

    public WindowMoveMonitor()
    {
        _callback = HandleWinEvent;
    }

    public event EventHandler<WindowMoveStateChangedEventArgs>? MoveStarted;
    public event EventHandler<WindowMoveStateChangedEventArgs>? MoveEnded;

    public bool IsRunning => _moveStartHook != IntPtr.Zero && _moveEndHook != IntPtr.Zero;

    public void Start()
    {
        if (_moveStartHook != IntPtr.Zero || _moveEndHook != IntPtr.Zero)
        {
            return;
        }

        _moveStartHook = SetWinEventHook(
            EventSystemMoveSizeStart,
            EventSystemMoveSizeStart,
            IntPtr.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext | WineventSkipOwnProcess);

        _moveEndHook = SetWinEventHook(
            EventSystemMoveSizeEnd,
            EventSystemMoveSizeEnd,
            IntPtr.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext | WineventSkipOwnProcess);

        if (!IsRunning)
        {
            Stop();
        }
    }

    public void Stop()
    {
        if (_moveStartHook != IntPtr.Zero)
        {
            UnhookWinEvent(_moveStartHook);
            _moveStartHook = IntPtr.Zero;
        }

        if (_moveEndHook != IntPtr.Zero)
        {
            UnhookWinEvent(_moveEndHook);
            _moveEndHook = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private void HandleWinEvent(
        IntPtr hWinEventHook,
        uint @event,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero || idObject != 0 || idChild != 0)
        {
            return;
        }

        var args = new WindowMoveStateChangedEventArgs(hwnd);
        if (@event == EventSystemMoveSizeStart)
        {
            MoveStarted?.Invoke(this, args);
            return;
        }

        if (@event == EventSystemMoveSizeEnd)
        {
            MoveEnded?.Invoke(this, args);
        }
    }

    private delegate void WinEventProc(
        IntPtr hWinEventHook,
        uint @event,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventProc lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
}
