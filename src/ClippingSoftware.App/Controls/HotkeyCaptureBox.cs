using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ClippingSoftware.Shared.Interop;

namespace ClippingSoftware.App.Controls;

/// <summary>
/// Read-only TextBox that captures a single keystroke plus held modifiers as a Win32
/// (virtual-key, MOD_* bitmask) pair, for binding global hotkeys from Settings instead of
/// hand-editing the AppSettings row. Focus the box and press the desired combo:
/// VirtualKey/Modifiers update immediately (two-way bindable) and the box displays a
/// human-readable label such as "Ctrl+Alt+F10".
/// </summary>
public class HotkeyCaptureBox : TextBox
{
    public static readonly DependencyProperty VirtualKeyProperty = DependencyProperty.Register(
        nameof(VirtualKey), typeof(int), typeof(HotkeyCaptureBox),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCaptureChanged));

    public static readonly DependencyProperty ModifiersProperty = DependencyProperty.Register(
        nameof(Modifiers), typeof(int), typeof(HotkeyCaptureBox),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCaptureChanged));

    public int VirtualKey
    {
        get => (int)GetValue(VirtualKeyProperty);
        set => SetValue(VirtualKeyProperty, value);
    }

    public int Modifiers
    {
        get => (int)GetValue(ModifiersProperty);
        set => SetValue(ModifiersProperty, value);
    }

    public HotkeyCaptureBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Cursor = Cursors.Arrow;
        ToolTip = "Click, then press the desired key combination.";
        UpdateDisplayText();
    }

    private static void OnCaptureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((HotkeyCaptureBox)d).UpdateDisplayText();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Alt-held combos arrive as Key.System with the real key in e.SystemKey - unwrap it so
        // VirtualKeyFromKey below gets the actual pressed key rather than the sentinel value.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // A bare modifier press is not itself a capturable combo - RegisterHotKey needs a
        // non-modifier vk to fire on - so wait for the accompanying key instead of capturing here.
        if (IsModifierKey(key))
        {
            e.Handled = true;
            return;
        }

        var modifiers = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= Win32Hotkeys.MOD_CONTROL;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= Win32Hotkeys.MOD_ALT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= Win32Hotkeys.MOD_SHIFT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= Win32Hotkeys.MOD_WIN;

        VirtualKey = KeyInterop.VirtualKeyFromKey(key);
        Modifiers = modifiers;

        e.Handled = true;
    }

    private static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
        or Key.LWin or Key.RWin or Key.System;

    private void UpdateDisplayText()
    {
        Text = FormatDisplayString(VirtualKey, Modifiers);
    }

    /// <summary>Renders a (vk, MOD_* bitmask) pair as a human-readable label, e.g. "Ctrl+Alt+F10".</summary>
    public static string FormatDisplayString(int virtualKey, int modifiers)
    {
        if (virtualKey == 0)
        {
            return "(none)";
        }

        var parts = new List<string>();
        if ((modifiers & Win32Hotkeys.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & Win32Hotkeys.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & Win32Hotkeys.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & Win32Hotkeys.MOD_WIN) != 0) parts.Add("Win");

        parts.Add(KeyInterop.KeyFromVirtualKey(virtualKey).ToString());

        return string.Join("+", parts);
    }
}
