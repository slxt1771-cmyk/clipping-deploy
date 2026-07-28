using ClippingSoftware.Data.ClipLibrary;
using ClippingSoftware.Data.Models;

namespace ClippingSoftware.Core.GameDetection;

/// <summary>
/// Orchestrates ForegroundWatcher + FullscreenHeuristic + GameDatabase + GameProfileRepository: on
/// foreground-window change, looks up the detected exe in the curated game DB, finds a matching
/// GameProfile (falling back to the Default profile when unmatched, or when a matched profile requires
/// fullscreen/borderless and the window isn't), and raises DetectedGameChanged.
/// </summary>
public sealed class GameDetectionService : IDisposable
{
    private readonly ForegroundWatcher _watcher;
    private readonly GameDatabase _gameDatabase;
    private readonly GameProfileRepository _profileRepository;
    private readonly bool _ownsWatcher;

    /// <summary>Raised whenever the resolved profile for the current foreground window changes. The third
    /// argument is the raw foreground process's executable name (e.g. "eldenring.exe"), passed through so
    /// subscribers that need to correlate against OBS's own window enumeration (AudioSourceManager's
    /// linked "Game" audio source, M12) don't have to re-derive it.</summary>
    public event Action<GameProfile, string?, string?>? DetectedGameChanged;

    public GameProfile? LastResolvedProfile { get; private set; }

    /// <summary>The curated game database's display name for the last foreground process, if it was
    /// recognized - null means the foreground window wasn't a known game (so LastResolvedProfile is
    /// whatever GetDefault() returned). Kept alongside LastResolvedProfile so a late subscriber (e.g.
    /// AudioSourceManager re-linking its Game source right after OBS reconnects) can re-announce the
    /// current state without waiting for the next actual foreground change.</summary>
    public string? LastDetectedGameDisplayName { get; private set; }

    /// <summary>The raw foreground process name behind LastDetectedGameDisplayName - see DetectedGameChanged's
    /// third argument. Null whenever LastDetectedGameDisplayName is null.</summary>
    public string? LastDetectedProcessName { get; private set; }

    public GameDetectionService(GameDatabase gameDatabase, GameProfileRepository profileRepository, ForegroundWatcher? watcher = null)
    {
        _gameDatabase = gameDatabase;
        _profileRepository = profileRepository;
        _ownsWatcher = watcher is null;
        _watcher = watcher ?? new ForegroundWatcher();
        _watcher.ForegroundChanged += OnForegroundChanged;
    }

    public void Start() => _watcher.Start();

    private void OnForegroundChanged(IntPtr hwnd, string processName)
    {
        try
        {
            var knownGame = _gameDatabase.FindByExecutable(processName);
            var isFullscreenOrBorderless = FullscreenHeuristic.IsFullscreenOrBorderless(hwnd);

            var profiles = _profileRepository.GetAll();

            GameProfile? matched = null;
            if (knownGame is not null)
            {
                matched = profiles.FirstOrDefault(p =>
                    !p.IsBuiltInDefault &&
                    p.ExecutableMatches.Any(exe => string.Equals(exe, processName, StringComparison.OrdinalIgnoreCase)) &&
                    (!p.RequireFullscreenOrBorderless || isFullscreenOrBorderless));
            }

            var profileToApply = matched ?? _profileRepository.GetDefault();
            LastResolvedProfile = profileToApply;
            LastDetectedGameDisplayName = knownGame?.DisplayName;
            LastDetectedProcessName = knownGame is null ? null : processName;

            DetectedGameChanged?.Invoke(profileToApply, knownGame?.DisplayName, LastDetectedProcessName);
        }
        catch
        {
            // Detection-path failures (bad JSON row, transient DB issue, etc.) must never take down the
            // watcher thread - the app should keep running with whatever profile was last applied.
        }
    }

    public void Dispose()
    {
        _watcher.ForegroundChanged -= OnForegroundChanged;
        if (_ownsWatcher)
        {
            _watcher.Dispose();
        }
    }
}
