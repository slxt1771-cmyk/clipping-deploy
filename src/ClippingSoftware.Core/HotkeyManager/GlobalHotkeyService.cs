using System.Windows.Interop;
using ClippingSoftware.Shared.Interop;

namespace ClippingSoftware.Core.HotkeyManager;

/// <summary>
/// Registers/unregisters Windows global hotkeys (RegisterHotKey/WM_HOTKEY) and dispatches them to
/// caller-supplied callbacks. These fire even when another application — e.g. a fullscreen game —
/// has input focus, unlike normal WPF KeyDown handling.
///
/// This must be constructed with a real window handle whose message pump is running, so it can only
/// be built after the owning WPF Window has a handle. In practice that means constructing it in (or
/// after) the Window's SourceInitialized event:
///
/// <code>
/// public MainWindow()
/// {
///     InitializeComponent();
///     SourceInitialized += (_, _) =>
///     {
///         var handle = new WindowInteropHelper(this).Handle;
///         _hotkeyService = new GlobalHotkeyService(handle);
///         _hotkeyService.RegisterHotkey(1, Win32Hotkeys.VK_F10, Win32Hotkeys.MOD_CONTROL | Win32Hotkeys.MOD_ALT, OnSaveReplayBufferHotkey);
///     };
/// }
/// </code>
///
/// Dispose() unregisters every hotkey this instance owns and removes its message hook. Do this
/// before the window closes (e.g. in Window.Closed), since the handle becomes invalid afterward.
/// </summary>
public class GlobalHotkeyService : IDisposable
{
    private readonly IntPtr _windowHandle;
    private readonly HwndSource _hwndSource;
    private readonly Dictionary<int, Action> _callbacksByHotkeyId = new();
    private bool _disposed;

    /// <param name="windowHandle">
    /// A real HWND with an active message pump, e.g. <c>new WindowInteropHelper(window).Handle</c>
    /// obtained during or after the window's SourceInitialized event.
    /// </param>
    public GlobalHotkeyService(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle is zero. Construct GlobalHotkeyService after the window's SourceInitialized event, not before.", nameof(windowHandle));
        }

        _windowHandle = windowHandle;

        _hwndSource = HwndSource.FromHwnd(windowHandle)
            ?? throw new InvalidOperationException("No HwndSource found for the given window handle. Construct GlobalHotkeyService after the window's SourceInitialized event.");

        _hwndSource.AddHook(WndProc);
    }

    /// <summary>
    /// Registers a system-wide hotkey and wires it to <paramref name="callback"/>. The callback runs
    /// on the WPF UI thread (it's invoked from the window's message loop), so it's safe to touch
    /// ViewModels/UI directly.
    /// </summary>
    /// <param name="id">Caller-chosen id, unique within this service instance.</param>
    /// <param name="virtualKey">Win32 virtual-key code, e.g. Win32Hotkeys.VK_F10.</param>
    /// <param name="modifiers">Bitwise OR of Win32Hotkeys.MOD_* constants. MOD_NOREPEAT is
    /// recommended and NOT added automatically — pass it explicitly if you want it.</param>
    /// <returns>
    /// false if the OS refused the registration (e.g. another app already owns that exact
    /// combination system-wide). No callback is stored in that case.
    /// </returns>
    public bool RegisterHotkey(int id, int virtualKey, int modifiers, Action callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_callbacksByHotkeyId.ContainsKey(id))
        {
            throw new ArgumentException($"Hotkey id {id} is already registered on this service instance.", nameof(id));
        }

        var registered = Win32Hotkeys.RegisterHotKey(_windowHandle, id, (uint)modifiers, (uint)virtualKey);
        if (registered)
        {
            _callbacksByHotkeyId[id] = callback;
        }

        return registered;
    }

    /// <summary>
    /// Unregisters a hotkey previously registered on this instance. No-op if the id isn't registered.
    /// </summary>
    public void UnregisterHotkey(int id)
    {
        if (_disposed)
        {
            return;
        }

        if (_callbacksByHotkeyId.Remove(id))
        {
            Win32Hotkeys.UnregisterHotKey(_windowHandle, id);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Hotkeys.WM_HOTKEY && _callbacksByHotkeyId.TryGetValue(wParam.ToInt32(), out var callback))
        {
            callback();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var id in _callbacksByHotkeyId.Keys)
        {
            Win32Hotkeys.UnregisterHotKey(_windowHandle, id);
        }

        _callbacksByHotkeyId.Clear();
        _hwndSource.RemoveHook(WndProc);
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
