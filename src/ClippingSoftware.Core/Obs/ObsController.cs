using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;
using OBSWebsocketDotNet.Types.Events;

namespace ClippingSoftware.Core.Obs;

/// <summary>
/// Sole chokepoint for all obs-websocket calls. Nothing else in the app should talk to OBS directly.
/// </summary>
public class ObsController : IDisposable
{
    // Single-scene assumption throughout the app (see CLAUDE.md) - these are the fixed names
    // MainViewModel/ProfileApplier/AudioSourceManager all provision against.
    public const string DefaultSceneName = "Scene";
    public const string DisplayCaptureSourceName = "Display Capture";
    public const string WindowCaptureSourceName = "Window Capture";

    private readonly OBSWebsocket _obs = new();

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action? RecordingStarted;
    public event Action<string?>? RecordingStopped;
    public event Action<string>? ReplayBufferSaved;

    public bool IsConnected => _obs.IsConnected;

    public ObsController()
    {
        _obs.Connected += (_, _) => Connected?.Invoke();
        _obs.Disconnected += (_, _) => Disconnected?.Invoke();
        _obs.RecordStateChanged += OnRecordStateChanged;
        _obs.ReplayBufferSaved += OnReplayBufferSaved;
    }

    public void Connect(string host, int port, string password)
    {
        _obs.ConnectAsync($"ws://{host}:{port}", password);
    }

    public void Disconnect()
    {
        _obs.Disconnect();
    }

    public void StartRecord() => _obs.StartRecord();

    public void StopRecord() => _obs.StopRecord();

    public void StartReplayBuffer() => _obs.StartReplayBuffer();

    public void StopReplayBuffer() => _obs.StopReplayBuffer();

    public void SaveReplayBuffer() => _obs.SaveReplayBuffer();

    public bool GetReplayBufferActive() => _obs.GetReplayBufferStatus();

    /// <summary>Whether OBS is currently recording (independent of the replay buffer - see
    /// GetReplayBufferActive). Via SendRawRequest since OBSWebsocketDotNet 5.0.1 has no typed wrapper for
    /// GetRecordStatus, same escape-hatch reasoning as GetInputVolume/GetProfileParameter below. ProfileApplier
    /// needs this to know whether a heavy switch's SetVideoSettings/SetCurrentProfile calls would be rejected -
    /// unlike the replay buffer, a manual recording is a deliberate user action this app must not silently
    /// stop just to apply a profile's video settings.</summary>
    public bool GetRecordingActive() => SendRawRequest("GetRecordStatus")["outputActive"]?.Value<bool>() ?? false;

    public string GetRecordDirectory() => _obs.GetRecordDirectory();

    public List<string> GetSceneItemSourceNames(string sceneName)
        => _obs.GetSceneItemList(sceneName).Select(item => item.SourceName).ToList();

    /// <summary>
    /// Idempotent: creates a Display Capture source in the given scene targeting the primary monitor,
    /// unless a source with that name already exists. OBS creates monitor_capture sources with a
    /// placeholder "DUMMY" monitor_id, so the real primary-monitor id is resolved and applied afterwards.
    /// </summary>
    public void EnsureDisplayCaptureSource(string sceneName, string sourceName)
    {
        if (!GetSceneItemSourceNames(sceneName).Contains(sourceName))
        {
            _obs.CreateInput(sceneName, sourceName, "monitor_capture", new JObject(), true);
        }

        var currentMonitorId = _obs.GetInputSettings(sourceName).Settings["monitor_id"]?.ToString();
        if (!string.IsNullOrEmpty(currentMonitorId) && currentMonitorId != "DUMMY")
        {
            return;
        }

        var primaryMonitorId = GetPrimaryMonitorId(sourceName);
        if (primaryMonitorId is not null)
        {
            _obs.SetInputSettings(sourceName, new JObject { ["monitor_id"] = primaryMonitorId }, true);
        }
    }

    private string? GetPrimaryMonitorId(string sourceName)
    {
        var enabledItems = GetPropertyListItems(sourceName, "monitor_id");

        var primary = enabledItems.FirstOrDefault(item =>
            (item["itemName"]?.ToString() ?? "").Contains("Primary Monitor"));

        return (primary ?? enabledItems.FirstOrDefault())?["itemValue"]?.ToString();
    }

    /// <summary>
    /// Idempotent counterpart to <see cref="EnsureDisplayCaptureSource"/> for window/app capture: creates a
    /// window_capture source, disabled by default (Display Capture is the default active mode - callers use
    /// <see cref="SetCaptureMode"/> to flip which one is enabled). Stays in the scene permanently rather than
    /// being created/destroyed per switch, so re-selecting a window target doesn't need to relink anything.
    /// </summary>
    public void EnsureWindowCaptureSource(string sceneName, string sourceName)
    {
        if (!GetSceneItemSourceNames(sceneName).Contains(sourceName))
        {
            _obs.CreateInput(sceneName, sourceName, "window_capture", new JObject(), false);
        }
    }

    /// <summary>
    /// Available monitors as (Label, Value) pairs suitable for a picker: Label is human-readable
    /// ("Odyssey G61SH: 2560x1440 @ 0,0 (Primary Monitor)"), Value is the raw device-path string OBS expects
    /// back via <see cref="SetMonitorCaptureTarget"/>. Reads the property list off an existing monitor_capture
    /// source (e.g. "Display Capture") - OBS surfaces the full monitor list on any input of that kind,
    /// regardless of what it's currently set to.
    /// </summary>
    public List<(string Label, string Value)> GetMonitorOptions(string monitorSourceName) =>
        GetPropertyListItems(monitorSourceName, "monitor_id")
            .Select(item => (item["itemName"]?.ToString() ?? "", item["itemValue"]?.ToString() ?? ""))
            .Where(pair => pair.Item2.Length > 0)
            .ToList();

    /// <summary>
    /// Available windows/running apps as (Label, Value) pairs, e.g. ("[Spotify.exe]: SALEM - Baby Ratta",
    /// "SALEM - Baby Ratta:Chrome_WidgetWin_1:Spotify.exe"). Value is the exact string both window_capture's
    /// "window" setting and wasapi_process_output_capture's "window" setting expect - one enumeration serves
    /// both the video window-picker and the per-app audio picker. windowSourceName must be an existing input
    /// of kind window_capture or wasapi_process_output_capture (OBS surfaces the live running-window list on
    /// either kind's "window" property).
    /// </summary>
    public List<(string Label, string Value)> GetWindowOptions(string windowSourceName) =>
        GetPropertyListItems(windowSourceName, "window")
            .Where(item => item["itemEnabled"]?.Value<bool>() == true)
            .Select(item => (item["itemName"]?.ToString() ?? "", item["itemValue"]?.ToString() ?? ""))
            .Where(pair => pair.Item2.Length > 0)
            .ToList();

    private List<JToken> GetPropertyListItems(string inputName, string propertyName)
    {
        var response = SendRawRequest("GetInputPropertiesListPropertyItems", new JObject
        {
            ["inputName"] = inputName,
            ["propertyName"] = propertyName,
        });

        var items = response["propertyItems"] as JArray;
        return items?.Where(item => item["itemEnabled"]?.Value<bool>() == true).ToList() ?? [];
    }

    /// <summary>Points a monitor_capture source at a specific monitor (Value from <see cref="GetMonitorOptions"/>).</summary>
    public void SetMonitorCaptureTarget(string monitorSourceName, string monitorId) =>
        _obs.SetInputSettings(monitorSourceName, new JObject { ["monitor_id"] = monitorId }, true);

    /// <summary>
    /// Points a window_capture or wasapi_process_output_capture source at a specific window/app (Value from
    /// <see cref="GetWindowOptions"/>) - both input kinds use the same "window" settings key.
    /// </summary>
    public void SetWindowCaptureTarget(string windowSourceName, string windowValue) =>
        _obs.SetInputSettings(windowSourceName, new JObject { ["window"] = windowValue }, true);

    /// <summary>
    /// Flips which of the two video capture sources is live in the scene: monitor or window. Only one is ever
    /// enabled at a time - OBS composites whatever's enabled, so leaving both on would show both stacked.
    /// </summary>
    public void SetCaptureMode(string sceneName, string monitorSourceName, string windowSourceName, bool useWindowCapture)
    {
        SetSceneItemEnabled(sceneName, monitorSourceName, !useWindowCapture);
        SetSceneItemEnabled(sceneName, windowSourceName, useWindowCapture);
    }

    private void SetSceneItemEnabled(string sceneName, string sourceName, bool enabled)
    {
        var idResponse = SendRawRequest("GetSceneItemId", new JObject { ["sceneName"] = sceneName, ["sourceName"] = sourceName });
        var sceneItemId = idResponse["sceneItemId"]?.Value<int>();
        if (sceneItemId is null)
        {
            return;
        }

        SendRawRequest("SetSceneItemEnabled", new JObject
        {
            ["sceneName"] = sceneName,
            ["sceneItemId"] = sceneItemId.Value,
            ["sceneItemEnabled"] = enabled,
        });
    }

    private void OnRecordStateChanged(object? sender, RecordStateChangedEventArgs e)
    {
        if (e.OutputState.State == OutputState.OBS_WEBSOCKET_OUTPUT_STARTED)
        {
            RecordingStarted?.Invoke();
        }
        else if (e.OutputState.State == OutputState.OBS_WEBSOCKET_OUTPUT_STOPPED)
        {
            RecordingStopped?.Invoke(e.OutputState.OutputPath);
        }
    }

    private void OnReplayBufferSaved(object? sender, ReplayBufferSavedEventArgs e)
    {
        ReplayBufferSaved?.Invoke(e.SavedReplayPath);
    }

    public JObject SendRawRequest(string requestType, JObject? fields = null)
        => _obs.SendRequest(requestType, fields ?? new JObject());

    // --- Added for M5 (game detection + profile switching) ---

    /// <summary>Gets the audio mute state of an input (e.g. "Desktop Audio", "Mic/Aux").</summary>
    public bool GetInputMute(string inputName) => _obs.GetInputMute(inputName);

    /// <summary>
    /// Sets the audio mute state of an input. This is the "light switch" primitive used by ProfileApplier
    /// to toggle mic/desktop-audio per game profile without interrupting the recording/replay-buffer output.
    /// </summary>
    public void SetInputMute(string inputName, bool muted) => _obs.SetInputMute(inputName, muted);

    /// <summary>
    /// Gets an input's volume as a linear multiplier (1.0 = unity/0dB, 0.0 = silent) - via SendRawRequest
    /// rather than a typed SDK call, same escape-hatch reasoning as GetProfileParameter. Used to seed the
    /// Capture tab's per-source volume slider with the input's actual current level rather than assuming 100%.
    /// </summary>
    public double GetInputVolume(string inputName)
    {
        var response = SendRawRequest("GetInputVolume", new JObject { ["inputName"] = inputName });
        return response["inputVolumeMul"]?.Value<double>() ?? 1.0;
    }

    /// <summary>Sets an input's volume as a linear multiplier (1.0 = unity/0dB) - the live counterpart to
    /// ClipTrimmer's export-time volume filter, applied directly by OBS to the running input.</summary>
    public void SetInputVolume(string inputName, double volumeMul) =>
        SendRawRequest("SetInputVolume", new JObject { ["inputName"] = inputName, ["inputVolumeMul"] = volumeMul });

    /// <summary>Gets the current base/output video resolution and frame rate.</summary>
    public ObsVideoSettings GetVideoSettings() => _obs.GetVideoSettings();

    /// <summary>
    /// Sets base+output resolution and frame rate. Per obs-websocket v5, this is rejected while an output
    /// (record/replay buffer/stream) is running - callers must stop the relevant output first. This is the
    /// "heavy switch" resolution/fps primitive; ProfileApplier is responsible for the stop/apply/restart
    /// sequencing around it.
    /// </summary>
    public void SetVideoSettings(int baseWidth, int baseHeight, int outputWidth, int outputHeight, int fpsNumerator, int fpsDenominator)
        => _obs.SetVideoSettings(new ObsVideoSettings
        {
            BaseWidth = baseWidth,
            BaseHeight = baseHeight,
            OutputWidth = outputWidth,
            OutputHeight = outputHeight,
            FpsNumerator = fpsNumerator,
            FpsDenominator = fpsDenominator,
        });

    /// <summary>Name of the currently active OBS profile.</summary>
    public string GetCurrentProfileName() => _obs.GetProfileList().CurrentProfileName;

    /// <summary>Names of all OBS profiles known to this OBS instance.</summary>
    public List<string> GetProfileNames() => _obs.GetProfileList().Profiles;

    /// <summary>
    /// Switches OBS to a different (pre-provisioned) profile - the mechanism per-game encoder/quality
    /// presets (NvencSettings) ride on, since those aren't individually settable over the websocket API.
    /// Per obs-websocket v5, rejected while an output is running; see SetVideoSettings remarks.
    /// </summary>
    public void SetCurrentProfile(string profileName) => _obs.SetCurrentProfile(profileName);

    public void Dispose()
    {
        _obs.Disconnect();
        GC.SuppressFinalize(this);
    }

    // --- Added for M8 (per-application audio isolation) ---

    /// <summary>
    /// Creates a wasapi_process_output_capture source targeting one running app's audio (windowValue from
    /// <see cref="GetWindowOptions"/>), added to the scene enabled (it has no video, so being "enabled" just
    /// means "actually capturing" - nothing visible changes). Not idempotent by source name like the two
    /// Ensure* methods above: each call is meant to add a genuinely new tracked app, so AudioSourceManager is
    /// responsible for picking a not-yet-used inputName.
    /// </summary>
    public void CreateAppAudioSource(string sceneName, string inputName, string windowValue) =>
        _obs.CreateInput(sceneName, inputName, "wasapi_process_output_capture",
            new JObject { ["window"] = windowValue }, true);

    public void RemoveInput(string inputName) => _obs.RemoveInput(inputName);

    /// <summary>
    /// Routes an input to exactly one of OBS's 6 output audio tracks (1-6), clearing any others it might
    /// have been on. AudioSourceManager uses this so each provisioned source (built-in or per-app) feeds
    /// exactly the track it was assigned, matching the positional convention RecordingIngestCoordinator
    /// relies on when labeling a recorded file's audio streams afterward.
    /// </summary>
    public void SetInputAudioTrack(string inputName, int trackNumber)
    {
        var tracks = new JObject();
        for (var i = 1; i <= 6; i++)
        {
            tracks[i.ToString()] = i == trackNumber;
        }

        SendRawRequest("SetInputAudioTracks", new JObject { ["inputName"] = inputName, ["inputAudioTracks"] = tracks });
    }

    /// <summary>
    /// Reads an OBS profile's raw ini-style parameter (e.g. category "AdvOut", name "RecTracks") - the
    /// escape hatch for settings with no dedicated typed request, same idea as SendRawRequest itself.
    /// </summary>
    public string? GetProfileParameter(string category, string name)
    {
        var response = SendRawRequest("GetProfileParameter",
            new JObject { ["parameterCategory"] = category, ["parameterName"] = name });
        return response["parameterValue"]?.ToString();
    }

    public void SetProfileParameter(string category, string name, string value) =>
        SendRawRequest("SetProfileParameter",
            new JObject { ["parameterCategory"] = category, ["parameterName"] = name, ["parameterValue"] = value });
}
