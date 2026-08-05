using System.Collections.ObjectModel;
using System.Windows;

namespace Daynote.App.Shell.Product;

/// <summary>
/// Production <see cref="IThemeApplier"/>. Merges the theme-independent product styles once and swaps the
/// Light/Dark brush dictionary in the application's merged dictionaries. High Contrast still wins because
/// the legacy HC aggregate (merged after, when active) overrides these brushes by key.
/// </summary>
public sealed class WpfProductThemeApplier : IThemeApplier
{
    private const string StylesUri = "/Daynote.App;component/Themes/Daynote.Product.Styles.xaml";
    private const string LightUri = "/Daynote.App;component/Themes/Daynote.Product.Light.xaml";
    private const string DarkUri = "/Daynote.App;component/Themes/Daynote.Product.Dark.xaml";

    private readonly System.Windows.Application _application;
    private ResourceDictionary? _stylesDictionary;
    private ResourceDictionary? _themeDictionary;

    public WpfProductThemeApplier(System.Windows.Application application) =>
        _application = application ?? throw new ArgumentNullException(nameof(application));

    public void Apply(bool dark)
    {
        Collection<ResourceDictionary> merged = _application.Resources.MergedDictionaries;
        _stylesDictionary ??= Add(merged, StylesUri);

        ResourceDictionary next = Load(dark ? DarkUri : LightUri);
        if (_themeDictionary is not null)
        {
            merged.Remove(_themeDictionary);
        }

        // Insert the theme brushes before the styles so styles resolve them, but they can still be
        // overridden by any dictionary merged later (e.g. High Contrast).
        int stylesIndex = merged.IndexOf(_stylesDictionary);
        merged.Insert(Math.Max(0, stylesIndex), next);
        _themeDictionary = next;
    }

    private static ResourceDictionary Add(Collection<ResourceDictionary> merged, string uri)
    {
        ResourceDictionary dictionary = Load(uri);
        merged.Add(dictionary);
        return dictionary;
    }

    private static ResourceDictionary Load(string uri) => new() { Source = new Uri(uri, UriKind.Relative) };
}
