namespace ClippingSoftware.Data.Models;

public class NvencSettings
{
    public string Codec { get; set; } = "hevc";
    public string RateControl { get; set; } = "CQP";
    public int CqLevel { get; set; } = 14;
    public string Preset { get; set; } = "p6";
    public string Tuning { get; set; } = "hq";
    public string Multipass { get; set; } = "qres";
    public int KeyframeIntervalSec { get; set; } = 2;
}

public class GameProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;
    public List<string> ExecutableMatches { get; set; } = [];
    public bool RequireFullscreenOrBorderless { get; set; }

    public string CaptureMode { get; set; } = "DisplayCapture";

    /// <summary>Monitor device-path (ObsController.GetMonitorOptions' Value) when CaptureMode is
    /// "DisplayCapture"; null means leave whatever's currently set on the Display Capture source alone.</summary>
    public string? CaptureTargetMonitorId { get; set; }

    /// <summary>Window target string (ObsController.GetWindowOptions' Value) when CaptureMode is
    /// "WindowCapture"; null means leave whatever's currently set on the Window Capture source alone.</summary>
    public string? CaptureTargetWindow { get; set; }

    /// <summary>Single switch for every audio input this profile controls (mic + desktop audio) - there's
    /// no per-input split; muting is all-or-nothing per profile.</summary>
    public bool AudioEnabled { get; set; } = true;

    public int OutputWidth { get; set; }
    public int OutputHeight { get; set; }

    /// <summary>When true, ProfileApplier ignores OutputWidth/OutputHeight and instead queries the current
    /// primary-display resolution at the moment this profile is applied, using that as the OBS canvas size.
    /// For a game run through a non-native "stretched" resolution (e.g. via Nvidia Control Panel's
    /// resolution override, which changes the OS-reported display mode), this keeps the OBS canvas matched
    /// to whatever the game actually renders at instead of a stale hardcoded value - avoiding the
    /// letterbox/pillarbox black bars that show up when the canvas is wider/taller than the captured
    /// desktop image. OutputWidth/OutputHeight are left in place as the fallback/manual values used when
    /// this is false.</summary>
    public bool AutoDetectResolution { get; set; }

    public int Fps { get; set; } = 60;
    public string ObsProfileName { get; set; } = "Untitled";
    public NvencSettings Encoder { get; set; } = new();

    public bool IsBuiltInDefault { get; set; }
}
