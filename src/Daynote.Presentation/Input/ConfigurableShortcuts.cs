using Daynote.Core.Settings;

namespace Daynote.App.Input;

/// <summary>Outcome of assigning an in-app shortcut chord.</summary>
public enum ShortcutSetResult
{
    Ok,

    /// <summary>Another action already uses this chord.</summary>
    Conflict,

    /// <summary>Not a usable chord (no modifier, or a modifier-only combo).</summary>
    Invalid,
}

/// <summary>
/// Owns the current gesture for each configurable in-app shortcut, backed by <see cref="ISettingsStore"/>.
/// The window rebuilds its <c>KeyBinding</c>s from <see cref="Current"/> whenever <see cref="Changed"/>
/// fires; the settings UI edits through <see cref="SetAsync"/>/<see cref="ResetAsync"/>. Chords must be
/// valid (a modifier + a real key) and unique across actions.
/// </summary>
public sealed class ConfigurableShortcuts
{
    private readonly ISettingsStore _settings;
    private readonly Dictionary<string, Hotkey> _current = new(StringComparer.Ordinal);

    public ConfigurableShortcuts(ISettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        foreach (AppShortcutAction action in AppShortcuts.Actions)
        {
            _current[action.Id] = action.Default;
        }
    }

    /// <summary>Raised (on the calling thread) after the resolved gesture set changes.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<AppShortcutAction> Actions => AppShortcuts.Actions;

    /// <summary>The gesture currently in effect for each action id.</summary>
    public IReadOnlyDictionary<string, Hotkey> Current => _current;

    public Hotkey Get(string actionId) => _current[actionId];

    /// <summary>Loads persisted overrides (falling back to defaults) and notifies listeners.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        foreach (AppShortcutAction action in AppShortcuts.Actions)
        {
            string? stored = await _settings.GetAsync(ShortcutSettings.ActionKey(action.Id), cancellationToken).ConfigureAwait(false);
            _current[action.Id] = Hotkey.TryParse(stored, out Hotkey parsed) ? parsed : action.Default;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Assigns a new chord to an action; rejects invalid chords and cross-action duplicates.</summary>
    public async Task<ShortcutSetResult> SetAsync(string actionId, Hotkey hotkey, CancellationToken cancellationToken = default)
    {
        if (!hotkey.IsValid)
        {
            return ShortcutSetResult.Invalid;
        }

        if (_current.Any(pair => pair.Key != actionId && pair.Value == hotkey))
        {
            return ShortcutSetResult.Conflict;
        }

        _current[actionId] = hotkey;
        await _settings.SetAsync(ShortcutSettings.ActionKey(actionId), hotkey.ToDisplayString(), cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return ShortcutSetResult.Ok;
    }

    /// <summary>Restores an action's default chord.</summary>
    public async Task ResetAsync(string actionId, CancellationToken cancellationToken = default)
    {
        Hotkey fallback = AppShortcuts.DefaultFor(actionId);
        _current[actionId] = fallback;
        await _settings.SetAsync(ShortcutSettings.ActionKey(actionId), fallback.ToDisplayString(), cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
