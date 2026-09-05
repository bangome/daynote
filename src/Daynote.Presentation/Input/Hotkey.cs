using System.Text;

namespace Daynote.App.Input;

/// <summary>
/// Modifier flags for a chord. Values match both WPF's <c>ModifierKeys</c> and Avalonia's
/// <c>KeyModifiers</c>, so each app casts. <see cref="Meta"/> is the Windows key on Windows and the
/// Command key on macOS; it persists as "Win" for compatibility with existing settings.
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Meta = 8,

    /// <summary>Alias kept for the Windows-flavoured call sites and their tests.</summary>
    Windows = Meta,
}

/// <summary>
/// A keyboard chord: a set of modifiers plus one non-modifier key (e.g. Ctrl+Alt+D). Serializes to a
/// stable display string ("Ctrl+Alt+D") for persistence and the settings UI. Pure logic — no OS or UI
/// framework calls — so it is unit-testable anywhere; the per-OS registration mapping (Win32 virtual
/// keys, Carbon key codes) lives in each app.
/// </summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, HotkeyKey Key)
{
    /// <summary>
    /// A registrable global hotkey needs at least one modifier and a real (non-modifier) key. A bare
    /// key or a modifier-only chord would either clash with normal typing or never fire.
    /// </summary>
    public bool IsValid => Modifiers != HotkeyModifiers.None && !IsModifierKey(Key) && Key != HotkeyKey.None;

    /// <summary>Canonical, order-stable label ("Ctrl+Shift+D") used for display and persistence.</summary>
    public string ToDisplayString()
    {
        var builder = new StringBuilder();
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            builder.Append("Ctrl+");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            builder.Append("Alt+");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            builder.Append("Shift+");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Meta))
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

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        HotkeyKey key = HotkeyKey.None;
        foreach (string raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "ALT":
                case "OPTION":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "SHIFT":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "WIN":
                case "WINDOWS":
                case "CMD":
                case "COMMAND":
                    modifiers |= HotkeyModifiers.Meta;
                    break;
                default:
                    if (key != HotkeyKey.None || !TryParseKey(raw, out key))
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
    /// Each symbol maps to exactly one <see cref="HotkeyKey"/> so display and parse are inverses.
    /// </summary>
    private static readonly (HotkeyKey Key, string Name)[] FriendlyKeys =
    [
        (HotkeyKey.Left, "←"), (HotkeyKey.Right, "→"), (HotkeyKey.Up, "↑"), (HotkeyKey.Down, "↓"),
        (HotkeyKey.OemComma, ","), (HotkeyKey.OemPeriod, "."), (HotkeyKey.OemQuestion, "/"),
        (HotkeyKey.OemMinus, "-"), (HotkeyKey.OemPlus, "="), (HotkeyKey.OemSemicolon, ";"),
        (HotkeyKey.Oem3, "`"), (HotkeyKey.Space, "Space"),
    ];

    private static bool TryParseKey(string token, out HotkeyKey key)
    {
        // Accept the friendly digit form ("1".."9","0") we render, plus Enum names ("D", "F5", "Space").
        if (token.Length == 1 && token[0] >= '0' && token[0] <= '9')
        {
            key = HotkeyKey.D0 + (token[0] - '0');
            return true;
        }

        foreach ((HotkeyKey friendlyKey, string name) in FriendlyKeys)
        {
            if (string.Equals(token, name, StringComparison.OrdinalIgnoreCase))
            {
                key = friendlyKey;
                return true;
            }
        }

        return Enum.TryParse(token, ignoreCase: true, out key) && key != HotkeyKey.None;
    }

    private static string KeyName(HotkeyKey key)
    {
        foreach ((HotkeyKey friendlyKey, string name) in FriendlyKeys)
        {
            if (key == friendlyKey)
            {
                return name;
            }
        }

        return key switch
        {
            >= HotkeyKey.D0 and <= HotkeyKey.D9 => ((char)('0' + (key - HotkeyKey.D0))).ToString(),
            >= HotkeyKey.NumPad0 and <= HotkeyKey.NumPad9 => "NumPad" + (key - HotkeyKey.NumPad0),
            _ => key.ToString(),
        };
    }

    private static bool IsModifierKey(HotkeyKey key) => key is
        HotkeyKey.LeftCtrl or HotkeyKey.RightCtrl or
        HotkeyKey.LeftAlt or HotkeyKey.RightAlt or
        HotkeyKey.LeftShift or HotkeyKey.RightShift or
        HotkeyKey.LWin or HotkeyKey.RWin or
        HotkeyKey.System;
}
