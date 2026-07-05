using PaneWorks.App.Diagnostics;
using PaneWorks.App.ViewModels;
using Forms = System.Windows.Forms;
using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;

namespace PaneWorks.App;

public partial class App : Wpf.Application
{
    private Forms.NotifyIcon? _notifyIcon;
    private WpfControls.ContextMenu? _trayContextMenu;
    private readonly System.Windows.Threading.DispatcherTimer _trayMenuOutsideClickTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(50)
    };
    private IReadOnlyList<LayoutListItemViewModel> _cachedTrayLayouts = Array.Empty<LayoutListItemViewModel>();
    private IReadOnlyList<LayoutListItemViewModel> _cachedTrayWorkspaceProfiles = Array.Empty<LayoutListItemViewModel>();
    private string _cachedActiveSnapLayoutId = string.Empty;
    private string _cachedActiveWorkspaceProfileId = string.Empty;
    private MainWindow? _mainWindow;
    private DateTimeOffset _trayMenuOutsideClickIgnoreUntil;
    private bool _isExitRequested;
    private bool _isForceExitScheduled;

    public App()
    {
        _trayMenuOutsideClickTimer.Tick += TrayMenuOutsideClickTimer_Tick;
    }

    protected override void OnStartup(Wpf.StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = Wpf.ShutdownMode.OnExplicitShutdown;
        PaneWorksLog.Info("App startup");
        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;
        InitializeNotifyIcon();
        _mainWindow.Show();
    }

    protected override void OnExit(Wpf.ExitEventArgs e)
    {
        DisposeTrayResources();

        base.OnExit(e);
    }

    public bool IsExitRequested => _isExitRequested;

    public void CancelExitRequest()
    {
        _isExitRequested = false;
    }

}
