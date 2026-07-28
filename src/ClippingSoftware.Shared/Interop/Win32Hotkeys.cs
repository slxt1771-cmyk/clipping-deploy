using System.Runtime.InteropServices;

namespace ClippingSoftware.Shared.Interop;

/// <summary>
/// P/Invoke signatures for registering Windows global hotkeys (RegisterHotKey/UnregisterHotKey)
/// and handling the resulting WM_HOTKEY window message. These work system-wide, including while
/// another application (e.g. a fullscreen game) has input focus, which is why global hotkeys use
/// this API instead of normal WPF KeyDown handling.
/// </summary>
public static class Win32Hotkeys
{
    /// <summary>
    /// Window message posted to the owning window when a registered hotkey combination is pressed.
    /// wParam holds the hotkey id passed to RegisterHotKey; lParam packs the modifiers (low word)
    /// and virtual-key code (high word).
    /// </summary>
    public const int WM_HOTKEY = 0x0312;

    // Modifier flags for RegisterHotKey's fsModifiers parameter. Combine with bitwise OR.
    public const int MOD_ALT = 0x0001;
    public const int MOD_CONTROL = 0x0002;
    public const int MOD_SHIFT = 0x0004;
    public const int MOD_WIN = 0x0008;

    /// <summary>
    /// Windows Vista+: suppresses repeated WM_HOTKEY messages while the key combination is held down.
    /// Recommended for all new registrations.
    /// </summary>
    public const int MOD_NOREPEAT = 0x4000;

    // A few commonly used virtual-key codes for default hotkey bindings.
    public const int VK_F9 = 0x78;
    public const int VK_F10 = 0x79;
    public const int VK_F11 = 0x7A;
    public const int VK_F12 = 0x7B;

    /// <summary>
    /// Registers a system-wide hotkey. The messages arrive as WM_HOTKEY on the message queue of the
    /// thread that owns <paramref name="hWnd"/>, so hWnd must belong to a window pumping messages
    /// (e.g. a WPF window's HwndSource) on the calling thread.
    /// </summary>
    /// <param name="hWnd">Window that will receive WM_HOTKEY messages.</param>
    /// <param name="id">Caller-chosen identifier, unique per hWnd, used to unregister later and to
    /// distinguish which hotkey fired in the WM_HOTKEY handler.</param>
    /// <param name="fsModifiers">Bitwise combination of MOD_* constants.</param>
    /// <param name="vk">Virtual-key code of the hotkey.</param>
    /// <returns>false if registration failed (e.g. already bound system-wide by another app).</returns>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    /// <summary>
    /// Unregisters a hotkey previously registered with the same hWnd/id pair.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
