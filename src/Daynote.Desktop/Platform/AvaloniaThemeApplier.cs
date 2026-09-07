using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Styling;
using Daynote.App.Shell.Product;

namespace Daynote.Desktop.Platform;

/// <summary>Flips the application's theme variant; the palette dictionaries do the rest.</summary>
/// <remarks>
/// Except under the system high-contrast theme, where the palette is built from the OS colours and
/// merged over the variant dictionaries (docs/WINDOWS_ON_AVALONIA.md §4). The light/dark toggle stays
/// live in that state and stops changing the colours, which is correct: the user's contrast theme
/// outranks the app's preference.
/// </remarks>
public sealed class AvaloniaThemeApplier : IThemeApplier
{
    private readonly Application _application;
    private readonly bool _highContrast;
    private ResourceDictionary? _highContrastDictionary;

    public AvaloniaThemeApplier(Application application)
        : this(application, DetectHighContrast(application))
    {
    }

    /// <param name="highContrast">
    /// Whether the system high-contrast theme is on. Injectable so a test can exercise the merge
    /// without the machine being in high contrast.
    /// </param>
    public AvaloniaThemeApplier(Application application, bool highContrast)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _highContrast = highContrast;
    }

    public void Apply(bool dark)
    {
        _application.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        if (!_highContrast || !OperatingSystem.IsWindows())
        {
            return;
        }

        // Merged last so it overrides the variant dictionaries by key, and re-appended on every
        // apply: a theme dictionary swapped in later would otherwise take high contrast back off.
        _highContrastDictionary ??= WindowsHighContrastPalette.Build();
        _application.Resources.MergedDictionaries.Remove(_highContrastDictionary);
        _application.Resources.MergedDictionaries.Add(_highContrastDictionary);
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
