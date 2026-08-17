using ClippingSoftware.Core.Obs;
using ClippingSoftware.Data.ClipLibrary;

namespace ClippingSoftware.Core.Recording;

/// <summary>One audio source currently routed into the recording, in track order (1-6).</summary>
public record AudioSourceInfo(string InputName, string DisplayLabel, int TrackNumber, bool IsBuiltIn);

/// <summary>
/// Owns the full set of audio sources feeding OBS's recording and their track assignments: two permanent
/// built-ins (Desktop Audio -> track 1, Mic/Aux -> track 2), one auto-managed "Game" source (track 3,
/// M12 - re-targeted to whatever GameDetectionService currently resolves as the foreground game, rather
/// than picked by hand like the others), and up to 3 user/preset app sources (tracks 4-6) - OBS supports
/// 6 output tracks total, and reserving track 3 for Game unconditionally (even before its OBS input
/// exists) keeps app-source numbering stable regardless of whether a game is currently detected.
///
/// Adding a source creates a wasapi_process_output_capture input targeting one running app, routes it to
/// a free track via SetInputAudioTracks, and updates the AdvOut/RecTracks profile parameter bitmask so
/// OBS actually bakes that track into the recorded file - all three steps matter; skipping the bitmask
/// update would silently drop the new track's audio from the output container even though the input and
/// routing look correct.
/// </summary>
public class AudioSourceManager(ObsController obsController, AudioSourceRepository repository, string sceneName = ObsController.DefaultSceneName)
{
    public const int MaxTracks = 6;
    private const int BuiltInTrackCount = 3; // Desktop Audio (1), Mic/Aux (2), Game (3 - reserved even when unlinked).
    private const int GameTrackNumber = 3;

    private const string DesktopAudioInputName = "Desktop Audio";
    private const string MicInputName = "Mic/Aux";

    /// <summary>Fixed input name for the auto-managed Game source - public so the UI (quick "GAME ONLY" /
    /// "ALL EXCEPT GAME" actions) can pick it out of <see cref="Sources"/> without a separate flag.</summary>
    public const string GameInputName = "Game Audio";

    /// <summary>The three apps M12 auto-provisions into free slots on connect when they're running and not
    /// already added - chosen as the common "always have something to say" background apps for a
    /// gaming/clipping setup. Executable names are the Windows process names, matched case-insensitively
    /// against OBS's window list.</summary>
    private static readonly (string ExeName, string DisplayLabel)[] DefaultPresetApps =
    [
        ("Discord.exe", "Discord"),
        ("Spotify.exe", "Spotify"),
        ("brave.exe", "Brave"),
    ];

    private readonly List<AudioSourceInfo> _appSources = [];
    private bool _gameSourceExists;
    private string _gameDisplayLabel = "Game";

    /// <summary>Raised after any add/remove/restore/re-link, so the UI can refresh its list.</summary>
    public event Action? SourcesChanged;

    /// <summary>All sources in track order: the two built-ins, then Game (once linked at least once), then
    /// app sources by track number.</summary>
    public IReadOnlyList<AudioSourceInfo> Sources
    {
        get
        {
            List<AudioSourceInfo> sources =
            [
                new(DesktopAudioInputName, "Desktop Audio", 1, true),
                new(MicInputName, "Mic/Aux", 2, true),
            ];

            if (_gameSourceExists)
            {
                sources.Add(new AudioSourceInfo(GameInputName, _gameDisplayLabel, GameTrackNumber, true));
            }

            sources.AddRange(_appSources);
            return sources;
        }
    }

    public bool CanAddMore => _appSources.Count < MaxTracks - BuiltInTrackCount;

    public void SetMuted(string inputName, bool muted) => obsController.SetInputMute(inputName, muted);

    public bool GetMuted(string inputName) => obsController.GetInputMute(inputName);

    public void SetVolume(string inputName, double volumeMul) => obsController.SetInputVolume(inputName, volumeMul);

    public double GetVolume(string inputName) => obsController.GetInputVolume(inputName);

    /// <summary>
    /// Adds a running app as a new tracked audio source. Throws <see cref="InvalidOperationException"/> if
    /// all 3 app-audio slots are already used - callers (the UI) should check <see cref="CanAddMore"/> first
    /// to disable the control instead of relying on this throw for the common case.
    /// </summary>
    public void AddAppAudioSource(string windowTargetValue, string displayLabel)
    {
        if (!CanAddMore)
        {
            throw new InvalidOperationException("All 6 OBS audio tracks are already in use (Desktop Audio + Mic + Game + 3 apps).");
        }

        var trackNumber = NextFreeTrack();
        var inputName = $"AppAudio_{Guid.NewGuid():N}";

        obsController.CreateAppAudioSource(sceneName, inputName, windowTargetValue);
        obsController.SetInputAudioTrack(inputName, trackNumber);

        repository.Insert(new AudioSourceRecord(Guid.NewGuid(), inputName, displayLabel, windowTargetValue, trackNumber));
        _appSources.Add(new AudioSourceInfo(inputName, displayLabel, trackNumber, false));

        UpdateRecTracksBitmask();
        SourcesChanged?.Invoke();
    }

    public void RemoveAppAudioSource(string inputName)
    {
        var existing = _appSources.FirstOrDefault(s => s.InputName == inputName);
        if (existing is null)
        {
            return;
        }

        obsController.RemoveInput(inputName);
        repository.Delete(inputName);
        _appSources.Remove(existing);

        UpdateRecTracksBitmask();
        SourcesChanged?.Invoke();
    }

    /// <summary>
    /// Re-provisions every persisted app-audio source on startup, after OBS connects. Idempotent (checks
    /// scene membership before creating, like ObsController's Ensure* methods) and best-effort per source -
    /// one source failing to restore (e.g. its target app was uninstalled) shouldn't block the others or
    /// startup itself. Also picks up a Game Audio input left over from a previous session (OBS's scene
    /// collection persists on disk independent of this app's own process lifetime), rather than assuming
    /// it always needs to be created fresh.
    /// </summary>
    public void RestoreProvisionedSources()
    {
        _appSources.Clear();

        var sceneItemNames = obsController.GetSceneItemSourceNames(sceneName);
        _gameSourceExists = sceneItemNames.Contains(GameInputName);
        if (_gameSourceExists)
        {
            obsController.SetInputAudioTrack(GameInputName, GameTrackNumber);
        }

        foreach (var record in repository.GetAll())
        {
            try
            {
                if (!sceneItemNames.Contains(record.InputName))
                {
                    obsController.CreateAppAudioSource(sceneName, record.InputName, record.WindowTarget);
                }

                obsController.SetInputAudioTrack(record.InputName, record.TrackNumber);
                _appSources.Add(new AudioSourceInfo(record.InputName, record.DisplayLabel, record.TrackNumber, false));
            }
            catch
            {
                // Best-effort: this is a live-only convenience list, and one bad row shouldn't stop the
                // app from starting or the other sources from restoring.
            }
        }

        UpdateRecTracksBitmask();
        SourcesChanged?.Invoke();
    }

    /// <summary>
    /// Re-targets the auto-managed "Game" audio source to match GameDetectionService's currently detected
    /// process (M12) - called from MainViewModel's DetectedGameChanged handler whenever a *recognized* game
    /// is in the foreground (callers should not invoke this for the Default/unknown-app case; there's
    /// nothing meaningful to point a game-audio capture at when the user just alt-tabbed to a browser).
    /// Creates the underlying wasapi_process_output_capture input lazily on first use (OBS must be
    /// connected and the target window must exist to create it), then just re-points it via
    /// SetWindowCaptureTarget on every later call - cheaper than recreating the input on every game switch,
    /// and keeps its mute/volume state stable across games.
    /// </summary>
    public void SetLinkedGameTargetForProcess(string processExeName, string displayName)
    {
        _gameDisplayLabel = $"Game ({displayName})";

        try
        {
            var windowValue = obsController.GetWindowOptions(ObsController.WindowCaptureSourceName)
                .FirstOrDefault(w => w.Value.EndsWith(":" + processExeName, StringComparison.OrdinalIgnoreCase))
                .Value;

            if (windowValue is not null)
            {
                if (!_gameSourceExists)
                {
                    obsController.CreateAppAudioSource(sceneName, GameInputName, windowValue);
                    obsController.SetInputAudioTrack(GameInputName, GameTrackNumber);
                    _gameSourceExists = true;
                    UpdateRecTracksBitmask();
                }
                else
                {
                    obsController.SetWindowCaptureTarget(GameInputName, windowValue);
                }
            }
            // windowValue null: the recognized game's process has no enumerable window yet (e.g. still
            // launching) - leave whatever the Game source last pointed at alone rather than clearing it.
        }
        catch
        {
            // Best-effort - OBS might not be connected yet, or window enumeration might briefly fail; the
            // Game track just keeps pointing at whatever it last had, and the next foreground change retries.
        }

        SourcesChanged?.Invoke();
    }

    /// <summary>
    /// Auto-provisions <see cref="DefaultPresetApps"/> into any still-free app-source slots, for whichever
    /// of them are currently running - called once after RestoreProvisionedSources on every connect, so a
    /// fresh install (or a slot freed up by removing something) gets sensible defaults without the user
    /// manually re-adding these particular apps every session. Best-effort per app: one missing/not-running
    /// app just leaves that slot free rather than blocking the others, and an app already present under the
    /// same display label (however it got there) is left alone rather than duplicated.
    /// </summary>
    public void EnsurePresetSources()
    {
        List<(string Label, string Value)> runningWindows;
        try
        {
            runningWindows = obsController.GetWindowOptions(ObsController.WindowCaptureSourceName);
        }
        catch
        {
            return;
        }

        foreach (var (exeName, displayLabel) in DefaultPresetApps)
        {
            if (!CanAddMore)
            {
                break;
            }

            if (_appSources.Any(s => s.DisplayLabel.Equals(displayLabel, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var match = runningWindows.FirstOrDefault(w => w.Value.EndsWith(":" + exeName, StringComparison.OrdinalIgnoreCase));
            if (match.Value is null)
            {
                continue;
            }

            try
            {
                AddAppAudioSource(match.Value, displayLabel);
            }
            catch
            {
                // Best-effort - one preset failing to add (e.g. a transient OBS call failure) shouldn't
                // block the others.
            }
        }
    }

    /// <summary>
    /// Re-resolves every existing app-audio source's OBS window target against whatever's currently
    /// running, keyed off the executable name embedded at the end of each source's stored WindowTarget
    /// ("title:class:exe" - see ObsController.GetWindowOptions' doc comment). Needed because nothing else
    /// in this class ever re-points an app source once it's added: RestoreProvisionedSources only
    /// (re)creates a scene item that's missing entirely, it doesn't refresh one that's still there but
    /// pointed at a window that's gone (the app was closed and relaunched as a new process/window - Spotify
    /// especially, since its window title also changes with every song, so its stored target goes stale far
    /// more often than just on restart). The Game source doesn't need this - it's already re-targeted on
    /// every foreground change via SetLinkedGameTargetForProcess. Call this periodically (see MainViewModel)
    /// rather than relying on a foreground-change event, since a backgrounded app's window can change
    /// without one ever firing.
    /// </summary>
    public void RefreshAppSourceTargets()
    {
        if (_appSources.Count == 0)
        {
            return;
        }

        List<(string Label, string Value)> runningWindows;
        try
        {
            runningWindows = obsController.GetWindowOptions(ObsController.WindowCaptureSourceName);
        }
        catch
        {
            return;
        }

        foreach (var record in repository.GetAll())
        {
            var exeName = record.WindowTarget.Split(':').LastOrDefault();
            if (string.IsNullOrEmpty(exeName))
            {
                continue;
            }

            var match = runningWindows.FirstOrDefault(w => w.Value.EndsWith(":" + exeName, StringComparison.OrdinalIgnoreCase));
            if (match.Value is null || match.Value == record.WindowTarget)
            {
                continue;
            }

            try
            {
                obsController.SetWindowCaptureTarget(record.InputName, match.Value);
                repository.UpdateWindowTarget(record.InputName, match.Value);
            }
            catch
            {
                // Best-effort - a transient OBS call failure here just means this source stays on its
                // previous target until the next refresh tick.
            }
        }
    }

    private int NextFreeTrack()
    {
        var used = new HashSet<int> { 1, 2, GameTrackNumber };
        foreach (var source in _appSources)
        {
            used.Add(source.TrackNumber);
        }

        for (var track = 4; track <= MaxTracks; track++)
        {
            if (!used.Contains(track))
            {
                return track;
            }
        }

        throw new InvalidOperationException("No free audio track (max 6 reached).");
    }

    private void UpdateRecTracksBitmask()
    {
        var mask = 0;
        foreach (var source in Sources)
        {
            mask |= 1 << (source.TrackNumber - 1);
        }

        obsController.SetProfileParameter("AdvOut", "RecTracks", mask.ToString());
    }
}
