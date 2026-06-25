using System.Windows;
using System.Windows.Interop;
using PaneWorks.Core.Models;

namespace PaneWorks.App;

public partial class SnapOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;

    public SnapOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += SnapOverlayWindow_SourceInitialized;
    }

    public LayoutDocument? Document
    {
        get => OverlayCanvas.Document;
        set => OverlayCanvas.Document = value;
    }

    public string? PreviewNodeId
    {
        get => OverlayCanvas.PreviewNodeId;
        set => OverlayCanvas.PreviewNodeId = value;
    }

    public PaneRect StageBounds
    {
        get => OverlayCanvas.StageBounds;
        set => OverlayCanvas.StageBounds = value;
    }

    private void SnapOverlayWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, extendedStyle | WsExTransparent | WsExToolWindow);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
