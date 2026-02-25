using ugokuDeskTop.TrayIcon;

namespace ugokuDeskTop;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;
    private TrayIconManager? _trayManager;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        _mainWindow = new MainWindow();
        _mainWindow.Show();

        _trayManager = new TrayIconManager(_mainWindow, RequestShutdown);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _mainWindow?.StopAudioCapture();
        _mainWindow?.DisableApoFilter();
        _mainWindow?.DetachWallpaper();
        _trayManager?.Dispose();
        base.OnExit(e);
    }

    private void RequestShutdown()
    {
        Dispatcher.Invoke(() => Shutdown());
    }
}
