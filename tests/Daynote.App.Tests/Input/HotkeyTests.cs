using System.Windows.Input;
using Daynote.App.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Input;

[TestClass]
public sealed class HotkeyTests
{
    [TestMethod]
    public void Display_orders_modifiers_and_names_the_key()
    {
        Assert.AreEqual("Ctrl+Alt+D", new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.D).ToDisplayString());
        Assert.AreEqual("Ctrl+Shift+5", new Hotkey(ModifierKeys.Control | ModifierKeys.Shift, Key.D5).ToDisplayString());
        Assert.AreEqual("Win+Space", new Hotkey(ModifierKeys.Windows, Key.Space).ToDisplayString());
    }

    [TestMethod]
    public void Parse_round_trips_the_display_string()
    {
        foreach (Hotkey original in new[]
        {
            new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.D),
            new Hotkey(ModifierKeys.Control | ModifierKeys.Shift, Key.D5),
            new Hotkey(ModifierKeys.Control, Key.F5),
        })
        {
            Assert.IsTrue(Hotkey.TryParse(original.ToDisplayString(), out Hotkey parsed));
            Assert.AreEqual(original, parsed);
        }
    }

    [TestMethod]
    public void Display_and_parse_use_friendly_names_for_arrows_and_punctuation()
    {
        Assert.AreEqual("Ctrl+Alt+←", new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.Left).ToDisplayString());
        Assert.AreEqual("Ctrl+,", new Hotkey(ModifierKeys.Control, Key.OemComma).ToDisplayString());

        foreach (Hotkey original in new[]
        {
            new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.Right),
            new Hotkey(ModifierKeys.Control, Key.OemComma),
            new Hotkey(ModifierKeys.Alt, Key.Oem3),
        })
        {
            Assert.IsTrue(Hotkey.TryParse(original.ToDisplayString(), out Hotkey parsed));
            Assert.AreEqual(original, parsed);
        }
    }

    [TestMethod]
    public void Parse_is_case_insensitive_and_trims()
    {
        Assert.IsTrue(Hotkey.TryParse(" ctrl + ALT + d ", out Hotkey parsed));
        Assert.AreEqual(new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.D), parsed);
    }

    [TestMethod]
    public void Parse_rejects_invalid_and_modifier_only_strings()
    {
        Assert.IsFalse(Hotkey.TryParse(null, out _));
        Assert.IsFalse(Hotkey.TryParse("", out _));
        Assert.IsFalse(Hotkey.TryParse("D", out _), "No modifier.");
        Assert.IsFalse(Hotkey.TryParse("Ctrl+Alt", out _), "Modifier only.");
        Assert.IsFalse(Hotkey.TryParse("Ctrl+Nope", out _), "Unknown key.");
    }

    [TestMethod]
    public void IsValid_requires_a_modifier_and_a_non_modifier_key()
    {
        Assert.IsTrue(new Hotkey(ModifierKeys.Control, Key.D).IsValid);
        Assert.IsFalse(new Hotkey(ModifierKeys.None, Key.D).IsValid, "Bare key.");
        Assert.IsFalse(new Hotkey(ModifierKeys.Control, Key.None).IsValid, "No key.");
        Assert.IsFalse(new Hotkey(ModifierKeys.Control, Key.LeftShift).IsValid, "Modifier as the key.");
    }

    [TestMethod]
    public void ToWin32_maps_modifiers_with_no_repeat_and_the_virtual_key()
    {
        (uint fsModifiers, uint virtualKey) = new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.D).ToWin32();

        Assert.AreEqual(Hotkey.ModNoRepeat | 0x0002u | 0x0001u, fsModifiers, "MOD_NOREPEAT | MOD_CONTROL | MOD_ALT.");
        Assert.AreEqual((uint)KeyInterop.VirtualKeyFromKey(Key.D), virtualKey);
    }
}
