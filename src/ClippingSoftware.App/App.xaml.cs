using System.Windows;

namespace ClippingSoftware.App;

public partial class App : Application
{
    private TrayIconController? _trayIconController;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconController?.Dispose();
        (MainWindow as MainWindow)?.ViewModel.Shutdown();
        base.OnExit(e);
    }
}
