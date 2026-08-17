using System.Windows;
using Velopack;

namespace ClippingSoftware.App;

public partial class App : Application
{
    private TrayIconController? _trayIconController;

    /// <summary>
    /// WPF normally generates Main() for you; ClippingSoftware.App.csproj's &lt;StartupObject&gt; disables
    /// that so VelopackApp.Build().Run() can run as the very first thing at process startup, before WPF
    /// itself initializes - required by Velopack so it can intercept the special command-line args it uses
    /// internally during install/update/uninstall. In normal runs (not one of those internal operations)
    /// this returns immediately and app startup continues as usual below.
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            VelopackApp.Build().Run();

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
        catch (Exception ex)
        {
            // Nothing else exists yet to show this to the user (no window, no logger) - this only fires
            // for a genuinely unexpected startup failure, not the normal path.
            MessageBox.Show("Clipping Software failed to start: " + ex, "Startup Error");
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();

        var viewModel = mainWindow.ViewModel;
        viewModel.UpdateReadyToInstall += OnUpdateReadyToInstall;

        _trayIconController = new TrayIconController(
            onStartRecording: () => viewModel.StartRecordCommand.Execute(null),
            onStopRecording: () => viewModel.StopRecordCommand.Execute(null),
            onSaveReplayBuffer: () => viewModel.SaveReplayBufferSafe(),
            onExit: () => mainWindow.RequestExit(),
            onShowWindow: () => mainWindow.ShowAndActivate());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconController?.Dispose();
        (MainWindow as MainWindow)?.ViewModel.Shutdown();
        base.OnExit(e);
    }

    /// <summary>
    /// UpdateManager.ApplyUpdatesAndRestart force-exits the process itself rather than going through WPF's
    /// normal Application.Shutdown()/window-Closing path, so OnExit above never runs for it - Velopack's
    /// own doc comment on that method says to clean up state before calling it, which is exactly what
    /// OnExit already knows how to do. Same two cleanup calls, called manually here instead of via a
    /// window close, then MainViewModel.ApplyPendingUpdate() actually terminates and relaunches the app.
    /// </summary>
    private void OnUpdateReadyToInstall()
    {
        _trayIconController?.Dispose();
        var viewModel = (MainWindow as MainWindow)?.ViewModel;
        viewModel?.Shutdown();
        viewModel?.ApplyPendingUpdate();
    }
}
