using System.Windows.Input;
using Daynote.App.Localization;

namespace Daynote.App.Input;

/// <summary>A user-configurable in-app shortcut: a stable id, a display label, and its default chord.</summary>
/// <remarks>
/// The label is stored as a catalog key, not resolved text. <see cref="AppShortcuts.Actions"/> is
/// built during static initialization — long before the user can pick a language — so resolving
/// eagerly would freeze these labels in whatever language happened to be active at startup.
/// </remarks>
public sealed record AppShortcutAction(string Id, string LabelKey, Hotkey Default)
{
    /// <summary>The action's label in the active UI language.</summary>
    public string Label => LocalizationService.Instance[LabelKey];
}

/// <summary>
/// The configurable in-app keyboard shortcuts (window <c>KeyBinding</c>s). Each action's id is stable
/// and used both as the persistence key suffix and to map to the shell command in ProductWindow. The
/// global summon hotkey and the Alt+` quick-note chord are separate (global <c>RegisterHotKey</c>) and
/// not part of this list.
/// </summary>
public static class AppShortcuts
{
    public const string NewNote = "new-note";
    public const string GoToday = "go-today";
    public const string Settings = "settings";
    public const string ToggleTheme = "toggle-theme";
    public const string ToggleLeft = "toggle-left";
    public const string ToggleRight = "toggle-right";
    public const string OpenSticky = "open-sticky";

    public static IReadOnlyList<AppShortcutAction> Actions { get; } =
    [
        new(NewNote, nameof(AppStrings.ShortcutNewNote), new Hotkey(ModifierKeys.Control, Key.N)),
        new(GoToday, nameof(AppStrings.ShortcutGoToday), new Hotkey(ModifierKeys.Control, Key.T)),
        new(Settings, nameof(AppStrings.ShortcutSettings), new Hotkey(ModifierKeys.Control, Key.OemComma)),
        new(ToggleTheme, nameof(AppStrings.ShortcutToggleTheme), new Hotkey(ModifierKeys.Control | ModifierKeys.Shift, Key.L)),
        new(ToggleLeft, nameof(AppStrings.ShortcutToggleLeft), new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.Left)),
        new(ToggleRight, nameof(AppStrings.ShortcutToggleRight), new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.Right)),
        new(OpenSticky, nameof(AppStrings.ShortcutOpenSticky), new Hotkey(ModifierKeys.Control | ModifierKeys.Shift, Key.S)),
    ];

    public static Hotkey DefaultFor(string id) => Actions.First(action => action.Id == id).Default;
}
