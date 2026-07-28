using System.IO;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;

namespace ClippingSoftware.App;

/// <summary>
/// Owns the system tray icon and its right-click context menu (Open, Start/Stop Recording, Save Replay
/// Buffer, Exit). All behavior is supplied by the caller as delegates, so this class has no
/// dependency on MainViewModel/DI wiring — construct it once (e.g. from App.xaml.cs OnStartup, or
/// from MainWindow after construction) and call Dispose() on app shutdown to remove the icon.
/// Double-clicking the icon also runs onShowWindow - the way back in now that the titlebar X minimizes
/// to tray instead of exiting (see MainWindow's Closing handler).
///
/// Usage:
/// <code>
/// _trayIconController = new TrayIconController(
///     onStartRecording: () => viewModel.StartRecordingCommand.Execute(null),
///     onStopRecording: () => viewModel.StopRecordingCommand.Execute(null),
///     onSaveReplayBuffer: () => obsController.SaveReplayBuffer(),
///     onExit: () => mainWindow.RequestExit(),
///     onShowWindow: () => mainWindow.ShowAndActivate());
/// </code>
/// </summary>
public class TrayIconController : IDisposable
{
    private readonly TaskbarIcon _taskbarIcon;
    private bool _disposed;

    public TrayIconController(
        Action onStartRecording,
        Action onStopRecording,
        Action onSaveReplayBuffer,
        Action onExit,
        Action onShowWindow,
        string tooltipText = "Clipping Software")
    {
        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = tooltipText,
            Icon = LoadTrayIcon(),
            ContextMenu = BuildContextMenu(onStartRecording, onStopRecording, onSaveReplayBuffer, onExit, onShowWindow),
            Visibility = Visibility.Visible,
        };

        // The X button now minimizes to tray (see MainWindow's Closing handler) rather than exiting, so
        // double-click needs to be the way back in - the same convention every other tray-resident app
        // uses, and without it the window would be reachable only via the "Open" menu item below.
        _taskbarIcon.TrayMouseDoubleClick += (_, _) => onShowWindow();
    }

    private static ContextMenu BuildContextMenu(
        Action onStartRecording, Action onStopRecording, Action onSaveReplayBuffer, Action onExit, Action onShowWindow)
    {
        var menu = new ContextMenu();

        var openItem = new MenuItem { Header = "Open", FontWeight = FontWeights.Bold };
        openItem.Click += (_, _) => onShowWindow();

        var startRecordingItem = new MenuItem { Header = "Start Recording" };
        startRecordingItem.Click += (_, _) => onStartRecording();

        var stopRecordingItem = new MenuItem { Header = "Stop Recording" };
        stopRecordingItem.Click += (_, _) => onStopRecording();

        var saveReplayBufferItem = new MenuItem { Header = "Save Replay Buffer" };
        saveReplayBufferItem.Click += (_, _) => onSaveReplayBuffer();

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => onExit();

        menu.Items.Add(openItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(startRecordingItem);
        menu.Items.Add(stopRecordingItem);
        menu.Items.Add(saveReplayBufferItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        return menu;
    }

    /// <summary>
    /// Loads Assets/tray.ico from the app's output directory (added to the csproj with
    /// CopyToOutputDirectory). A placeholder icon — swap the file for real artwork later; no code
    /// change needed since the path is fixed.
    /// </summary>
    private static System.Drawing.Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tray.ico");
        return new System.Drawing.Icon(iconPath);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _taskbarIcon.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
