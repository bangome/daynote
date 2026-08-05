using System.Windows.Input;
using Daynote.App.Input;
using Daynote.App.Tests.Lifecycle;
using Daynote.Core.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Input;

[TestClass]
public sealed class ConfigurableShortcutsTests
{
    [TestMethod]
    public void Defaults_are_applied_before_loading()
    {
        var shortcuts = new ConfigurableShortcuts(new InMemorySettingsStore());
        Assert.AreEqual("Ctrl+N", shortcuts.Get(AppShortcuts.NewNote).ToDisplayString());
        Assert.AreEqual("Ctrl+Alt+←", shortcuts.Get(AppShortcuts.ToggleLeft).ToDisplayString());
    }

    [TestMethod]
    public async Task Set_persists_and_survives_a_reload()
    {
        var store = new InMemorySettingsStore();
        var shortcuts = new ConfigurableShortcuts(store);

        ShortcutSetResult result = await shortcuts.SetAsync(AppShortcuts.NewNote, new Hotkey(ModifierKeys.Control | ModifierKeys.Shift, Key.M));
        Assert.AreEqual(ShortcutSetResult.Ok, result);

        // A fresh instance over the same store loads the override.
        var reloaded = new ConfigurableShortcuts(store);
        await reloaded.LoadAsync();
        Assert.AreEqual("Ctrl+Shift+M", reloaded.Get(AppShortcuts.NewNote).ToDisplayString());
    }

    [TestMethod]
    public async Task Set_rejects_a_chord_already_used_by_another_action()
    {
        var shortcuts = new ConfigurableShortcuts(new InMemorySettingsStore());

        // Ctrl+T is go-today's default.
        ShortcutSetResult result = await shortcuts.SetAsync(AppShortcuts.NewNote, new Hotkey(ModifierKeys.Control, Key.T));

        Assert.AreEqual(ShortcutSetResult.Conflict, result);
        Assert.AreEqual("Ctrl+N", shortcuts.Get(AppShortcuts.NewNote).ToDisplayString(), "The binding is unchanged on conflict.");
    }

    [TestMethod]
    public async Task Set_rejects_an_invalid_modifier_less_chord()
    {
        var shortcuts = new ConfigurableShortcuts(new InMemorySettingsStore());
        Assert.AreEqual(ShortcutSetResult.Invalid, await shortcuts.SetAsync(AppShortcuts.NewNote, new Hotkey(ModifierKeys.None, Key.M)));
    }

    [TestMethod]
    public async Task Reset_restores_the_default()
    {
        var shortcuts = new ConfigurableShortcuts(new InMemorySettingsStore());
        await shortcuts.SetAsync(AppShortcuts.GoToday, new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.G));
        Assert.AreEqual("Ctrl+Alt+G", shortcuts.Get(AppShortcuts.GoToday).ToDisplayString());

        await shortcuts.ResetAsync(AppShortcuts.GoToday);
        Assert.AreEqual("Ctrl+T", shortcuts.Get(AppShortcuts.GoToday).ToDisplayString());
    }

    [TestMethod]
    public async Task Reassigning_a_freed_chord_is_allowed()
    {
        var shortcuts = new ConfigurableShortcuts(new InMemorySettingsStore());
        // Move go-today off Ctrl+T, then new-note can take it.
        await shortcuts.SetAsync(AppShortcuts.GoToday, new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.G));
        Assert.AreEqual(ShortcutSetResult.Ok, await shortcuts.SetAsync(AppShortcuts.NewNote, new Hotkey(ModifierKeys.Control, Key.T)));
    }
}
