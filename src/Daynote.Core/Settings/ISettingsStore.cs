namespace Daynote.Core.Settings;

/// <summary>
/// Typed persistence over the <c>settings</c> key/value table. Values are opaque strings; typed
/// helpers layer bool/enum semantics on top without introducing a second storage shape.
/// </summary>
public interface ISettingsStore
{
    ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    ValueTask SetAsync(string key, string value, CancellationToken cancellationToken = default);

    ValueTask<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken = default);

    ValueTask SetBoolAsync(string key, bool value, CancellationToken cancellationToken = default);
}

/// <summary>
/// Well-known shortcut setting keys, namespaced under <c>shortcuts.</c> so they never collide with the
/// lifecycle or note-title markers that share the settings table.
/// </summary>
public static class ShortcutSettings
{
    /// <summary>The global summon hotkey, stored as its display string (e.g. <c>Ctrl+Alt+D</c>).</summary>
    public const string SummonHotkeyKey = "shortcuts.summon";

    /// <summary>Default summon hotkey applied when nothing is persisted yet.</summary>
    public const string SummonHotkeyDefault = "Ctrl+Alt+D";

    /// <summary>Per-action in-app shortcut override key (value = the chord's display string).</summary>
    public static string ActionKey(string actionId) => $"shortcuts.action.{actionId}";
}

/// <summary>Well-known presentation setting keys, namespaced under <c>ui.</c>.</summary>
public static class UiSettings
{
    /// <summary>
    /// The chosen UI language as a short BCP-47 tag (<c>ko</c> or <c>en</c>). Absent means the user
    /// has never chosen one, in which case the shell follows the Windows display language.
    /// </summary>
    public const string LanguageKey = "ui.language";
}

/// <summary>Well-known onboarding setting keys.</summary>
public static class OnboardingSettings
{
    /// <summary>Set true once the first-run tutorial has been finished or skipped.</summary>
    public const string CompletedKey = "onboarding.completed";

    /// <summary>Set true once the first-run sample note has been seeded (so it is only added once).</summary>
    public const string SampleSeededKey = "onboarding.sample-seeded";

    /// <summary>The seeded sample note's id/date, and its last-written body — used to re-localize it on a
    /// language switch, but only while the user has not edited it (current body still equals this).</summary>
    public const string SampleNoteIdKey = "onboarding.sample-note-id";

    public const string SampleNoteDateKey = "onboarding.sample-note-date";

    public const string SampleNoteBodyKey = "onboarding.sample-note-body";
}
