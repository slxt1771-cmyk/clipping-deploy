using System.Windows;
using ClippingSoftware.App.Views;

namespace ClippingSoftware.App;

public partial class App : Application
{
    private TrayIconController? _trayIconController;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!RunFirstRunSetup())
        {
            Shutdown();
            return;
        }

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();

        var viewModel = mainWindow.ViewModel;

        _trayIconController = new TrayIconController(
            onStartRecording: () => viewModel.StartRecordCommand.Execute(null),
            onStopRecording: () => viewModel.StopRecordCommand.Execute(null),
            onSaveReplayBuffer: () => viewModel.SaveReplayBufferSafe(),
            onExit: () => mainWindow.RequestExit(),
            onShowWindow: () => mainWindow.ShowAndActivate());
    }

    /// <summary>
    /// Runs the first-run wizard (a no-op on an already-configured install) and reports whether startup
    /// should continue.
    ///
    /// ShutdownMode is flipped to OnExplicitShutdown for the duration: the default OnLastWindowClose would
    /// treat the wizard closing as "the app's last window is gone" and tear the process down before
    /// MainWindow is ever constructed. It's restored immediately afterward so the normal exit path
    /// (MainWindow closing for real - see its Closing handler) still ends the process as it always did.
    /// </summary>
    private bool RunFirstRunSetup()
    {
        var previousShutdownMode = ShutdownMode;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            return SetupWindow.RunIfNeeded();
        }
        finally
        {
            ShutdownMode = previousShutdownMode;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconController?.Dispose();
        (MainWindow as MainWindow)?.ViewModel.Shutdown();
        base.OnExit(e);
    }
}
