using System.Text;
using System.Windows.Input;

namespace Daynote.App.Input;

/// <summary>
/// A keyboard chord: a set of modifiers plus one non-modifier key (e.g. Ctrl+Alt+D). Serializes to a
/// stable display string ("Ctrl+Alt+D") for persistence and the settings UI, and maps to the Win32
/// <c>RegisterHotKey</c> modifier/virtual-key pair for global registration. Pure logic — no OS calls —
/// so it is unit-testable without a message loop.
/// </summary>
public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    // Win32 fsModifiers (winuser.h): the low bits Windows expects from RegisterHotKey.
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    /// <summary>Suppresses auto-repeat while the chord is held (a single press = a single activation).</summary>
    public const uint ModNoRepeat = 0x4000;

    /// <summary>
    /// A registrable global hotkey needs at least one modifier and a real (non-modifier) key. A bare
    /// key or a modifier-only chord would either clash with normal typing or never fire.
    /// </summary>
    public bool IsValid => Modifiers != ModifierKeys.None && !IsModifierKey(Key) && Key != Key.None;

    /// <summary>The Win32 (fsModifiers, virtualKey) pair for <c>RegisterHotKey</c>, including MOD_NOREPEAT.</summary>
    public (uint FsModifiers, uint VirtualKey) ToWin32()
    {
        uint fs = ModNoRepeat;
        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            fs |= ModControl;
        }

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            fs |= ModAlt;
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            fs |= ModShift;
        }

        if (Modifiers.HasFlag(ModifierKeys.Windows))
        {
            fs |= ModWin;
        }

        return (fs, (uint)KeyInterop.VirtualKeyFromKey(Key));
    }

    /// <summary>Canonical, order-stable label ("Ctrl+Shift+D") used for display and persistence.</summary>
    public string ToDisplayString()
    {
        var builder = new StringBuilder();
        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            builder.Append("Ctrl+");
        }

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            builder.Append("Alt+");
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            builder.Append("Shift+");
        }

        if (Modifiers.HasFlag(ModifierKeys.Windows))
        {
            builder.Append("Win+");
        }

        builder.Append(KeyName(Key));
        return builder.ToString();
    }

    /// <summary>Parses a display string produced by <see cref="ToDisplayString"/> back into a hotkey.</summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ModifierKeys modifiers = ModifierKeys.None;
        Key key = Key.None;
        foreach (string raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "ALT":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "SHIFT":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    if (key != Key.None || !TryParseKey(raw, out key))
                    {
                        return false;
                    }

                    break;
            }
        }

        var candidate = new Hotkey(modifiers, key);
        if (!candidate.IsValid)
        {
            return false;
        }

        hotkey = candidate;
        return true;
    }

    /// <summary>
    /// Friendly, round-trippable names for keys whose enum name is unreadable (arrows, punctuation).
    /// Each symbol maps to exactly one <see cref="Key"/> so display and parse are inverses.
    /// </summary>
    private static readonly (Key Key, string Name)[] FriendlyKeys =
    [
        (Key.Left, "←"), (Key.Right, "→"), (Key.Up, "↑"), (Key.Down, "↓"),
        (Key.OemComma, ","), (Key.OemPeriod, "."), (Key.OemQuestion, "/"),
        (Key.OemMinus, "-"), (Key.OemPlus, "="), (Key.OemSemicolon, ";"),
        (Key.Oem3, "`"), (Key.Space, "Space"),
    ];

    private static bool TryParseKey(string token, out Key key)
    {
        // Accept the friendly digit form ("1".."9","0") we render, plus Enum names ("D", "F5", "Space").
        if (token.Length == 1 && token[0] >= '0' && token[0] <= '9')
        {
            key = Key.D0 + (token[0] - '0');
            return true;
        }

        foreach ((Key friendlyKey, string name) in FriendlyKeys)
        {
            if (string.Equals(token, name, StringComparison.OrdinalIgnoreCase))
            {
                key = friendlyKey;
                return true;
            }
        }

        return Enum.TryParse(token, ignoreCase: true, out key) && key != Key.None;
    }

    private static string KeyName(Key key)
    {
        foreach ((Key friendlyKey, string name) in FriendlyKeys)
        {
            if (key == friendlyKey)
            {
                return name;
            }
        }

        return key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "NumPad" + (key - Key.NumPad0),
            _ => key.ToString(),
        };
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin or
        Key.System;
}
