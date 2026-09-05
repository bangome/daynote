using System.ComponentModel;
using System.Globalization;
using Daynote.Core.Domain.Notes;

namespace Daynote.App.Localization;

/// <summary>
/// A view model (or any other object) that shows text it derived from <see cref="AppStrings"/>
/// rather than binding a key directly. Implementations refresh that derived text when the user
/// switches languages; register with <see cref="LocalizationService.Observe"/>.
/// </summary>
public interface ILanguageAware
{
    void OnLanguageChanged();
}

/// <summary>
/// The active UI language and the string lookup behind <see cref="AppStrings"/> and
/// <see cref="TrExtension"/>.
/// </summary>
/// <remarks>
/// Switching languages is live: this type raises <see cref="INotifyPropertyChanged"/> for the
/// indexer (which re-evaluates every <c>{loc:Tr Key}</c> binding in loaded XAML), notifies
/// registered <see cref="ILanguageAware"/> objects so view models can re-raise their own derived
/// text, and swaps the thread cultures so dates and numbers follow suit.
///
/// It is a singleton rather than an injected service because XAML markup extensions have no access
/// to the DI container, and a second instance would leave half the UI bound to a stale catalog.
/// Observers are held weakly so transient view models do not leak through the static root.
/// </remarks>
public sealed class LocalizationService : INotifyPropertyChanged
{
    /// <summary>WPF re-evaluates every indexer binding on a change notification for this name.</summary>
    private const string IndexerName = "Item[]";

    private readonly Lock _gate = new();
    private readonly List<WeakReference<ILanguageAware>> _observers = [];

    private IReadOnlyDictionary<string, string> _values = KoreanStrings.Values;
    private AppLanguage _language = AppLanguage.Korean;

    private LocalizationService()
    {
    }

    public static LocalizationService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after <see cref="Language"/> changes, for consumers outside the binding system.</summary>
    public event EventHandler? LanguageChanged;

    public AppLanguage Language => _language;

    /// <summary>The culture applied to date and number formatting for <see cref="Language"/>.</summary>
    public CultureInfo Culture => AppLanguages.ToCulture(_language);

    /// <summary>
    /// The catalog lookup. A key with no entry falls back to Korean and then to the key itself, so
    /// a gap shows up as visibly wrong copy instead of crashing the shell. The catalog parity test
    /// is what actually keeps gaps from shipping.
    /// </summary>
    public string this[string key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);
            if (_values.TryGetValue(key, out string? value))
            {
                return value;
            }

            return KoreanStrings.Values.TryGetValue(key, out string? fallback) ? fallback : key;
        }
    }

    /// <summary>
    /// The keys a language's catalog actually defines, before any fallback. The indexer papers over
    /// a missing key on purpose, so this is the only way to see a genuine gap — which is exactly
    /// what the catalog parity test needs.
    /// </summary>
    public static IReadOnlyCollection<string> KeysFor(AppLanguage language) =>
        (language == AppLanguage.English ? EnglishStrings.Values : KoreanStrings.Values).Keys.ToArray();

    /// <summary>
    /// Switches the active language. Does nothing when the language is unchanged, so a redundant
    /// settings click does not churn every binding in the shell.
    /// </summary>
    public void SetLanguage(AppLanguage language)
    {
        if (_language == language)
        {
            return;
        }

        _language = language;
        _values = language == AppLanguage.English ? EnglishStrings.Values : KoreanStrings.Values;
        ApplyCulture();

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        NotifyObservers();
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Registers an object to be notified on every subsequent language change. The reference is
    /// weak, so callers never need to unregister; a closed window's view model simply stops being
    /// notified once it is collected. Registering the same object twice is a no-op.
    /// </summary>
    public void Observe(ILanguageAware observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (_gate)
        {
            PruneLocked();
            foreach (WeakReference<ILanguageAware> slot in _observers)
            {
                if (slot.TryGetTarget(out ILanguageAware? existing) && ReferenceEquals(existing, observer))
                {
                    return;
                }
            }

            _observers.Add(new WeakReference<ILanguageAware>(observer));
        }
    }

    /// <summary>
    /// Points the thread cultures at the active language so <c>string.Format</c> with
    /// <see cref="CultureInfo.CurrentCulture"/>, calendar weekday names, and date patterns all
    /// follow the UI. <see cref="CultureInfo.DefaultThreadCurrentCulture"/> covers background
    /// threads started afterwards; the explicit assignment covers the caller (the UI thread).
    /// </summary>
    internal void ApplyCulture()
    {
        CultureInfo culture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        // Core computes an untitled note's display title itself and cannot reference this layer, so
        // hand it the translated format. Called from here rather than SetLanguage so startup — which
        // may resolve to the language already active — sets it too.
        UntitledNote.Format = this[nameof(AppStrings.UntitledNoteFormat)];
    }

    private void NotifyObservers()
    {
        ILanguageAware[] targets;
        lock (_gate)
        {
            PruneLocked();
            targets = [.. _observers
                .Select(slot => slot.TryGetTarget(out ILanguageAware? target) ? target : null)
                .Where(target => target is not null)
                .Select(target => target!)];
        }

        // Outside the lock: an observer is free to register another observer while refreshing.
        foreach (ILanguageAware target in targets)
        {
            target.OnLanguageChanged();
        }
    }

    private void PruneLocked() =>
        _observers.RemoveAll(slot => !slot.TryGetTarget(out _));
}
