namespace PaneWorks.App;

public partial class App
{
    private void InvokeOnMainWindow(Action action)
    {
        if (_mainWindow is null)
        {
            return;
        }

        if (_mainWindow.Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _mainWindow.Dispatcher.Invoke(action);
    }

    private void BeginInvokeOnMainWindow(Action action)
    {
        if (_mainWindow is null)
        {
            return;
        }

        if (_mainWindow.Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _mainWindow.Dispatcher.BeginInvoke(action);
    }
}
