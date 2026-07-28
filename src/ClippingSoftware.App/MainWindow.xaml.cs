using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using ClippingSoftware.App.Controls;
using ClippingSoftware.App.ViewModels;
using ClippingSoftware.Core.HotkeyManager;
using ClippingSoftware.Core.Recording;
using ClippingSoftware.Shared.Interop;

namespace ClippingSoftware.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private GlobalHotkeyService? _hotkeyService;
    private ReplayBufferHotkeyHandler? _replayBufferHotkeyHandler;

    /// <summary>Set right before a real exit (QUIT rail button after its confirmation, or the tray
    /// icon's Exit item via RequestExit) so Closing knows to let the window actually close instead of
    /// hiding it - see Closing's own comment for why the two need to be told apart.</summary>
    private bool _isReallyQuitting;

    public MainViewModel ViewModel => _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();

        // Set from a runtime file path rather than XAML Icon="Assets/tray.ico": that file is a loose
        // CopyToOutputDirectory asset (see TrayIconController.LoadTrayIcon), not a compiled WPF pack
        // resource, so the XAML pack-URI form can't locate it and throws at parse time.
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tray.ico");
        if (File.Exists(iconPath))
        {
            Icon = BitmapFrame.Create(new Uri(iconPath, UriKind.Absolute));
        }

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            _hotkeyService = new GlobalHotkeyService(handle);
            _replayBufferHotkeyHandler = new ReplayBufferHotkeyHandler(_hotkeyService, _viewModel.ObsController);

            RegisterHotkeysFromSettings();

            // Custom titlebar (M15, WindowChrome) fix: without this, a maximized window overhangs the
            // taskbar/screen edge by a few pixels, because WindowChrome removes the OS non-client frame
            // that normally clips maximized bounds to the work area. See Win32Window's doc comment.
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);

            // Deliberately not WindowState="Maximized" in XAML: that initializes the window already
            // maximized, which sends WM_GETMINMAXINFO before this hook exists to correct it (the hook
            // can only affect messages sent after AddHook runs) - the overhang bug showed up even with
            // the hook in place until this was moved here, confirming the ordering was the cause.
            // Maximizing here, after the hook is attached, means the message that actually matters gets
            // intercepted correctly.
            WindowState = WindowState.Maximized;
        };

        // The Settings tab lets the user rebind either hotkey and writes the new values into
        // AppSettings via SaveSettingsCommand, but GlobalHotkeyService/ReplayBufferHotkeyHandler live
        // here rather than on MainViewModel (they need the real HWND from SourceInitialized above).
        // Re-register both ids against whatever was just saved so a rebind takes effect immediately
        // instead of only after restart.
        _viewModel.HotkeySettingsChanged += () =>
        {
            _replayBufferHotkeyHandler?.Unregister();
            _hotkeyService?.UnregisterHotkey(2);
            RegisterHotkeysFromSettings();
        };

        Closed += (_, _) =>
        {
            _replayBufferHotkeyHandler?.Unregister();
            _hotkeyService?.Dispose();
        };

        // The titlebar X (and Alt+F4) now minimizes to tray instead of exiting - this is meant to be an
        // always-on background recorder (replay buffer/hotkeys keep running), so an accidental X shouldn't
        // stop that. Real exit is the rail's QUIT button (with confirmation) or the tray icon's Exit item
        // (RequestExit) - both set _isReallyQuitting first so this handler lets the close through instead
        // of cancelling it.
        Closing += (_, e) =>
        {
            if (_isReallyQuitting)
            {
                return;
            }

            e.Cancel = true;
            Hide();
        };
    }

    /// <summary>Restores the window after it's been minimized-to-tray via the X button - wired to the
    /// tray icon's double-click and its "Open" menu item (see TrayIconController).</summary>
    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    /// <summary>Real, unconditional exit - used by the tray icon's Exit item, which (unlike the rail's
    /// QUIT button) has its own long-established "this just quits" convention and doesn't need a second
    /// confirmation on top of it.</summary>
    public void RequestExit()
    {
        _isReallyQuitting = true;
        Close();
    }

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "Quit Clipping Software? This stops the replay buffer and ends any active recording.",
            "Quit Clipping Software",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
        {
            RequestExit();
        }
    }

    /// <summary>
    /// Registers both global hotkeys (id 1 = save replay buffer, id 2 = start/stop recording) from
    /// the current AppSettings values. Used both for the initial registration in SourceInitialized
    /// and to re-register after a save from the Settings tab (see HotkeySettingsChanged above) - the
    /// caller is expected to have already unregistered ids 1/2 in the latter case, since
    /// GlobalHotkeyService.RegisterHotkey throws if the id is still in use.
    /// RegisterHotKey can fail if the combo is already owned system-wide by another app; that's
    /// surfaced via StatusMessage rather than silently ignored.
    /// </summary>
    private void RegisterHotkeysFromSettings()
    {
        if (_hotkeyService == null || _replayBufferHotkeyHandler == null)
        {
            return;
        }

        var settings = _viewModel.Settings;
        var failures = new List<string>();

        var replayRegistered = _replayBufferHotkeyHandler.RegisterSaveHotkey(
            1, settings.ReplayBufferSaveHotkeyVk, settings.ReplayBufferSaveHotkeyModifiers);
        if (!replayRegistered)
        {
            var label = HotkeyCaptureBox.FormatDisplayString(settings.ReplayBufferSaveHotkeyVk, settings.ReplayBufferSaveHotkeyModifiers);
            failures.Add($"save replay buffer ({label})");
        }

        var toggleRegistered = _hotkeyService.RegisterHotkey(
            2, settings.StartStopRecordingHotkeyVk, settings.StartStopRecordingHotkeyModifiers,
            () => _viewModel.ToggleRecording());
        if (!toggleRegistered)
        {
            var label = HotkeyCaptureBox.FormatDisplayString(settings.StartStopRecordingHotkeyVk, settings.StartStopRecordingHotkeyModifiers);
            failures.Add($"start/stop recording ({label})");
        }

        if (failures.Count > 0)
        {
            _viewModel.StatusMessage = $"Could not register hotkey(s) - already in use by another app: {string.Join(", ", failures)}.";
        }
    }

    /// <summary>Clamps WM_GETMINMAXINFO's maximized bounds to the actual monitor work area (excluding
    /// the taskbar) instead of the full monitor rect - see the doc comment on Win32Window's
    /// WM_GETMINMAXINFO section for why WindowChrome needs this at all.</summary>
    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Win32Window.WM_GETMINMAXINFO)
        {
            return IntPtr.Zero;
        }

        var monitor = Win32Window.MonitorFromWindow(hwnd, Win32Window.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var monitorInfo = new Win32Window.MONITORINFO { cbSize = (uint)Marshal.SizeOf<Win32Window.MONITORINFO>() };
        if (!Win32Window.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return IntPtr.Zero;
        }

        var mmi = Marshal.PtrToStructure<Win32Window.MINMAXINFO>(lParam);
        var workArea = monitorInfo.rcWork;
        var monitorArea = monitorInfo.rcMonitor;

        mmi.ptMaxPosition.X = workArea.Left - monitorArea.Left;
        mmi.ptMaxPosition.Y = workArea.Top - monitorArea.Top;
        mmi.ptMaxSize.X = workArea.Width;
        mmi.ptMaxSize.Y = workArea.Height;

        Marshal.StructureToPtr(mmi, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }

    // --- Custom titlebar caption buttons (M15) ---

    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);
}
