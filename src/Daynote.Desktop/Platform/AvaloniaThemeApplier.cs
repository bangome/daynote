using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Styling;
using Daynote.App.Shell.Product;

namespace Daynote.Desktop.Platform;

/// <summary>Where the high-contrast colours come from on this platform.</summary>
public enum HighContrastSource
{
    /// <summary>
    /// The operating system's own theme, through <c>GetSysColor</c>. Windows: the user chose those
    /// colours, and the app defers to them.
    /// </summary>
    SystemColors,

    /// <summary>
    /// Derived from the app's palette roles, one set per variant. macOS: "Increase contrast" is a
    /// preference with no colour set behind it, so the app has to supply one.
    /// </summary>
    Derived,
}

/// <summary>Flips the application's theme variant; the palette dictionaries do the rest.</summary>
/// <remarks>
/// Except under the system high-contrast theme, where a high-contrast palette is merged over the
/// variant dictionaries (docs/WINDOWS_ON_AVALONIA.md §4). On Windows that palette is the OS theme's
/// colours, and the light/dark toggle stops changing the colours — correct, because the user's
/// contrast theme outranks the app's preference. On macOS there is no such theme to read, so the
/// palette is derived from the app's own roles and follows the variant: light stays light, at full
/// contrast.
/// </remarks>
public sealed class AvaloniaThemeApplier : IThemeApplier
{
    private readonly Application _application;
    private readonly bool _highContrast;
    private readonly HighContrastSource _source;
    private readonly Dictionary<bool, ResourceDictionary> _dictionaries = [];

    public AvaloniaThemeApplier(Application application)
        : this(application, DetectHighContrast(application))
    {
    }

    /// <param name="highContrast">
    /// Whether the system high-contrast preference is on. Injectable so a test can exercise the merge
    /// without the machine being in high contrast.
    /// </param>
    public AvaloniaThemeApplier(Application application, bool highContrast)
        : this(
            application,
            highContrast,
            OperatingSystem.IsWindows() ? HighContrastSource.SystemColors : HighContrastSource.Derived)
    {
    }

    /// <param name="source">
    /// Where the colours come from. Injectable so the derived path can be tested on a Windows build
    /// machine, where the default would read the system theme instead.
    /// </param>
    public AvaloniaThemeApplier(Application application, bool highContrast, HighContrastSource source)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _highContrast = highContrast;
        _source = source;
    }

    public void Apply(bool dark)
    {
        _application.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        if (!_highContrast)
        {
            return;
        }

        // Merged last so it overrides the variant dictionaries by key, and re-appended on every
        // apply: a theme dictionary swapped in later would otherwise take high contrast back off.
        // The derived palette differs per variant, so the other variant's copy comes out first.
        foreach (ResourceDictionary stale in _dictionaries.Values)
        {
            _application.Resources.MergedDictionaries.Remove(stale);
        }

        _application.Resources.MergedDictionaries.Add(DictionaryFor(dark));
    }

    private ResourceDictionary DictionaryFor(bool dark)
    {
        // The system palette does not depend on the variant, so both variants share one dictionary.
        bool key = _source == HighContrastSource.Derived && dark;
        if (!_dictionaries.TryGetValue(key, out ResourceDictionary? dictionary))
        {
            dictionary = _source == HighContrastSource.SystemColors && OperatingSystem.IsWindows()
                ? WindowsHighContrastPalette.Build()
                : DerivedHighContrastBrushes.Build(dark);
            _dictionaries[key] = dictionary;
        }

        return dictionary;
    }

    /// <summary>
    /// Avalonia reports a contrast preference rather than the colours behind it, which is enough to
    /// know whether to build the palette. Platform settings are unavailable early in startup and in
    /// some headless setups; no settings means no high contrast, which is the safe answer.
    /// </summary>
    private static bool DetectHighContrast(Application application)
    {
        try
        {
            IPlatformSettings? settings = application.PlatformSettings;
            return settings?.GetColorValues().ContrastPreference == ColorContrastPreference.High;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }
}
