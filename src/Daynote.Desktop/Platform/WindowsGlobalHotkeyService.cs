using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;
using Daynote.App.Input;

namespace Daynote.Desktop.Platform;

/// <summary>
/// Win32 <c>RegisterHotKey</c> for the Avalonia build on Windows. Hotkeys are delivered as
/// <c>WM_HOTKEY</c> to the thread that registered them, so this service owns a dedicated thread with a
/// message-only window and its own message loop, independent of Avalonia's windows (which may be
/// hidden to the tray). Events are marshalled back to the UI thread.
/// </summary>
/// <remarks>Written against the documented API; exercised only by the Windows build of the Avalonia app.</remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsGlobalHotkeyService : IGlobalHotkeyService
{
    private const int SummonId = 0xB0B0;
    private const int QuickNoteId = 0xB0B1;
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;
    private const uint WmApp = 0x8000;
    private const uint WmApply = WmApp + 1;
    private const uint ModAlt = 0x0001, ModControl = 0x0002, ModShift = 0x0004, ModWin = 0x0008, ModNoRepeat = 0x4000;
    private static readonly Hotkey QuickNoteChord = new(HotkeyModifiers.Alt, HotkeyKey.Oem3);

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly object _gate = new();
    private uint _threadId;
    private nint _hwnd;
    private Hotkey? _pending;
    private HotkeySetResult _lastResult = HotkeySetResult.Ok;
    private readonly ManualResetEventSlim _applied = new(false);
    private bool _disposed;

    public WindowsGlobalHotkeyService()
    {
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "Daynote hotkeys" };
        _thread.Start();
        _ready.Wait();
    }

    public event EventHandler? Pressed;

    public event EventHandler? QuickNotePressed;

    public Hotkey? Current { get; private set; }

    public void Attach(nint hwnd)
    {
    }

    public HotkeySetResult TrySet(Hotkey hotkey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!hotkey.IsValid)
        {
            return HotkeySetResult.Invalid;
        }

        lock (_gate)
        {
            _pending = hotkey;
            _applied.Reset();
        }

        PostThreadMessage(_threadId, WmApply, 0, 0);
        _applied.Wait(TimeSpan.FromSeconds(2));
        return _lastResult;
    }

    private void MessageLoop()
    {
        _threadId = GetCurrentThreadId();
        _hwnd = CreateWindowEx(0, "STATIC", "DaynoteHotkeys", 0, 0, 0, 0, 0, new nint(-3) /* HWND_MESSAGE */, 0, 0, 0);
        (uint quickMods, uint quickKey) = ToWin32(QuickNoteChord);
        RegisterHotKey(_hwnd, QuickNoteId, quickMods, quickKey);
        _ready.Set();

        while (GetMessage(out Msg message, 0, 0, 0) > 0)
        {
            if (message.Message == WmHotkey)
            {
                int id = (int)message.WParam;
                Dispatcher.UIThread.Post(() =>
                {
                    if (id == SummonId)
                    {
                        Pressed?.Invoke(this, EventArgs.Empty);
                    }
                    else if (id == QuickNoteId)
                    {
                        QuickNotePressed?.Invoke(this, EventArgs.Empty);
                    }
                });
            }
            else if (message.Message == WmApply)
            {
                ApplyPending();
            }
            else
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }

        UnregisterHotKey(_hwnd, SummonId);
        UnregisterHotKey(_hwnd, QuickNoteId);
        DestroyWindow(_hwnd);
    }

    private void ApplyPending()
    {
        Hotkey? next;
        lock (_gate)
        {
            next = _pending;
            _pending = null;
        }

        if (next is not { } hotkey)
        {
            _applied.Set();
            return;
        }

        UnregisterHotKey(_hwnd, SummonId);
        (uint mods, uint key) = ToWin32(hotkey);
        if (RegisterHotKey(_hwnd, SummonId, mods, key))
        {
            Current = hotkey;
            _lastResult = HotkeySetResult.Ok;
        }
        else
        {
            if (Current is { } kept)
            {
                (uint km, uint kk) = ToWin32(kept);
                RegisterHotKey(_hwnd, SummonId, km, kk);
            }

            _lastResult = HotkeySetResult.Conflict;
        }

        _applied.Set();
    }

    private static (uint Modifiers, uint VirtualKey) ToWin32(Hotkey hotkey)
    {
        uint fs = ModNoRepeat;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Control)) fs |= ModControl;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt)) fs |= ModAlt;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift)) fs |= ModShift;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Meta)) fs |= ModWin;
        return (fs, MapVirtualKey(hotkey.Key));
    }

    /// <summary>WPF's Key → Win32 virtual key, for the keys a chord can contain (mirrors KeyInterop for that subset).</summary>
    private static uint MapVirtualKey(HotkeyKey key) => key switch
    {
        >= HotkeyKey.A and <= HotkeyKey.Z => (uint)('A' + (key - HotkeyKey.A)),
        >= HotkeyKey.D0 and <= HotkeyKey.D9 => (uint)('0' + (key - HotkeyKey.D0)),
        >= HotkeyKey.F1 and <= HotkeyKey.F24 => 0x70u + (uint)(key - HotkeyKey.F1),
        >= HotkeyKey.NumPad0 and <= HotkeyKey.NumPad9 => 0x60u + (uint)(key - HotkeyKey.NumPad0),
        HotkeyKey.Space => 0x20, HotkeyKey.Left => 0x25, HotkeyKey.Up => 0x26, HotkeyKey.Right => 0x27, HotkeyKey.Down => 0x28,
        HotkeyKey.Home => 0x24, HotkeyKey.End => 0x23, HotkeyKey.PageUp => 0x21, HotkeyKey.PageDown => 0x22,
        HotkeyKey.Insert => 0x2D, HotkeyKey.Delete => 0x2E, HotkeyKey.Back => 0x08, HotkeyKey.Tab => 0x09,
        HotkeyKey.Return => 0x0D, HotkeyKey.Escape => 0x1B,
        HotkeyKey.OemSemicolon => 0xBA, HotkeyKey.OemPlus => 0xBB, HotkeyKey.OemComma => 0xBC, HotkeyKey.OemMinus => 0xBD,
        HotkeyKey.OemPeriod => 0xBE, HotkeyKey.OemQuestion => 0xBF, HotkeyKey.Oem3 => 0xC0,
        HotkeyKey.OemOpenBrackets => 0xDB, HotkeyKey.OemPipe => 0xDC, HotkeyKey.OemCloseBrackets => 0xDD, HotkeyKey.OemQuotes => 0xDE,
        _ => 0,
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        PostThreadMessage(_threadId, WmQuit, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
        _applied.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg message, nint hWnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref Msg message);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
