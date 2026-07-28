namespace ClippingSoftware.Data.Models;

public class AppSettings
{
    public string ObsExecutablePath { get; set; } = @"C:\Program Files\obs-studio\bin\64bit\obs64.exe";
    public string ObsWebsocketHost { get; set; } = "localhost";
    public int ObsWebsocketPort { get; set; } = 4455;
    // Empty by default, not a hardcoded real password - this repo is pushed to GitHub (see the
    // Squirrel.Windows packaging work), so no per-machine secret can live here. Each machine's actual
    // websocket password is set once through the Settings tab and persisted in the local (gitignored)
    // SQLite DB, never in source.
    public string ObsWebsocketPassword { get; set; } = string.Empty;
    public string ClipStorageFolder { get; set; } = @"D:\claude stuff\clipping software\Recordings";
    public string ExportStorageFolder { get; set; } = @"D:\claude stuff\clipping software\Exports";
    public string? DefaultGameProfileId { get; set; }

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
