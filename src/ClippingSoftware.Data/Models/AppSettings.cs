namespace ClippingSoftware.Data.Models;

public class AppSettings
{
    /// <summary>
    /// Where a default OBS install puts obs64.exe. Only a starting guess - the first-run setup wizard
    /// probes the real location (including a 32-bit-Program-Files / custom install) and overwrites this,
    /// and the Settings tab can correct it afterward.
    /// </summary>
    public string ObsExecutablePath { get; set; } = DefaultObsExecutablePath;

    public string ObsWebsocketHost { get; set; } = "localhost";
    public int ObsWebsocketPort { get; set; } = 4455;

    // Empty by default, never a real password: this repo is public, so no per-machine secret can live in
    // source. OBS 28+ generates its own websocket password and enables auth by default, so a fresh install
    // *will* need one - the first-run setup wizard walks the user through copying it out of OBS's
    // Tools > WebSocket Server Settings dialog. It's then persisted in the local (gitignored) SQLite DB.
    public string ObsWebsocketPassword { get; set; } = string.Empty;

    // Under the user's own Videos folder rather than a fixed drive/path: an installed copy runs on a
    // machine whose drive layout this app knows nothing about, and Videos is both guaranteed to exist and
    // where a recording tool's output is expected to land.
    public string ClipStorageFolder { get; set; } = DefaultClipStorageFolder;
    public string ExportStorageFolder { get; set; } = DefaultExportStorageFolder;

    public string? DefaultGameProfileId { get; set; }

    /// <summary>
    /// False until the first-run setup wizard has been completed (or explicitly skipped). Drives whether
    /// the wizard is shown at startup - a stored flag rather than "is the settings row missing", since the
    /// row is created with defaults on first load, well before the user has actually configured anything.
    /// </summary>
    public bool HasCompletedFirstRunSetup { get; set; }

    /// <summary>Launch OBS automatically at startup (the normal always-on-recorder behavior). The wizard
    /// exposes this so a user who prefers to start OBS themselves isn't fought with on every launch.</summary>
    public bool AutoLaunchObs { get; set; } = true;

    public static string DefaultObsExecutablePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "obs-studio", "bin", "64bit", "obs64.exe");

    public static string DefaultClipStorageFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ClippingSoftware", "Recordings");

    public static string DefaultExportStorageFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ClippingSoftware", "Exports");

    // Default: Ctrl+Alt+F10. VK_F10 = 0x79, MOD_CONTROL (0x0002) | MOD_ALT (0x0001) = 0x0003.
    // See ClippingSoftware.Shared.Interop.Win32Hotkeys for the named constants these mirror.
    public int ReplayBufferSaveHotkeyVk { get; set; } = 0x79;
    public int ReplayBufferSaveHotkeyModifiers { get; set; } = 0x0003;

    // Default: Ctrl+Alt+F11. VK_F11 = 0x7A, MOD_CONTROL (0x0002) | MOD_ALT (0x0001) = 0x0003.
    public int StartStopRecordingHotkeyVk { get; set; } = 0x7A;
    public int StartStopRecordingHotkeyModifiers { get; set; } = 0x0003;

    public int ReplayBufferLengthSeconds { get; set; } = 60;

    // Sound cues (M13) - see SoundCuePlayer. Volume is 0-100 (UI convention, same as
    // AudioSourceRowViewModel.VolumePercent), converted to MediaPlayer's 0.0-1.0 at the call site.
    public bool PlayClipSavedSound { get; set; } = true;
    public bool PlayRecordingStartedSound { get; set; } = true;
    public int SoundCueVolumePercent { get; set; } = 70;

    // Accent color customization (M15) - superseded by the 4-color scheme below, kept only so an existing
    // installs' old values aren't silently discarded from the row; nothing reads these anymore.
    public string? PrimaryAccentHex { get; set; }
    public string? SecondaryAccentHex { get; set; }

    // 4-color editable theme - see App.Theming.ThemeManager. Null means "use the built-in default"
    // (ThemeManager.DefaultPrimaryColorHex etc.) rather than duplicating those defaults here - Data has no
    // reason to know what the built-in colors actually are.
    public string? PrimaryColorHex { get; set; }
    public string? SecondaryColorHex { get; set; }
    public string? TertiaryColorHex { get; set; }
    public string? QuaternaryColorHex { get; set; }
}
