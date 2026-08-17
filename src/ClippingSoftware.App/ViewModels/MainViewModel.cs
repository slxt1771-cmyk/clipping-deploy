using System.Collections.ObjectModel;
using ClippingSoftware.App.Theming;
using ClippingSoftware.App.Views;
using ClippingSoftware.Core.GameDetection;
using ClippingSoftware.Core.Notifications;
using ClippingSoftware.Core.Obs;
using ClippingSoftware.Core.ProfileManager;
using ClippingSoftware.Core.Recording;
using ClippingSoftware.Data;
using ClippingSoftware.Data.ClipLibrary;
using ClippingSoftware.Data.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClippingSoftware.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ObsProcessManager _processManager = new();
    private readonly ObsController _obsController = new();
    private readonly Database _database = new();
    private readonly SettingsRepository _settingsRepository;
    private readonly ClipRepository _clipRepository;
    private readonly TagRepository _tagRepository;
    private readonly GameProfileRepository _gameProfileRepository;
    private readonly AudioSourceRepository _audioSourceRepository;
    private readonly AudioSourceManager _audioSourceManager;
    private readonly GameDatabase _gameDatabase = new();
    private readonly RecordingIngestCoordinator _ingestCoordinator;
    private readonly GameDetectionService _gameDetectionService;
    private readonly ProfileApplier _profileApplier;
    private readonly SoundCuePlayer _soundCuePlayer = new();
    private readonly ClipEditDraftRepository _draftRepository;
    private readonly SequenceRepository _sequenceRepository;

    /// <summary>Periodically re-resolves app-audio source window targets and picks up newly-launched preset
    /// apps (see AudioSourceManager.RefreshAppSourceTargets/EnsurePresetSources) - a backgrounded app like
    /// Spotify can close, relaunch, or just change its window title (every song) without ever triggering a
    /// foreground-change event, so this can't be driven off GameDetectionService's WinEvent hook the way the
    /// Game source is. A plain System.Threading.Timer (not DispatcherTimer) so this round-trips to OBS on a
    /// thread-pool thread instead of the UI thread - nothing it does touches WPF objects directly, it only
    /// reaches the UI indirectly via AudioSourceManager.SourcesChanged, which is already dispatcher-marshaled
    /// in the subscription above. 3 minutes rather than something snappier since none of this is
    /// time-critical (a stale app-audio target sitting wrong for a few extra minutes is harmless) and the
    /// whole point is minimizing steady-state background work while a game's running. Started/stopped via
    /// Change() alongside Connected/Disconnected below; constructed idle (Timeout.Infinite) since nothing
    /// should run before the first connect.</summary>
    private readonly Timer _audioSourceRefreshTimer;

    private static readonly TimeSpan AudioSourceRefreshInterval = TimeSpan.FromMinutes(3);

    public ObsController ObsController => _obsController;

    public AppSettings Settings { get; private set; }

    public ClipBrowserViewModel ClipBrowser { get; }

    public GameProfilesViewModel GameProfiles { get; }

    /// <summary>Live monitor list for the Settings tab's CAPTURE SOURCE monitor picker (M7). Refreshed on
    /// connect and via RefreshCaptureOptionsCommand.</summary>
    public ObservableCollection<CaptureOptionItem> MonitorOptions { get; } = [];

    /// <summary>Live running-window list, shared by the Settings tab's CAPTURE SOURCE window-target picker
    /// (M7) and its "add app audio source" picker (M8) - both read the same OBS-surfaced window list.</summary>
    public ObservableCollection<CaptureOptionItem> WindowOptions { get; } = [];

    /// <summary>Rows for the Settings tab's "AUDIO SOURCES" list (M8): the two permanent built-ins plus any
    /// per-app sources, rebuilt whenever AudioSourceManager.SourcesChanged fires.</summary>
    public ObservableCollection<AudioSourceRowViewModel> AudioSources { get; } = [];

    [ObservableProperty]
    private string _obsHost = "localhost";

    [ObservableProperty]
    private int _obsPort = 4455;

    [ObservableProperty]
    private string _obsPassword = string.Empty;

    [ObservableProperty]
    private string _clipStorageFolder = string.Empty;

    /// <summary>How many seconds of history the replay buffer keeps, i.e. how long a saved clip is.
    /// Applied to OBS via ApplyRecordingOutputSettings - OBS reads it when the buffer output starts, so a
    /// change only takes hold on the next buffer restart (reconnect), not instantly.</summary>
    [ObservableProperty]
    private int _replayBufferLengthSeconds = 60;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isRecording;

    /// <summary>Mirrors ObsController.GetReplayBufferActive() - the split "action" indicator (M10) needs
    /// this as a bound property since the underlying call is a one-shot query, not push-notified elsewhere
    /// in the app already. Set right after the existing StartReplayBuffer/StopReplayBuffer calls, and
    /// refreshed opportunistically wherever those already run.</summary>
    [ObservableProperty]
    private bool _isReplayBufferActive;

    [ObservableProperty]
    private string _statusMessage = "Starting up...";

    [ObservableProperty]
    private string _detectedGameName = "Unknown";

    [ObservableProperty]
    private int _replayBufferSaveHotkeyVk;

    [ObservableProperty]
    private int _replayBufferSaveHotkeyModifiers;

    [ObservableProperty]
    private int _startStopRecordingHotkeyVk;

    [ObservableProperty]
    private int _startStopRecordingHotkeyModifiers;

    /// <summary>Sound cue toggles/volume (M13) - see SoundCuePlayer. Applied live (no "Save Settings"
    /// click needed to hear the effect), and also persisted through the normal SaveSettings flow so they
    /// stick across restarts.</summary>
    [ObservableProperty]
    private bool _playClipSavedSound = true;

    [ObservableProperty]
    private bool _playRecordingStartedSound = true;

    [ObservableProperty]
    private int _soundCueVolumePercent = 70;

    /// <summary>4-color theme customization - raw text the Settings-tab hex fields bind to. Not applied
    /// until ApplyColorsCommand runs (unlike the sound-cue properties above, hex needs validation before
    /// it's safe to hand to ThemeManager, so this isn't a live-as-you-type binding).</summary>
    [ObservableProperty]
    private string _primaryColorHex = ThemeManager.DefaultPrimaryColorHex;

    [ObservableProperty]
    private string _secondaryColorHex = ThemeManager.DefaultSecondaryColorHex;

    [ObservableProperty]
    private string _tertiaryColorHex = ThemeManager.DefaultTertiaryColorHex;

    [ObservableProperty]
    private string _quaternaryColorHex = ThemeManager.DefaultQuaternaryColorHex;

    /// <summary>False = Display Capture, true = Window Capture (M7). Changing this flips which of the two
    /// scene sources is live immediately - direct action, same pattern as Connect/Start Recording.</summary>
    [ObservableProperty]
    private bool _isWindowCaptureMode;

    [ObservableProperty]
    private CaptureOptionItem? _selectedMonitorOption;

    [ObservableProperty]
    private CaptureOptionItem? _selectedWindowOption;

    /// <summary>Separate selection from SelectedWindowOption - same underlying WindowOptions list, but this
    /// one drives AddAppAudioSourceCommand (M8) rather than the video capture target.</summary>
    [ObservableProperty]
    private CaptureOptionItem? _selectedAppAudioOption;

    /// <summary>
    /// Raised at the end of SaveSettings() when the persisted hotkey fields change. MainWindow owns
    /// the actual GlobalHotkeyService/ReplayBufferHotkeyHandler instances (they require the real HWND
    /// from the window's SourceInitialized event, so MainViewModel can't hold them - see
    /// GlobalHotkeyService's doc comment) and subscribes to this to unregister + re-register hotkey
    /// ids 1 and 2 against the freshly-saved values.
    /// </summary>
    public event Action? HotkeySettingsChanged;

    public MainViewModel()
    {
        _settingsRepository = new SettingsRepository(_database);
        // Loaded early (the rest of the constructor's settings-mirroring block further down re-reads
        // Settings, not the repository, so this isn't a duplicate load) - SequenceEditorViewModel needs
        // ExportStorageFolder at construction time, before the point this used to be loaded at.
        Settings = _settingsRepository.Load();
        _clipRepository = new ClipRepository(_database);
        _tagRepository = new TagRepository(_database);
        _gameProfileRepository = new GameProfileRepository(_database);
        _audioSourceRepository = new AudioSourceRepository(_database);
        _audioSourceManager = new AudioSourceManager(_obsController, _audioSourceRepository);
        _draftRepository = new ClipEditDraftRepository(_database);
        _sequenceRepository = new SequenceRepository(_database);

        _ingestCoordinator = new RecordingIngestCoordinator(_obsController, _clipRepository, _audioSourceManager, _tagRepository);
        var sequenceEditor = new SequenceEditorViewModel(_sequenceRepository, _clipRepository, Settings.ExportStorageFolder, _ingestCoordinator);
        ClipBrowser = new ClipBrowserViewModel(_clipRepository, _tagRepository, sequenceEditor, _ingestCoordinator);
        GameProfiles = new GameProfilesViewModel(_gameProfileRepository, _obsController);
        ClipBrowser.TrimEditorRequested += OnTrimEditorRequested;

        _audioSourceManager.SourcesChanged += () => App.Current.Dispatcher.Invoke(RefreshAudioSources);
        _audioSourceRefreshTimer = new Timer(_ => AudioSourceRefreshTick(), null, Timeout.Infinite, Timeout.Infinite);

        _gameDetectionService = new GameDetectionService(_gameDatabase, _gameProfileRepository);
        _profileApplier = new ProfileApplier(_obsController, _gameDetectionService);
        _gameDetectionService.DetectedGameChanged += (profile, displayName, processName) => App.Current.Dispatcher.Invoke(() =>
        {
            DetectedGameName = displayName ?? profile.DisplayName;

            // Only a *recognized* game gets linked - displayName/processName are both null for the
            // Default/unmatched case (e.g. the user alt-tabbed to a browser), and there's nothing
            // meaningful to point a game-audio capture at then. See AudioSourceManager's doc comment.
            if (displayName is not null && processName is not null)
            {
                _audioSourceManager.SetLinkedGameTargetForProcess(processName, displayName);
            }
        });

        _obsController.Connected += () => App.Current.Dispatcher.Invoke(() =>
        {
            IsConnected = true;
            StatusMessage = "Connected to OBS.";
            _obsController.EnsureDisplayCaptureSource(ObsController.DefaultSceneName, ObsController.DisplayCaptureSourceName);
            _obsController.EnsureWindowCaptureSource(ObsController.DefaultSceneName, ObsController.WindowCaptureSourceName);
            GameProfiles.RefreshObsProfilesCommand.Execute(null);
            GameProfiles.RefreshCaptureOptionsCommand.Execute(null);
            RefreshCaptureOptionsCommand.Execute(null);

            try
            {
                _audioSourceManager.RestoreProvisionedSources();
                _audioSourceManager.EnsurePresetSources();

                // On a reconnect (not the app's first-ever connect), GameDetectionService may already have
                // been running and have a game in hand - re-announce it so the Game source picks up where
                // it left off instead of waiting for the next foreground change.
                if (_gameDetectionService.LastDetectedGameDisplayName is { } lastDisplayName
                    && _gameDetectionService.LastDetectedProcessName is { } lastProcessName)
                {
                    _audioSourceManager.SetLinkedGameTargetForProcess(lastProcessName, lastDisplayName);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to restore audio sources: {ex.Message}";
            }

            _audioSourceRefreshTimer.Change(AudioSourceRefreshInterval, AudioSourceRefreshInterval);

            // Both of these have to land before StartReplayBuffer: OBS reads the record directory and the
            // buffer duration when the output starts, so applying them afterwards would take effect only
            // on the next connect. Each is separately best-effort - an older OBS without SetRecordDirectory
            // shouldn't cost us the buffer-length setting, or the replay buffer itself.
            ApplyRecordingOutputSettings();

            try
            {
                _obsController.StartReplayBuffer();
                IsReplayBufferActive = true;
            }
            catch
            {
                // Already active, or OBS-side prerequisite not met yet - non-fatal, "Save Replay Buffer"
                // just no-ops until the buffer is actually running.
            }

            _gameDetectionService.Start();
        });
        _obsController.Disconnected += () => App.Current.Dispatcher.Invoke(() =>
        {
            IsConnected = false;
            IsRecording = false;
            IsReplayBufferActive = false;
            StatusMessage = "Disconnected from OBS.";
            _audioSourceRefreshTimer.Change(Timeout.Infinite, Timeout.Infinite);
        });
        _obsController.RecordingStopped += path => App.Current.Dispatcher.Invoke(() =>
        {
            IsRecording = false;
            StatusMessage = $"Recording saved: {path}";
        });
        // Sound cues (M13). RecordingStarted only fires once OBS itself confirms the output actually
        // started (not just that the button was clicked), and ReplayBufferSaved is the exact moment a
        // clip gets captured - the same "did the thing you asked for actually happen" signal
        // RecordingIngestCoordinator's own subscriptions key off, just routed to audio instead of a
        // status message.
        _obsController.RecordingStarted += () => App.Current.Dispatcher.Invoke(() =>
        {
            if (PlayRecordingStartedSound)
            {
                _soundCuePlayer.PlayRecordingStarted();
            }
        });
        _obsController.ReplayBufferSaved += _ => App.Current.Dispatcher.Invoke(() =>
        {
            if (PlayClipSavedSound)
            {
                _soundCuePlayer.PlayClipSaved();
            }
        });
        _ingestCoordinator.ClipIngested += clip => App.Current.Dispatcher.Invoke(() =>
        {
            StatusMessage = $"Clip ready: {System.IO.Path.GetFileName(clip.FilePath)} ({clip.Duration:mm\\:ss})";
        });
        _ingestCoordinator.IngestFailed += (path, ex) => App.Current.Dispatcher.Invoke(() =>
        {
            StatusMessage = $"Failed to process clip {System.IO.Path.GetFileName(path)}: {ex.Message}";
        });

        ObsHost = Settings.ObsWebsocketHost;
        ObsPort = Settings.ObsWebsocketPort;
        ObsPassword = Settings.ObsWebsocketPassword;
        ClipStorageFolder = Settings.ClipStorageFolder;
        ReplayBufferLengthSeconds = Settings.ReplayBufferLengthSeconds;
        ReplayBufferSaveHotkeyVk = Settings.ReplayBufferSaveHotkeyVk;
        ReplayBufferSaveHotkeyModifiers = Settings.ReplayBufferSaveHotkeyModifiers;
        StartStopRecordingHotkeyVk = Settings.StartStopRecordingHotkeyVk;
        StartStopRecordingHotkeyModifiers = Settings.StartStopRecordingHotkeyModifiers;
        PlayClipSavedSound = Settings.PlayClipSavedSound;
        PlayRecordingStartedSound = Settings.PlayRecordingStartedSound;
        SoundCueVolumePercent = Settings.SoundCueVolumePercent;

        PrimaryColorHex = Settings.PrimaryColorHex ?? ThemeManager.DefaultPrimaryColorHex;
        SecondaryColorHex = Settings.SecondaryColorHex ?? ThemeManager.DefaultSecondaryColorHex;
        TertiaryColorHex = Settings.TertiaryColorHex ?? ThemeManager.DefaultTertiaryColorHex;
        QuaternaryColorHex = Settings.QuaternaryColorHex ?? ThemeManager.DefaultQuaternaryColorHex;
        // Runs before MainWindow.Show() (this constructor completes before InitializeComponent, since
        // MainWindow's _viewModel field initializer runs first) - the window never renders the built-in
        // colors before this applies, so there's no flash of the default palette on startup.
        ThemeManager.ApplyColors(PrimaryColorHex, SecondaryColorHex, TertiaryColorHex, QuaternaryColorHex);
    }

    partial void OnSoundCueVolumePercentChanged(int value) => _soundCuePlayer.SetVolume(value / 100.0);

    public async Task InitializeAsync()
    {
        _ = ClipBrowser.LoadAsync();
        _ = GameProfiles.LoadAsync();

        try
        {
            var settings = _settingsRepository.Load();
            var obsWasAlreadyRunning = _processManager.IsObsRunning();

            if (settings.AutoLaunchObs && !obsWasAlreadyRunning)
            {
                StatusMessage = "Launching OBS...";
                _processManager.EnsureRunning(settings.ObsExecutablePath);
            }

            // Give a freshly-launched OBS time to open its websocket port before the first attempt.
            await Task.Delay(obsWasAlreadyRunning ? 500 : 5000);

            _obsController.Connect(ObsHost, ObsPort, ObsPassword);

            for (var attempt = 0; attempt < 20 && !_obsController.IsConnected; attempt++)
            {
                await Task.Delay(500);
            }

            if (!_obsController.IsConnected)
            {
                StatusMessage = "Could not connect to OBS. Check Settings and try Connect.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Startup error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Connect()
    {
        StatusMessage = "Connecting...";

        // EnsureRunning throws FileNotFoundException when the configured OBS path is wrong, which is the
        // single most likely thing to be wrong on a fresh install. Unhandled, that took down the whole app
        // from a button click; surfacing it as a status message keeps the user in the Settings tab where
        // the path they need to correct actually is.
        try
        {
            var settings = _settingsRepository.Load();
            if (settings.AutoLaunchObs)
            {
                _processManager.EnsureRunning(settings.ObsExecutablePath);
            }

            _obsController.Connect(ObsHost, ObsPort, ObsPassword);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not connect: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartRecord))]
    private void StartRecord()
    {
        _obsController.StartRecord();
        IsRecording = true;
        StatusMessage = "Recording...";
    }

    private bool CanStartRecord() => IsConnected && !IsRecording;

    [RelayCommand(CanExecute = nameof(CanStopRecord))]
    private void StopRecord()
    {
        _obsController.StopRecord();
        StatusMessage = "Stopping recording...";
    }

    private bool CanStopRecord() => IsConnected && IsRecording;

    /// <summary>Used by the global start/stop-recording hotkey (not bound from XAML directly).</summary>
    public void ToggleRecording()
    {
        if (IsRecording)
        {
            if (StopRecordCommand.CanExecute(null))
            {
                StopRecordCommand.Execute(null);
            }
        }
        else if (StartRecordCommand.CanExecute(null))
        {
            StartRecordCommand.Execute(null);
        }
    }

    /// <summary>Used by the global save-replay-buffer hotkey and the tray icon; safely no-ops if not ready.</summary>
    public void SaveReplayBufferSafe()
    {
        if (_obsController.IsConnected && _obsController.GetReplayBufferActive())
        {
            _obsController.SaveReplayBuffer();
        }
    }

    /// <summary>Reloads MonitorOptions/WindowOptions from OBS (M7) - open windows change over time, so this
    /// is re-run on connect and via a manual "REFRESH" button rather than polled continuously.</summary>
    [RelayCommand]
    private void RefreshCaptureOptions()
    {
        if (!_obsController.IsConnected)
        {
            return;
        }

        try
        {
            MonitorOptions.Clear();
            foreach (var (label, value) in _obsController.GetMonitorOptions(ObsController.DisplayCaptureSourceName))
            {
                MonitorOptions.Add(new CaptureOptionItem(label, value));
            }

            WindowOptions.Clear();
            foreach (var (label, value) in _obsController.GetWindowOptions(ObsController.WindowCaptureSourceName))
            {
                WindowOptions.Add(new CaptureOptionItem(label, value));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to refresh capture sources: {ex.Message}";
        }
    }

    partial void OnIsWindowCaptureModeChanged(bool value)
    {
        if (!_obsController.IsConnected)
        {
            return;
        }

        try
        {
            _obsController.SetCaptureMode(
                ObsController.DefaultSceneName, ObsController.DisplayCaptureSourceName, ObsController.WindowCaptureSourceName, value);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to switch capture mode: {ex.Message}";
        }
    }

    partial void OnSelectedMonitorOptionChanged(CaptureOptionItem? value)
    {
        if (value is null || !_obsController.IsConnected)
        {
            return;
        }

        try
        {
            _obsController.SetMonitorCaptureTarget(ObsController.DisplayCaptureSourceName, value.Value);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to set monitor: {ex.Message}";
        }
    }

    partial void OnSelectedWindowOptionChanged(CaptureOptionItem? value)
    {
        if (value is null || !_obsController.IsConnected)
        {
            return;
        }

        try
        {
            _obsController.SetWindowCaptureTarget(ObsController.WindowCaptureSourceName, value.Value);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to set capture window: {ex.Message}";
        }
    }

    private bool CanAddAppAudioSource() =>
        _obsController.IsConnected && _audioSourceManager.CanAddMore && SelectedAppAudioOption is not null;

    [RelayCommand(CanExecute = nameof(CanAddAppAudioSource))]
    private void AddAppAudioSource()
    {
        if (SelectedAppAudioOption is not { } option)
        {
            return;
        }

        try
        {
            _audioSourceManager.AddAppAudioSource(option.Value, option.Label);
            StatusMessage = $"Added audio source: {option.Label}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to add audio source: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RemoveAudioSource(AudioSourceRowViewModel? row)
    {
        if (row is null || row.IsBuiltIn)
        {
            return;
        }

        try
        {
            _audioSourceManager.RemoveAppAudioSource(row.InputName);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to remove audio source: {ex.Message}";
        }
    }

    /// <summary>Quick action (M12): mutes every audio source except the linked Game one, e.g. for a clip
    /// where Discord/Spotify chatter would be unwanted. Just flips each row's IsMuted - the existing
    /// AudioSourceRowViewModel.OnIsMutedChanged handler does the actual OBS calls.</summary>
    [RelayCommand]
    private void SetGameOnlyAudio()
    {
        foreach (var row in AudioSources)
        {
            row.IsMuted = row.InputName != AudioSourceManager.GameInputName;
        }
    }

    /// <summary>Quick action (M12): the inverse of SetGameOnlyAudio - mutes only the linked Game source,
    /// unmuting everything else (Desktop Audio, Mic, and every app source).</summary>
    [RelayCommand]
    private void SetAllExceptGameAudio()
    {
        foreach (var row in AudioSources)
        {
            row.IsMuted = row.InputName == AudioSourceManager.GameInputName;
        }
    }

    /// <summary>
    /// Pushes the two recording-output settings that live in this app's database but are enforced by OBS:
    /// where recordings are written, and how many seconds of history the replay buffer keeps. Called on
    /// every connect (before the buffer starts) and again after a settings save, so a change takes effect
    /// on the next buffer restart rather than only after an app restart.
    ///
    /// Failures are reported but never thrown: SetRecordDirectory needs OBS 30+, and neither setting is
    /// worth losing the connection over - recording still works using OBS's own configuration.
    /// </summary>
    private void ApplyRecordingOutputSettings()
    {
        if (!_obsController.IsConnected)
        {
            return;
        }

        try
        {
            _obsController.SetReplayBufferSeconds(ReplayBufferLengthSeconds);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not set replay buffer length: {ex.Message}";
        }

        if (string.IsNullOrWhiteSpace(ClipStorageFolder))
        {
            return;
        }

        try
        {
            _obsController.SetRecordDirectory(ClipStorageFolder);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not set the recording folder in OBS (needs OBS 30+): {ex.Message}";
        }
    }

    private void RefreshAudioSources()
    {
        AudioSources.Clear();
        foreach (var source in _audioSourceManager.Sources)
        {
            var isMuted = false;
            var volumePercent = 100;
            try
            {
                isMuted = _audioSourceManager.GetMuted(source.InputName);
                volumePercent = (int)Math.Round(_audioSourceManager.GetVolume(source.InputName) * 100);
            }
            catch
            {
                // Best-effort initial state - default to unmuted/full volume rather than block the row from showing.
            }

            AudioSources.Add(new AudioSourceRowViewModel(source, isMuted, volumePercent, _audioSourceManager));
        }

        AddAppAudioSourceCommand.NotifyCanExecuteChanged();
    }

    /// <summary>_audioSourceRefreshTimer's callback - runs on a thread-pool thread, not the UI thread (see
    /// the timer's own doc comment). Fetches the running-window list once and hands it to both methods
    /// below, which would otherwise each independently round-trip to OBS for the same list.</summary>
    private void AudioSourceRefreshTick()
    {
        List<(string Label, string Value)>? runningWindows = null;
        try
        {
            runningWindows = _obsController.GetWindowOptions(ObsController.WindowCaptureSourceName);
        }
        catch
        {
            // Best-effort - each method below falls back to fetching (and failing/no-op-ing) on its own
            // rather than skipping outright.
        }

        _audioSourceManager.EnsurePresetSources(runningWindows);
        _audioSourceManager.RefreshAppSourceTargets(runningWindows);
    }

    [RelayCommand]
    private void SaveSettings()
    {
        var settings = _settingsRepository.Load();
        settings.ObsWebsocketHost = ObsHost;
        settings.ObsWebsocketPort = ObsPort;
        settings.ObsWebsocketPassword = ObsPassword;
        settings.ClipStorageFolder = ClipStorageFolder;
        settings.ReplayBufferLengthSeconds = ReplayBufferLengthSeconds;
        settings.ReplayBufferSaveHotkeyVk = ReplayBufferSaveHotkeyVk;
        settings.ReplayBufferSaveHotkeyModifiers = ReplayBufferSaveHotkeyModifiers;
        settings.StartStopRecordingHotkeyVk = StartStopRecordingHotkeyVk;
        settings.StartStopRecordingHotkeyModifiers = StartStopRecordingHotkeyModifiers;
        settings.PlayClipSavedSound = PlayClipSavedSound;
        settings.PlayRecordingStartedSound = PlayRecordingStartedSound;
        settings.SoundCueVolumePercent = SoundCueVolumePercent;
        settings.PrimaryColorHex = PrimaryColorHex;
        settings.SecondaryColorHex = SecondaryColorHex;
        settings.TertiaryColorHex = TertiaryColorHex;
        settings.QuaternaryColorHex = QuaternaryColorHex;
        _settingsRepository.Save(settings);
        Settings = settings;
        // Saving applies the colors too, in case the user typed a hex and hit Save without clicking Apply
        // first - Save shouldn't silently leave the preview one step behind the DB.
        ThemeManager.ApplyColors(PrimaryColorHex, SecondaryColorHex, TertiaryColorHex, QuaternaryColorHex);
        StatusMessage = "Settings saved.";

        // Re-push the OBS-enforced settings so a changed clip folder / buffer length doesn't sit in the
        // database until the next app restart.
        ApplyRecordingOutputSettings();

        // May overwrite the "Settings saved." message above with a registration failure - that's
        // intended, a failed re-registration is more important for the user to see than the generic
        // save confirmation.
        HotkeySettingsChanged?.Invoke();
    }

    /// <summary>Live preview: applies whatever's currently typed in the 4 hex fields without requiring
    /// Save Settings first, so picking a color is a fast type-and-look loop. Does not persist - SaveSettings
    /// is still what makes it stick across restarts.</summary>
    [RelayCommand]
    private void ApplyColors()
    {
        var ok = ThemeManager.ApplyColors(PrimaryColorHex, SecondaryColorHex, TertiaryColorHex, QuaternaryColorHex);
        StatusMessage = ok
            ? "Colors applied."
            : "One or more hex codes are invalid (expected #RRGGBB) - the invalid ones were left unchanged.";
    }

    [RelayCommand]
    private void ResetColors()
    {
        PrimaryColorHex = ThemeManager.DefaultPrimaryColorHex;
        SecondaryColorHex = ThemeManager.DefaultSecondaryColorHex;
        TertiaryColorHex = ThemeManager.DefaultTertiaryColorHex;
        QuaternaryColorHex = ThemeManager.DefaultQuaternaryColorHex;
        ThemeManager.ResetToDefaults();
        StatusMessage = "Colors reset to default.";
    }

    /// <summary>Lets the Settings tab's sound-cue checkboxes preview what they sound like at the current
    /// volume without waiting for a real recording-start/clip-save event.</summary>
    [RelayCommand]
    private void PreviewClipSavedSound() => _soundCuePlayer.PlayClipSaved();

    [RelayCommand]
    private void PreviewRecordingStartedSound() => _soundCuePlayer.PlayRecordingStarted();

    /// <summary>
    /// Opens the Trim Editor for a clip picked from the browser. This handler runs on the UI thread
    /// already - TrimEditorRequested is raised synchronously from ClipBrowserViewModel's
    /// OpenInTrimEditorCommand, a [RelayCommand] bound to a XAML button click - so no dispatcher
    /// hop is needed here, unlike the ObsController/RecordingIngestCoordinator event handlers above
    /// which fire from background threads. Builds the embedded editor's DataContext; ClipBrowserViewModel's
    /// "Edit" sub-view has already switched to it before this fires (see
    /// ClipBrowserViewModel.OpenInTrimEditor/OnEditingClipChanged).
    /// </summary>
    private void OnTrimEditorRequested(ClipItemViewModel clip) =>
        ClipBrowser.ActiveEditor = new TrimEditorViewModel(clip, Settings.ExportStorageFolder, _ingestCoordinator, _draftRepository);

    partial void OnIsConnectedChanged(bool value)
    {
        StartRecordCommand.NotifyCanExecuteChanged();
        StopRecordCommand.NotifyCanExecuteChanged();
        AddAppAudioSourceCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedAppAudioOptionChanged(CaptureOptionItem? value) => AddAppAudioSourceCommand.NotifyCanExecuteChanged();

    partial void OnIsRecordingChanged(bool value)
    {
        StartRecordCommand.NotifyCanExecuteChanged();
        StopRecordCommand.NotifyCanExecuteChanged();
    }

    public void Shutdown()
    {
        _audioSourceRefreshTimer.Dispose();
        _profileApplier.Dispose();
        _gameDetectionService.Dispose();
        ClipBrowser.Dispose();
        _ingestCoordinator.Dispose();
        _soundCuePlayer.Dispose();
        _obsController.Dispose();
    }
}
