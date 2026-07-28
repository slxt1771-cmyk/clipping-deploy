using System.IO;
using ClippingSoftware.Core.Obs;
using ClippingSoftware.Core.Recording;
using ClippingSoftware.Data.ClipLibrary;
using ClippingSoftware.Data.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClippingSoftware.App.ViewModels;

/// <summary>
/// Backs the first-run setup wizard (see SetupWindow). Four steps, each one thing the app cannot work
/// out on its own and cannot usefully run without:
///   1. Welcome + install integrity (are the bundled ffmpeg tools actually there).
///   2. Where OBS Studio is (auto-detected, correctable).
///   3. The obs-websocket connection - above all the password, which OBS 28+ generates per machine and
///      enables auth for by default, so a fresh install can never connect until it's copied across.
///   4. Where clips go, then write everything and mark setup complete.
///
/// The wizard writes through <see cref="SettingsRepository"/> exactly once, at Finish - stepping forward
/// and back doesn't persist anything, so abandoning the wizard leaves no half-configured row behind.
/// </summary>
public partial class SetupViewModel : ObservableObject
{
    private readonly SettingsRepository _settingsRepository;
    private readonly AppSettings _settings;

    /// <summary>Highest step index, i.e. the Finish step. Steps are 0-based to match the visibility
    /// converter's parameter and the Back/Next bounds checks.</summary>
    public const int LastStep = 3;

    [ObservableProperty]
    private int _currentStep;

    [ObservableProperty]
    private string _obsExecutablePath = string.Empty;

    [ObservableProperty]
    private string _obsWebsocketHost = "localhost";

    [ObservableProperty]
    private int _obsWebsocketPort = 4455;

    [ObservableProperty]
    private string _obsWebsocketPassword = string.Empty;

    [ObservableProperty]
    private string _clipStorageFolder = string.Empty;

    [ObservableProperty]
    private string _exportStorageFolder = string.Empty;

    [ObservableProperty]
    private bool _autoLaunchObs = true;

    /// <summary>How many seconds a saved clip covers - the replay buffer's history window.</summary>
    [ObservableProperty]
    private int _replayBufferLengthSeconds = 60;

    /// <summary>Result line under the TEST CONNECTION button. Empty until a test has been run, so the
    /// step doesn't open with a scary-looking "not connected" the user hasn't earned yet.</summary>
    [ObservableProperty]
    private string _connectionTestResult = string.Empty;

    [ObservableProperty]
    private bool _isTestingConnection;

    /// <summary>Error text for the final step. Separate from ConnectionTestResult, which is rendered on
    /// the connection step - reusing that one meant a Finish failure was written to a message the user had
    /// already navigated away from and could not see.</summary>
    [ObservableProperty]
    private string _finishError = string.Empty;

    /// <summary>True when the bundled ffmpeg/ffprobe are missing - a broken install, surfaced on step 1
    /// rather than left to fail later as an unexplained "clip never appeared in the library".</summary>
    public bool IsFfmpegMissing { get; }

    public string ObsExecutableStatus => ObsInstallLocator.IsValidExecutablePath(ObsExecutablePath)
        ? "OBS Studio found."
        : "No obs64.exe at this path - use Browse to point at your OBS install.";

    public bool IsFirstStep => CurrentStep == 0;

    public bool IsLastStep => CurrentStep == LastStep;

    /// <summary>Header progress readout, 1-based for display ("STEP 2 / 4") over the 0-based CurrentStep
    /// the visibility converter matches on.</summary>
    public string StepLabel => $"STEP {CurrentStep + 1} / {LastStep + 1}";

    /// <summary>Inverse of <see cref="IsTestingConnection"/>, for the TEST CONNECTION button's IsEnabled -
    /// a plain bool, since IsEnabled can't take the visibility converters the rest of this view uses.</summary>
    public bool IsNotTestingConnection => !IsTestingConnection;

    /// <summary>Raised when the wizard is done and the window should close. The bool is what
    /// SetupWindow.DialogResult becomes: true = finished (settings saved), false = cancelled.</summary>
    public event Action<bool>? Completed;

    public SetupViewModel(SettingsRepository settingsRepository)
    {
        _settingsRepository = settingsRepository;
        _settings = settingsRepository.Load();

        IsFfmpegMissing = !FfmpegTools.AreAvailable();

        // Prefer a real detected install over whatever the stored/default path says - on a fresh install
        // the stored value is just AppSettings' guess at the standard location, which is exactly what
        // detection checks first anyway, so this only ever changes the answer when the guess was wrong.
        ObsExecutablePath = ObsInstallLocator.Locate() ?? _settings.ObsExecutablePath;

        ObsWebsocketHost = _settings.ObsWebsocketHost;
        ObsWebsocketPort = _settings.ObsWebsocketPort;
        ObsWebsocketPassword = _settings.ObsWebsocketPassword;
        ClipStorageFolder = _settings.ClipStorageFolder;
        ExportStorageFolder = _settings.ExportStorageFolder;
        AutoLaunchObs = _settings.AutoLaunchObs;
        ReplayBufferLengthSeconds = _settings.ReplayBufferLengthSeconds;
    }

    partial void OnObsExecutablePathChanged(string value) => OnPropertyChanged(nameof(ObsExecutableStatus));

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsFirstStep));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(StepLabel));
        BackCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsTestingConnectionChanged(bool value) => OnPropertyChanged(nameof(IsNotTestingConnection));

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => CurrentStep--;

    private bool CanGoBack() => CurrentStep > 0;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        if (CurrentStep < LastStep)
        {
            CurrentStep++;
        }
    }

    private bool CanGoNext() => CurrentStep < LastStep;

    /// <summary>
    /// Connects a throwaway <see cref="ObsController"/> and polls for success, rather than reusing the
    /// app's own: the wizard runs before MainViewModel exists, and a test that left a live connection
    /// behind would race the real one that opens moments later.
    ///
    /// Polling rather than awaiting a result is forced by ObsController.Connect being fire-and-forget
    /// (its underlying ConnectAsync reports success only via the Connected event) - the same shape
    /// MainViewModel.InitializeAsync already uses, just with a shorter budget since a human is waiting.
    /// </summary>
    [RelayCommand]
    private async Task TestConnection()
    {
        IsTestingConnection = true;
        ConnectionTestResult = "Connecting...";

        using var probe = new ObsController();
        try
        {
            // Checked regardless of the AutoLaunchObs preference: the wizard never launches OBS itself
            // (that only happens once the app proper starts), so "OBS is not running" is the accurate and
            // more actionable message either way - without it the user just sees a generic timeout and
            // starts second-guessing the password they only just pasted in.
            if (!new ObsProcessManager().IsObsRunning())
            {
                ConnectionTestResult = "OBS isn't running - start OBS Studio first, then test again.";
                return;
            }

            probe.Connect(ObsWebsocketHost, ObsWebsocketPort, ObsWebsocketPassword);

            for (var attempt = 0; attempt < 20 && !probe.IsConnected; attempt++)
            {
                await Task.Delay(250);
            }

            if (probe.IsConnected)
            {
                ConnectionTestResult = "Connected to OBS successfully.";
            }
            else
            {
                ConnectionTestResult =
                    "Could not connect. Check that WebSocket Server is enabled in OBS " +
                    "(Tools > WebSocket Server Settings) and that the port and password match.";
            }
        }
        catch (Exception ex)
        {
            ConnectionTestResult = $"Could not connect: {ex.Message}";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    /// <summary>
    /// Writes every collected value and marks setup complete. Storage folders are created here rather
    /// than lazily at first record: a folder the user can see and open immediately after setup is the
    /// point of having asked for it, and a path that can't be created (bad drive, no permission) should
    /// fail now, while the field to fix it is still on screen.
    /// </summary>
    [RelayCommand]
    private void Finish()
    {
        try
        {
            Directory.CreateDirectory(ClipStorageFolder);
            Directory.CreateDirectory(ExportStorageFolder);
        }
        catch (Exception ex)
        {
            FinishError = $"Could not create the clip folders: {ex.Message}";
            return;
        }

        _settings.ObsExecutablePath = ObsExecutablePath;
        _settings.ObsWebsocketHost = ObsWebsocketHost;
        _settings.ObsWebsocketPort = ObsWebsocketPort;
        _settings.ObsWebsocketPassword = ObsWebsocketPassword;
        _settings.ClipStorageFolder = ClipStorageFolder;
        _settings.ExportStorageFolder = ExportStorageFolder;
        _settings.AutoLaunchObs = AutoLaunchObs;
        // Guards a blank/zero field: the wizard's text box binds an int, and 0 would tell OBS to keep no
        // history at all, making every saved clip empty.
        _settings.ReplayBufferLengthSeconds = Math.Max(1, ReplayBufferLengthSeconds);
        _settings.HasCompletedFirstRunSetup = true;

        _settingsRepository.Save(_settings);
        Completed?.Invoke(true);
    }

    /// <summary>Closes the wizard without saving. HasCompletedFirstRunSetup stays false, so setup runs
    /// again next launch rather than dropping the user into an unconfigured app permanently.</summary>
    [RelayCommand]
    private void Cancel() => Completed?.Invoke(false);
}
