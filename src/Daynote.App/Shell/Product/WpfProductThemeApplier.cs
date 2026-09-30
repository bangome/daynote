using System.Collections.ObjectModel;
using System.Windows;

namespace Daynote.App.Shell.Product;

/// <summary>
/// Production <see cref="IThemeApplier"/>. Merges the theme-independent product styles once and swaps the
/// Light/Dark brush dictionary in the application's merged dictionaries.
/// </summary>
/// <remarks>
/// This used to claim that High Contrast still wins, because the HC aggregate is merged after these
/// brushes and would override them by key. Measured 2026-09-07: it overrides nothing here. The HC
/// dictionary defines 28 <c>Daynote.Brush.*</c> keys from the pre-v3 foundation layer; the product
/// palette defines 43 <c>Daynote.Product.Brush.*</c> keys, and the overlap between the two sets is
/// empty. Every v3 surface reads only the product keys, so turning High Contrast on changes nothing
/// the user can see. Giving the app a working high-contrast mode means a high-contrast *product*
/// palette, which neither shell has.
/// </remarks>
public sealed class WpfProductThemeApplier : IThemeApplier
{
    private const string StylesUri = "/Daynote.App;component/Themes/Daynote.Product.Styles.xaml";
    private const string LightUri = "/Daynote.App;component/Themes/Daynote.Product.Light.xaml";
    private const string DarkUri = "/Daynote.App;component/Themes/Daynote.Product.Dark.xaml";

    /// <summary>
    /// The design-B layer: the theme-independent half, then the light or dark half.
    /// </summary>
    /// <remarks>
    /// A layer of its own rather than edits to the product palette, mirroring the Avalonia shell's
    /// Daynote.Desk.axaml. The product dictionaries are held to the Avalonia shared palette by
    /// PaletteParityTests, and that palette is also the phone's base — so the desktop design cannot
    /// be written into them without dragging the phone along.
    /// </remarks>
    private const string DeskUri = "/Daynote.App;component/Themes/Daynote.Desk.xaml";
    private const string DeskLightUri = "/Daynote.App;component/Themes/Daynote.Desk.Light.xaml";
    private const string DeskDarkUri = "/Daynote.App;component/Themes/Daynote.Desk.Dark.xaml";

    /// <summary>
    /// The design's control styles, merged last of the app's own dictionaries because every one of
    /// them reads a Daynote.Desk.* brush or geometry that the three above declare.
    /// </summary>
    private const string DeskStylesUri = "/Daynote.App;component/Themes/Daynote.Desk.Styles.xaml";

    /// <summary>The per-place half of the same styles; it builds on the file above, so it follows it.</summary>
    private const string DeskShellStylesUri = "/Daynote.App;component/Themes/Daynote.Desk.Styles.Shell.xaml";

    /// <summary>The settings dialog's controls, which build on the bare button in the two above.</summary>
    private const string DeskSettingsStylesUri = "/Daynote.App;component/Themes/Daynote.Desk.Styles.Settings.xaml";

    private readonly System.Windows.Application _application;
    private readonly bool _highContrast;
    private ResourceDictionary? _stylesDictionary;
    private ResourceDictionary? _deskDictionary;
    private ResourceDictionary? _deskStylesDictionary;
    private ResourceDictionary? _deskShellStylesDictionary;
    private ResourceDictionary? _deskSettingsStylesDictionary;
    private ResourceDictionary? _themeDictionary;
    private ResourceDictionary? _deskThemeDictionary;
    private ResourceDictionary? _highContrastDictionary;

    public WpfProductThemeApplier(System.Windows.Application application)
        : this(application, System.Windows.SystemParameters.HighContrast)
    {
    }

    /// <param name="highContrast">
    /// Whether the system high-contrast theme is on. Injectable so a test can exercise the merge
    /// order without the machine being in high contrast.
    /// </param>
    public WpfProductThemeApplier(System.Windows.Application application, bool highContrast)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _highContrast = highContrast;
    }

    public void Apply(bool dark)
    {
        Collection<ResourceDictionary> merged = _application.Resources.MergedDictionaries;
        _stylesDictionary ??= Add(merged, StylesUri);
        _deskDictionary ??= Add(merged, DeskUri);
        _deskStylesDictionary ??= Add(merged, DeskStylesUri);
        _deskShellStylesDictionary ??= Add(merged, DeskShellStylesUri);
        _deskSettingsStylesDictionary ??= Add(merged, DeskSettingsStylesUri);

        ResourceDictionary next = Load(dark ? DarkUri : LightUri);
        ResourceDictionary nextDesk = Load(dark ? DeskDarkUri : DeskLightUri);
        if (_themeDictionary is not null)
        {
            merged.Remove(_themeDictionary);
        }

        if (_deskThemeDictionary is not null)
        {
            merged.Remove(_deskThemeDictionary);
        }

        // Insert the theme brushes before the styles so the styles resolve them. A dictionary merged
        // later can still override them by key — which is what the High Contrast aggregate was meant
        // to do and does not, per the remarks above.
        //
        // The desk layer goes immediately after the product palette and still before the styles:
        // after, so it wins on the shared keys it redefines; before, so a style setter written
        // against a Daynote.Desk.* key still resolves.
        int stylesIndex = merged.IndexOf(_stylesDictionary);
        merged.Insert(Math.Max(0, stylesIndex), next);
        merged.Insert(Math.Max(0, merged.IndexOf(next) + 1), nextDesk);
        _themeDictionary = next;
        _deskThemeDictionary = nextDesk;

        if (!_highContrast)
        {
            return;
        }

        // Last, so it overrides the light or dark palette by key. Rebuilt on every apply and moved
        // to the end, because inserting the theme dictionary above would otherwise leave it ahead of
        // this one and the theme toggle would silently take high contrast back off.
        if (_highContrastDictionary is not null)
        {
            merged.Remove(_highContrastDictionary);
        }

        _highContrastDictionary = WpfHighContrastPalette.Build();
        merged.Add(_highContrastDictionary);
    }

    private static ResourceDictionary Add(Collection<ResourceDictionary> merged, string uri)
    {
        ResourceDictionary dictionary = Load(uri);
        merged.Add(dictionary);
        return dictionary;
    }

    private static ResourceDictionary Load(string uri) => new() { Source = new Uri(uri, UriKind.Relative) };
}
