using ClippingSoftware.Core.GameDetection;
using ClippingSoftware.Core.Obs;
using ClippingSoftware.Data.Models;
using ClippingSoftware.Shared.Interop;

namespace ClippingSoftware.Core.ProfileManager;

/// <summary>
/// Subscribes to GameDetectionService.DetectedGameChanged and diffs the incoming GameProfile against the
/// currently-applied one, implementing the plan's light-switch vs heavy-switch logic:
///
///   Light switch (capture mode / audio-enabled differ only): mute/unmute the relevant OBS audio inputs
///   directly - no output interruption.
///
///   Heavy switch (resolution / fps / OBS profile / encoder settings differ): obs-websocket rejects
///   SetVideoSettings/SetCurrentProfile while an output is running, so if the replay buffer is active this
///   flushes it (SaveReplayBuffer -> StopReplayBuffer), applies the video/profile change, then restarts the
///   buffer (StartReplayBuffer), accepting the resulting ~1-2s gap per the plan.
/// </summary>
public sealed class ProfileApplier : IDisposable
{
    private readonly ObsController _obs;
    private readonly GameDetectionService _detectionService;
    private readonly object _applyLock = new();

    private GameProfile? _currentProfile;

    /// <summary>Raised after a profile has been applied. The bool indicates whether it was a heavy switch.</summary>
    public event Action<GameProfile, bool>? ProfileApplied;

    public ProfileApplier(ObsController obs, GameDetectionService detectionService)
    {
        _obs = obs;
        _detectionService = detectionService;
        _detectionService.DetectedGameChanged += OnDetectedGameChanged;
    }

    private void OnDetectedGameChanged(GameProfile profile, string? displayName, string? processName)
    {
        lock (_applyLock)
        {
            Apply(profile);
        }
    }

    private void Apply(GameProfile profile)
    {
        if (!_obs.IsConnected)
        {
            return;
        }

        var previous = _currentProfile;
        if (previous is not null && previous.Id == profile.Id)
        {
            return;
        }

        var isHeavySwitch = previous is null || RequiresHeavySwitch(previous, profile);

        if (isHeavySwitch)
        {
            ApplyHeavySwitch(profile);
        }
        else
        {
            ApplyLightSwitch(profile);
        }

        _currentProfile = profile;
        ProfileApplied?.Invoke(profile, isHeavySwitch);
    }

    private static bool RequiresHeavySwitch(GameProfile previous, GameProfile next)
    {
        var (previousWidth, previousHeight) = ResolveResolution(previous);
        var (nextWidth, nextHeight) = ResolveResolution(next);

        return previousWidth != nextWidth ||
            previousHeight != nextHeight ||
            previous.Fps != next.Fps ||
            previous.ObsProfileName != next.ObsProfileName ||
            !EncoderMatches(previous.Encoder, next.Encoder);
    }

    /// <summary>
    /// The width/height actually applied for a profile: its stored OutputWidth/OutputHeight, or - if
    /// AutoDetectResolution is set - whatever the primary display currently reports (see
    /// Win32Window.GetSystemMetrics doc comment). Centralized here so RequiresHeavySwitch's diff and
    /// ApplyHeavySwitch's SetVideoSettings call always agree on what "this profile's resolution" means.
    /// </summary>
    private static (int Width, int Height) ResolveResolution(GameProfile profile) =>
        profile.AutoDetectResolution
            ? (Win32Window.GetSystemMetrics(Win32Window.SM_CXSCREEN), Win32Window.GetSystemMetrics(Win32Window.SM_CYSCREEN))
            : (profile.OutputWidth, profile.OutputHeight);

    private static bool EncoderMatches(NvencSettings a, NvencSettings b) =>
        a.Codec == b.Codec &&
        a.RateControl == b.RateControl &&
        a.CqLevel == b.CqLevel &&
        a.Preset == b.Preset &&
        a.Tuning == b.Tuning &&
        a.Multipass == b.Multipass &&
        a.KeyframeIntervalSec == b.KeyframeIntervalSec;

    private void ApplyLightSwitch(GameProfile profile)
    {
        ApplyAudioMuteState(profile);
        ApplyCaptureTarget(profile);
    }

    private void ApplyHeavySwitch(GameProfile profile)
    {
        // Per obs-websocket v5, SetCurrentProfile/SetVideoSettings are rejected while *any* output is
        // running - not just the replay buffer. Unlike the replay buffer (which this app owns and can
        // stop/restart transparently), a manual recording is a deliberate user action; auto-stopping it
        // just to apply a profile's resolution would silently split the user's recording into two files,
        // which is worse than just deferring the video-settings change. So: only touch video
        // settings/profile when nothing is actively recording. Audio mute state and capture target don't
        // need an output stopped, so those still apply either way - this used to be one unguarded sequence
        // where an exception here (previously only guarded for the replay buffer) aborted the whole method,
        // silently skipping even the parts that didn't need output stopped.
        if (!_obs.GetRecordingActive())
        {
            var replayBufferWasActive = _obs.GetReplayBufferActive();

            if (replayBufferWasActive)
            {
                _obs.SaveReplayBuffer();
                // Give OBS a moment to flush the in-flight save before pulling the buffer out from under it.
                Thread.Sleep(500);
                _obs.StopReplayBuffer();
            }

            if (!string.Equals(_obs.GetCurrentProfileName(), profile.ObsProfileName, StringComparison.Ordinal))
            {
                _obs.SetCurrentProfile(profile.ObsProfileName);
            }

            var (width, height) = ResolveResolution(profile);
            _obs.SetVideoSettings(
                baseWidth: width,
                baseHeight: height,
                outputWidth: width,
                outputHeight: height,
                fpsNumerator: profile.Fps,
                fpsDenominator: 1);

            if (replayBufferWasActive)
            {
                _obs.StartReplayBuffer();
            }
        }

        ApplyAudioMuteState(profile);
        ApplyCaptureTarget(profile);
    }

    private void ApplyAudioMuteState(GameProfile profile)
    {
        TrySetMute("Desktop Audio", !profile.AudioEnabled);
        TrySetMute("Mic/Aux", !profile.AudioEnabled);
    }

    /// <summary>
    /// Applies a profile's capture mode/target: flips which of Display Capture / Window Capture is the live
    /// scene item, then (if the profile specifies one) points that source at its saved monitor/window. Runs
    /// as part of both light and heavy switch - flipping which source is enabled doesn't need an output
    /// stopped, so there's no reason to gate it behind the heavy-switch path.
    /// </summary>
    private void ApplyCaptureTarget(GameProfile profile)
    {
        var useWindowCapture = profile.CaptureMode == "WindowCapture";

        try
        {
            _obs.SetCaptureMode(
                ObsController.DefaultSceneName,
                ObsController.DisplayCaptureSourceName,
                ObsController.WindowCaptureSourceName,
                useWindowCapture);

            if (useWindowCapture && !string.IsNullOrEmpty(profile.CaptureTargetWindow))
            {
                _obs.SetWindowCaptureTarget(ObsController.WindowCaptureSourceName, profile.CaptureTargetWindow);
            }
            else if (!useWindowCapture && !string.IsNullOrEmpty(profile.CaptureTargetMonitorId))
            {
                _obs.SetMonitorCaptureTarget(ObsController.DisplayCaptureSourceName, profile.CaptureTargetMonitorId);
            }
        }
        catch
        {
            // Capture sources may not exist yet in the active scene collection - non-fatal, matches
            // TrySetMute's tolerance below.
        }
    }

    private void TrySetMute(string inputName, bool muted)
    {
        try
        {
            _obs.SetInputMute(inputName, muted);
        }
        catch
        {
            // Input may not exist under this name in the active scene collection - non-fatal.
        }
    }

    public void Dispose()
    {
        _detectionService.DetectedGameChanged -= OnDetectedGameChanged;
    }
}
