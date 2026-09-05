using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Input;
using System.Windows.Interop;

namespace Daynote.App.Input;

/// <summary>
/// The Win32 global-hotkey presence. Registers the summon chord with <c>RegisterHotKey</c> against the
/// resident window handle and listens for <c>WM_HOTKEY</c> through an <see cref="HwndSource"/> hook, so
/// the chord fires even when the window is hidden to the tray. Registration is idempotent and always
/// unregisters the previous id before claiming a new one; a refused chord leaves the prior one intact.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0xB0B0; // The configurable summon slot; any stable per-window id works.
    private const int QuickNoteId = 0xB0B1; // The fixed Alt+` quick-note slot.

    /// <summary>Alt+` (Oem3): fixed, not user-configurable — mirrored for display by AppShortcuts.</summary>
    private static readonly Hotkey QuickNoteChord = new(HotkeyModifiers.Alt, HotkeyKey.Oem3);

    private HwndSource? _source;
    private nint _hwnd;
    private Hotkey? _pending;
    private bool _registered;
    private bool _quickRegistered;
    private bool _disposed;

    public event EventHandler? Pressed;

    public event EventHandler? QuickNotePressed;

    public Hotkey? Current { get; private set; }

    public void Attach(nint hwnd)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hwnd == hwnd && _source is not null)
        {
            return;
        }

        DetachHook();
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);

        // The fixed quick-note chord registers once per handle; a refusal (another app owns Alt+`)
        // just leaves quick-note inactive — the configurable summon key is unaffected.
        (uint quickModifiers, uint quickKey) = HotkeyInterop.ToWin32(QuickNoteChord);
        _quickRegistered = RegisterHotKey(hwnd, QuickNoteId, quickModifiers, quickKey);

        // Apply whatever was requested before the handle existed.
        if (_pending is { } queued)
        {
            _pending = null;
            _ = TrySet(queued);
        }
    }

    public HotkeySetResult TrySet(Hotkey hotkey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!hotkey.IsValid)
        {
            return HotkeySetResult.Invalid;
        }

        // No handle yet: remember the desired chord and apply it on Attach.
        if (_hwnd == 0)
        {
            _pending = hotkey;
            Current = hotkey;
            return HotkeySetResult.Ok;
        }

        Unregister();
        (uint fsModifiers, uint virtualKey) = HotkeyInterop.ToWin32(hotkey);
        if (!RegisterHotKey(_hwnd, HotkeyId, fsModifiers, virtualKey))
        {
            // The OS refused it (another app owns the chord). Re-claim the previous one so the user is
            // never left with no working summon key after a failed change.
            if (Current is { } previous)
            {
                (uint pfs, uint pvk) = HotkeyInterop.ToWin32(previous);
                _registered = RegisterHotKey(_hwnd, HotkeyId, pfs, pvk);
            }

            return HotkeySetResult.Conflict;
        }

        _registered = true;
        Current = hotkey;
        return HotkeySetResult.Ok;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmHotkey && (int)wParam == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        else if (msg == WmHotkey && (int)wParam == QuickNoteId)
        {
            handled = true;
            QuickNotePressed?.Invoke(this, EventArgs.Empty);
        }

        return nint.Zero;
    }

    private void Unregister()
    {
        if (_registered && _hwnd != 0)
        {
            _ = UnregisterHotKey(_hwnd, HotkeyId);
            _registered = false;
        }
    }

    private void DetachHook()
    {
        Unregister();
        if (_quickRegistered && _hwnd != 0)
        {
            _ = UnregisterHotKey(_hwnd, QuickNoteId);
            _quickRegistered = false;
        }

        _source?.RemoveHook(WndProc);
        _source = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DetachHook();
        _hwnd = 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
