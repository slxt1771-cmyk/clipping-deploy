using System.Runtime.InteropServices;

namespace ClippingSoftware.Shared.Interop;

/// <summary>
/// Shared Win32 P/Invoke signatures for foreground-window detection and monitor/fullscreen geometry.
/// Kept separate from any hotkey-related interop (see Win32Hotkeys.cs) to avoid touching that file.
/// </summary>
public static class Win32Window
{
    // --- SetWinEventHook (foreground-change notifications) ---

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    public delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    // --- Foreground window / process resolution ---

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // --- Window / monitor geometry (fullscreen heuristic) ---

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    // --- Window style bits (used as a best-effort signal for borderless vs exclusive fullscreen) ---

    public const int GWL_STYLE = -16;
    public const long WS_CAPTION = 0x00C00000L;
    public const long WS_POPUP = unchecked((long)0x80000000L);
    public const long WS_THICKFRAME = 0x00040000L;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", CharSet = CharSet.Auto)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", CharSet = CharSet.Auto)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    public static long GetWindowStyle(IntPtr hWnd)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, GWL_STYLE).ToInt64() : GetWindowLong32(hWnd, GWL_STYLE);

    // --- Message loop plumbing for a dedicated SetWinEventHook thread ---

    public const uint WM_QUIT = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    // --- GetSystemMetrics (current primary-display resolution, for GameProfile auto-detect resolution) ---
    //
    // Reflects whatever the OS currently reports as the primary display's pixel resolution - including a
    // resolution *override* (e.g. Nvidia Control Panel's "stretched"/custom-resolution scaling), since that
    // changes the actual reported display mode system-wide rather than just how the panel scales the final
    // image. GetSystemMetrics is a live query (not cached), so calling it right before a heavy profile
    // switch always reflects whatever's active at that moment.

    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    // --- WM_GETMINMAXINFO (custom-chrome maximized-bounds fix, M15) ---
    //
    // WindowChrome removes the OS's own non-client frame, which is what normally clips a maximized
    // window to the monitor's work area (excluding the taskbar) instead of the full monitor bounds.
    // Without this fix a maximized custom-chrome window overhangs past the taskbar/screen edge by a
    // few pixels. Handling WM_GETMINMAXINFO and writing the real work-area bounds back into the
    // MINMAXINFO struct is the standard, correct fix (not a padding hack) - reuses RECT/MONITORINFO/
    // MonitorFromWindow/GetMonitorInfo already declared above for FullscreenHeuristic.

    public const int WM_GETMINMAXINFO = 0x0024;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }
}
