using ClippingSoftware.Core.HotkeyManager;
using ClippingSoftware.Core.Obs;

namespace ClippingSoftware.Core.Recording;

/// <summary>
/// Thin glue between a global hotkey and ObsController.SaveReplayBuffer(). Owns registering its own
/// hotkey id on the given GlobalHotkeyService so callers don't need to know about WM_HOTKEY plumbing
/// — just construct this and call RegisterSaveHotkey with the desired key combination.
///
/// Note: OBS itself requires the replay buffer to already be running (StartReplayBuffer succeeded)
/// before SaveReplayBuffer will do anything useful; this class silently no-ops if the buffer isn't
/// active rather than surfacing an OBS-side error to the user for what is, from their perspective,
/// just "the hotkey didn't do anything yet."
/// </summary>
public class ReplayBufferHotkeyHandler
{
    private readonly GlobalHotkeyService _hotkeyService;
    private readonly ObsController _obsController;
    private int? _registeredHotkeyId;

    public ReplayBufferHotkeyHandler(GlobalHotkeyService hotkeyService, ObsController obsController)
    {
        _hotkeyService = hotkeyService;
        _obsController = obsController;
    }

    /// <summary>
    /// Registers the given hotkey id/key/modifiers so pressing it triggers a replay buffer save.
    /// </summary>
    /// <returns>false if the OS refused the hotkey registration (e.g. already bound elsewhere).</returns>
    public bool RegisterSaveHotkey(int hotkeyId, int virtualKey, int modifiers)
    {
        var registered = _hotkeyService.RegisterHotkey(hotkeyId, virtualKey, modifiers, OnSaveHotkeyPressed);
        if (registered)
        {
            _registeredHotkeyId = hotkeyId;
        }

        return registered;
    }

    /// <summary>Unregisters the hotkey this handler owns, if any.</summary>
    public void Unregister()
    {
        if (_registeredHotkeyId is int id)
        {
            _hotkeyService.UnregisterHotkey(id);
            _registeredHotkeyId = null;
        }
    }

    private void OnSaveHotkeyPressed()
    {
        if (!_obsController.IsConnected || !_obsController.GetReplayBufferActive())
        {
            return;
        }

        _obsController.SaveReplayBuffer();
    }
}
