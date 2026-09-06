using System.Windows.Input;

namespace Daynote.App.Input;

/// <summary>Maps the framework-neutral <see cref="Hotkey"/> onto WPF and Win32 types.</summary>
public static class HotkeyInterop
{
    // Win32 fsModifiers (winuser.h): the low bits Windows expects from RegisterHotKey.
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    /// <summary>Suppresses auto-repeat while the chord is held (a single press = a single activation).</summary>
    public const uint ModNoRepeat = 0x4000;

    /// <summary>The Win32 (fsModifiers, virtualKey) pair for <c>RegisterHotKey</c>, including MOD_NOREPEAT.</summary>
    public static (uint FsModifiers, uint VirtualKey) ToWin32(Hotkey hotkey)
    {
        uint fs = ModNoRepeat;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            fs |= ModControl;
        }

        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            fs |= ModAlt;
        }

        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            fs |= ModShift;
        }

        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Meta))
        {
            fs |= ModWin;
        }

        return (fs, (uint)KeyInterop.VirtualKeyFromKey(ToKey(hotkey.Key)));
    }

    /// <summary>Same names, same values (see <see cref="HotkeyKey"/>), so the cast is exact.</summary>
    public static Key ToKey(HotkeyKey key) => (Key)key;

    public static ModifierKeys ToModifierKeys(HotkeyModifiers modifiers) => (ModifierKeys)modifiers;

    public static Hotkey FromWpf(ModifierKeys modifiers, Key key) => new((HotkeyModifiers)modifiers, (HotkeyKey)key);

    public static KeyGesture ToGesture(Hotkey hotkey) => new(ToKey(hotkey.Key), ToModifierKeys(hotkey.Modifiers));
}
