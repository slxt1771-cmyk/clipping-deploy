using ClippingSoftware.Shared.Interop;

namespace ClippingSoftware.Core.GameDetection;

public enum WindowFullscreenState
{
    Windowed,
    BorderlessFullscreen,
    ExclusiveFullscreen,
}

/// <summary>
/// Classifies a window as Fullscreen / Borderless-Fullscreen / Windowed by comparing its rect against the
/// containing monitor's bounds.
///
/// Known limitation: distinguishing true exclusive fullscreen (DXGI swap-chain exclusive mode) from
/// borderless-fullscreen-windowed is not reliably possible from window rect + style bits alone - both fill
/// the monitor identically at the Win32 level, and many modern game engines implement "Fullscreen" as
/// borderless-windowed under the hood regardless of what the in-game menu calls it. The style-bit check
/// below (WS_POPUP vs WS_CAPTION/WS_THICKFRAME) is a best-effort signal, not a guarantee. For the purposes
/// this app actually needs (GameProfile.RequireFullscreenOrBorderless), use <see cref="IsFullscreenOrBorderless"/>,
/// which collapses both fullscreen-like states into a single boolean and is unaffected by this ambiguity.
/// </summary>
public static class FullscreenHeuristic
{
    public static WindowFullscreenState Classify(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Win32Window.GetWindowRect(hwnd, out var windowRect))
        {
            return WindowFullscreenState.Windowed;
        }

        var monitor = Win32Window.MonitorFromWindow(hwnd, Win32Window.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return WindowFullscreenState.Windowed;
        }

        var monitorInfo = new Win32Window.MONITORINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32Window.MONITORINFO>(),
        };

        if (!Win32Window.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return WindowFullscreenState.Windowed;
        }

        var coversMonitor =
            windowRect.Left <= monitorInfo.rcMonitor.Left &&
            windowRect.Top <= monitorInfo.rcMonitor.Top &&
            windowRect.Right >= monitorInfo.rcMonitor.Right &&
            windowRect.Bottom >= monitorInfo.rcMonitor.Bottom;

        if (!coversMonitor)
        {
            return WindowFullscreenState.Windowed;
        }

        var style = Win32Window.GetWindowStyle(hwnd);
        var hasChrome = (style & Win32Window.WS_CAPTION) != 0 || (style & Win32Window.WS_THICKFRAME) != 0;
        if (hasChrome)
        {
            // Still has a title bar / resizable border despite covering the monitor - treat as windowed
            // (e.g. an unusual maximized window on a monitor with no taskbar margin).
            return WindowFullscreenState.Windowed;
        }

        var isPopupStyle = (style & Win32Window.WS_POPUP) != 0;
        return isPopupStyle ? WindowFullscreenState.BorderlessFullscreen : WindowFullscreenState.ExclusiveFullscreen;
    }

    public static bool IsFullscreenOrBorderless(IntPtr hwnd) => Classify(hwnd) != WindowFullscreenState.Windowed;
}
