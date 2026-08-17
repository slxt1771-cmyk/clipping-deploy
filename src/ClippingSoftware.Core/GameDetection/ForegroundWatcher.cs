using System.ComponentModel;
using System.Diagnostics;
using ClippingSoftware.Shared.Interop;

namespace ClippingSoftware.Core.GameDetection;

/// <summary>
/// Watches for foreground-window changes using SetWinEventHook(EVENT_SYSTEM_FOREGROUND) on a dedicated
/// message-pump thread, with a poll-loop fallback (belt-and-suspenders, per the architecture plan) in case
/// the hook misses an event or fails to install (e.g. under certain security/session contexts). The hook is
/// the real detection path and costs nothing while idle (it's asleep in GetMessage until Windows actually
/// posts an event); the poll is only a safety net for the hook failing, not the primary signal, so it
/// doesn't need to be fast - a several-second detection lag from the fallback alone is unnoticeable for
/// game-profile switching, and this app's whole point is staying out of a game's way, so this runs forever
/// for the life of the app and should cost as little steady-state CPU as the fallback role allows.
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    // Rooted for the lifetime of the watcher so the GC never collects the delegate while native code
    // still holds a pointer to it (a classic SetWinEventHook pitfall).
    private readonly Win32Window.WinEventDelegate _winEventDelegate;

    private readonly object _lock = new();
    private Thread? _hookThread;
    private volatile uint _hookThreadId;

    // Signaled once HookThreadProc has actually installed the hook and has a live message queue to post
    // to - _hookThreadId alone isn't enough of a readiness signal, since Dispose() could otherwise run
    // (and see _hookThreadId still 0) before the OS has scheduled the new thread far enough to set it,
    // in which case Dispose() would skip PostThreadMessage entirely and the thread/hook would leak forever
    // (Join would just time out with nothing left to ever wake it).
    private readonly ManualResetEventSlim _hookThreadReady = new(false);
    private Timer? _pollTimer;
    private IntPtr _lastHwnd;
    private bool _started;
    private bool _disposed;

    public event Action<IntPtr, string>? ForegroundChanged;

    public ForegroundWatcher()
    {
        _winEventDelegate = OnWinEvent;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_started || _disposed)
            {
                return;
            }

            _started = true;

            _hookThread = new Thread(HookThreadProc)
            {
                IsBackground = true,
                Name = "ForegroundWatcher-WinEventHook",
                // Lets the OS scheduler favor a running game over this thread whenever the CPU is actually
                // contended - this thread spends nearly all its life blocked in GetMessage waiting for a
                // foreground-change event, so the lower priority only matters (and only costs anything)
                // under real contention, which is exactly when it should yield.
                Priority = ThreadPriority.BelowNormal,
            };
            _hookThread.SetApartmentState(ApartmentState.STA);
            _hookThread.Start();

            _pollTimer = new Timer(_ => PollForegroundWindow(), null, PollInterval, PollInterval);
        }

        // Report whatever is already focused immediately, rather than waiting for the first change/poll tick.
        PollForegroundWindow();
    }

    private void HookThreadProc()
    {
        _hookThreadId = Win32Window.GetCurrentThreadId();

        var hook = Win32Window.SetWinEventHook(
            Win32Window.EVENT_SYSTEM_FOREGROUND,
            Win32Window.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventDelegate,
            0,
            0,
            Win32Window.WINEVENT_OUTOFCONTEXT | Win32Window.WINEVENT_SKIPOWNPROCESS);

        // The thread's message queue exists by this point (SetWinEventHook with WINEVENT_OUTOFCONTEXT
        // creates one for the calling thread if it doesn't already have one) - safe to signal Dispose()
        // that PostThreadMessage will now actually reach this thread's GetMessage loop below.
        _hookThreadReady.Set();

        try
        {
            // WINEVENT_OUTOFCONTEXT delivers callbacks via this thread's message queue, so it needs a pump.
            while (Win32Window.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                Win32Window.TranslateMessage(ref msg);
                Win32Window.DispatchMessage(ref msg);
            }
        }
        finally
        {
            if (hook != IntPtr.Zero)
            {
                Win32Window.UnhookWinEvent(hook);
            }
        }
    }

    private void OnWinEvent(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType != Win32Window.EVENT_SYSTEM_FOREGROUND || hwnd == IntPtr.Zero)
        {
            return;
        }

        HandleForegroundCandidate(hwnd);
    }

    private void PollForegroundWindow()
    {
        var hwnd = Win32Window.GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
        {
            HandleForegroundCandidate(hwnd);
        }
    }

    private void HandleForegroundCandidate(IntPtr hwnd)
    {
        lock (_lock)
        {
            if (hwnd == _lastHwnd)
            {
                return;
            }
        }

        var processName = TryResolveProcessName(hwnd);
        if (processName is null)
        {
            // Elevated/protected process (common for anti-cheat-guarded games) or the process already
            // exited by the time we looked it up - swallow rather than crash the watcher.
            return;
        }

        lock (_lock)
        {
            if (hwnd == _lastHwnd)
            {
                return;
            }

            _lastHwnd = hwnd;
        }

        ForegroundChanged?.Invoke(hwnd, processName);
    }

    private static string? TryResolveProcessName(IntPtr hwnd)
    {
        try
        {
            Win32Window.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0)
            {
                return null;
            }

            using var process = Process.GetProcessById((int)pid);

            // MainModule.FileName throws Win32Exception (access denied) for elevated/protected processes.
            var fullPath = process.MainModule?.FileName;
            return string.IsNullOrEmpty(fullPath)
                ? process.ProcessName + ".exe"
                : Path.GetFileName(fullPath);
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // Process exited between GetWindowThreadProcessId and GetProcessById.
            return null;
        }
        catch (ArgumentException)
        {
            // No process with that id (already exited).
            return null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _pollTimer?.Dispose();

        // Wait for the hook thread to actually have a message queue before trying to post to it - see
        // _hookThreadReady's doc comment. Only relevant if Start() was ever called (_hookThread not null);
        // the 2s timeout is defensive (matches the Join below) in case the thread never gets there at all.
        if (_hookThread is not null && _hookThreadReady.Wait(TimeSpan.FromSeconds(2)) && _hookThreadId != 0)
        {
            Win32Window.PostThreadMessage(_hookThreadId, Win32Window.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        _hookThread?.Join(TimeSpan.FromSeconds(2));
        _hookThreadReady.Dispose();
    }
}
