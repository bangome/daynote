using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Daynote.App.Input;

namespace Daynote.Desktop.Platform;

/// <summary>
/// The macOS global-hotkey presence over Carbon's <c>RegisterEventHotKey</c>, which still is the API
/// for system-wide chords that fire while the app is in the background and needs no Accessibility
/// permission. Modifiers map literally: Ctrl→⌃, Alt→⌥, Shift→⇧, Win→⌘, so a chord saved by the
/// Windows app reads the same way here. The event handler runs on the main thread, which is Avalonia's
/// UI thread, so the events are raised directly.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacGlobalHotkeyService : IGlobalHotkeyService
{
    private const uint SummonId = 0xB0B0;
    private const uint QuickNoteId = 0xB0B1;
    private static readonly Hotkey QuickNoteChord = new(HotkeyModifiers.Alt, HotkeyKey.Oem3);

    // Keeps the native callback alive for the life of the service.
    private readonly Carbon.EventHandlerProc _handler;
    private IntPtr _handlerRef;
    private IntPtr _summonRef;
    private IntPtr _quickRef;
    private bool _disposed;

    public MacGlobalHotkeyService()
    {
        _handler = OnHotkeyEvent;
        var spec = new Carbon.EventTypeSpec { EventClass = Carbon.KEventClassKeyboard, EventKind = Carbon.KEventHotKeyPressed };
        Carbon.InstallEventHandler(Carbon.GetApplicationEventTarget(), _handler, 1, ref spec, IntPtr.Zero, out _handlerRef);

        // The fixed quick-note chord (⌥`). A refusal just leaves quick-note inactive.
        Register(QuickNoteChord, QuickNoteId, ref _quickRef);
    }

    public event EventHandler? Pressed;

    public event EventHandler? QuickNotePressed;

    public Hotkey? Current { get; private set; }

    /// <summary>No window handle is involved on macOS; registration is application-wide.</summary>
    public void Attach(nint hwnd)
    {
    }

    public HotkeySetResult TrySet(Hotkey hotkey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!hotkey.IsValid || KeyCodes.ToVirtualKey(hotkey.Key) is null)
        {
            return HotkeySetResult.Invalid;
        }

        IntPtr previous = _summonRef;
        _summonRef = IntPtr.Zero;
        if (previous != IntPtr.Zero)
        {
            Carbon.UnregisterEventHotKey(previous);
        }

        if (Register(hotkey, SummonId, ref _summonRef))
        {
            Current = hotkey;
            return HotkeySetResult.Ok;
        }

        // Refused (another app owns it): put the previous chord back so the user keeps a working key.
        if (Current is { } kept)
        {
            Register(kept, SummonId, ref _summonRef);
        }

        return HotkeySetResult.Conflict;
    }

    private static bool Register(Hotkey hotkey, uint id, ref IntPtr reference)
    {
        uint? code = KeyCodes.ToVirtualKey(hotkey.Key);
        if (code is null)
        {
            return false;
        }

        var hotKeyId = new Carbon.EventHotKeyID { Signature = Carbon.Signature, Id = id };
        int status = Carbon.RegisterEventHotKey(
            code.Value, ToCarbonModifiers(hotkey.Modifiers), hotKeyId, Carbon.GetApplicationEventTarget(), 0, out reference);
        if (status != 0)
        {
            reference = IntPtr.Zero;
            return false;
        }

        return true;
    }

    private static uint ToCarbonModifiers(HotkeyModifiers modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            result |= Carbon.ControlKey;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            result |= Carbon.OptionKey;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            result |= Carbon.ShiftKey;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Meta))
        {
            result |= Carbon.CmdKey;
        }

        return result;
    }

    private int OnHotkeyEvent(IntPtr nextHandler, IntPtr theEvent, IntPtr userData)
    {
        var hotKeyId = default(Carbon.EventHotKeyID);
        int status = Carbon.GetEventParameter(
            theEvent, Carbon.KEventParamDirectObject, Carbon.TypeEventHotKeyID, IntPtr.Zero,
            (uint)Marshal.SizeOf<Carbon.EventHotKeyID>(), IntPtr.Zero, ref hotKeyId);
        if (status == 0 && hotKeyId.Signature == Carbon.Signature)
        {
            if (hotKeyId.Id == SummonId)
            {
                Pressed?.Invoke(this, EventArgs.Empty);
            }
            else if (hotKeyId.Id == QuickNoteId)
            {
                QuickNotePressed?.Invoke(this, EventArgs.Empty);
            }
        }

        return 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_summonRef != IntPtr.Zero)
        {
            Carbon.UnregisterEventHotKey(_summonRef);
        }

        if (_quickRef != IntPtr.Zero)
        {
            Carbon.UnregisterEventHotKey(_quickRef);
        }

        if (_handlerRef != IntPtr.Zero)
        {
            Carbon.RemoveEventHandler(_handlerRef);
        }
    }

    /// <summary>ANSI (US) virtual key codes from Carbon's HIToolbox/Events.h; layout-independent positions.</summary>
    internal static class KeyCodes
    {
        private static readonly Dictionary<HotkeyKey, uint> Map = new()
        {
            [HotkeyKey.A] = 0x00, [HotkeyKey.S] = 0x01, [HotkeyKey.D] = 0x02, [HotkeyKey.F] = 0x03, [HotkeyKey.H] = 0x04,
            [HotkeyKey.G] = 0x05, [HotkeyKey.Z] = 0x06, [HotkeyKey.X] = 0x07, [HotkeyKey.C] = 0x08, [HotkeyKey.V] = 0x09,
            [HotkeyKey.B] = 0x0B, [HotkeyKey.Q] = 0x0C, [HotkeyKey.W] = 0x0D, [HotkeyKey.E] = 0x0E, [HotkeyKey.R] = 0x0F,
            [HotkeyKey.Y] = 0x10, [HotkeyKey.T] = 0x11, [HotkeyKey.D1] = 0x12, [HotkeyKey.D2] = 0x13, [HotkeyKey.D3] = 0x14,
            [HotkeyKey.D4] = 0x15, [HotkeyKey.D6] = 0x16, [HotkeyKey.D5] = 0x17, [HotkeyKey.OemPlus] = 0x18, [HotkeyKey.D9] = 0x19,
            [HotkeyKey.D7] = 0x1A, [HotkeyKey.OemMinus] = 0x1B, [HotkeyKey.D8] = 0x1C, [HotkeyKey.D0] = 0x1D,
            [HotkeyKey.OemCloseBrackets] = 0x1E, [HotkeyKey.O] = 0x1F, [HotkeyKey.U] = 0x20, [HotkeyKey.OemOpenBrackets] = 0x21,
            [HotkeyKey.I] = 0x22, [HotkeyKey.P] = 0x23, [HotkeyKey.Return] = 0x24, [HotkeyKey.L] = 0x25, [HotkeyKey.J] = 0x26,
            [HotkeyKey.OemQuotes] = 0x27, [HotkeyKey.K] = 0x28, [HotkeyKey.OemSemicolon] = 0x29, [HotkeyKey.OemBackslash] = 0x2A,
            [HotkeyKey.OemComma] = 0x2B, [HotkeyKey.OemQuestion] = 0x2C, [HotkeyKey.N] = 0x2D, [HotkeyKey.M] = 0x2E,
            [HotkeyKey.OemPeriod] = 0x2F, [HotkeyKey.Tab] = 0x30, [HotkeyKey.Space] = 0x31, [HotkeyKey.Oem3] = 0x32,
            [HotkeyKey.Back] = 0x33, [HotkeyKey.Escape] = 0x35,
            [HotkeyKey.F5] = 0x60, [HotkeyKey.F6] = 0x61, [HotkeyKey.F7] = 0x62, [HotkeyKey.F3] = 0x63, [HotkeyKey.F8] = 0x64,
            [HotkeyKey.F9] = 0x65, [HotkeyKey.F11] = 0x67, [HotkeyKey.F13] = 0x69, [HotkeyKey.F14] = 0x6B, [HotkeyKey.F10] = 0x6D,
            [HotkeyKey.F12] = 0x6F, [HotkeyKey.F15] = 0x71, [HotkeyKey.Help] = 0x72, [HotkeyKey.Home] = 0x73, [HotkeyKey.PageUp] = 0x74,
            [HotkeyKey.Delete] = 0x75, [HotkeyKey.F4] = 0x76, [HotkeyKey.End] = 0x77, [HotkeyKey.F2] = 0x78, [HotkeyKey.PageDown] = 0x79,
            [HotkeyKey.F1] = 0x7A, [HotkeyKey.Left] = 0x7B, [HotkeyKey.Right] = 0x7C, [HotkeyKey.Down] = 0x7D, [HotkeyKey.Up] = 0x7E,
        };

        public static uint? ToVirtualKey(HotkeyKey key) => Map.TryGetValue(key, out uint code) ? code : null;
    }

    private static class Carbon
    {
        private const string Library = "/System/Library/Frameworks/Carbon.framework/Carbon";

        public const uint KEventClassKeyboard = 0x6B657962; // 'keyb'
        public const uint KEventHotKeyPressed = 5;
        public const uint KEventParamDirectObject = 0x2D2D2D2D; // '----'
        public const uint TypeEventHotKeyID = 0x686B6964; // 'hkid'
        public const uint Signature = 0x44594E54; // 'DYNT'

        public const uint CmdKey = 0x0100;
        public const uint ShiftKey = 0x0200;
        public const uint OptionKey = 0x0800;
        public const uint ControlKey = 0x1000;

        [StructLayout(LayoutKind.Sequential)]
        public struct EventTypeSpec
        {
            public uint EventClass;
            public uint EventKind;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EventHotKeyID
        {
            public uint Signature;
            public uint Id;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int EventHandlerProc(IntPtr nextHandler, IntPtr theEvent, IntPtr userData);

        [DllImport(Library)]
        public static extern IntPtr GetApplicationEventTarget();

        [DllImport(Library)]
        public static extern int InstallEventHandler(IntPtr target, EventHandlerProc handler, uint numTypes, ref EventTypeSpec list, IntPtr userData, out IntPtr handlerRef);

        [DllImport(Library)]
        public static extern int RemoveEventHandler(IntPtr handlerRef);

        [DllImport(Library)]
        public static extern int RegisterEventHotKey(uint hotKeyCode, uint modifiers, EventHotKeyID hotKeyId, IntPtr target, uint options, out IntPtr outRef);

        [DllImport(Library)]
        public static extern int UnregisterEventHotKey(IntPtr hotKeyRef);

        [DllImport(Library)]
        public static extern int GetEventParameter(IntPtr theEvent, uint name, uint desiredType, IntPtr outActualType, uint bufferSize, IntPtr outActualSize, ref EventHotKeyID data);
    }
}
