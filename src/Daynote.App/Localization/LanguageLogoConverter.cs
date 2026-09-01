using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Daynote.App.Localization;

/// <summary>
/// Maps the active <see cref="AppLanguage"/> to the titlebar wordmark image, so the English build shows
/// the English logo and Korean shows the Korean one. Bound to <c>LocalizationService.Instance.Language</c>,
/// which raises change notifications on a switch, so the logo swaps live. Pass ConverterParameter="Dark"
/// for the dark-theme variants (light ink so the wordmark stays readable on the dark ground).
/// Images are frozen and cached.
/// </summary>
public sealed class LanguageLogoConverter : IValueConverter
{
    private const string KoreanLogo = "pack://application:,,,/Daynote.App;component/Assets/Brand/daynote-logo-trimmed.png";
    private const string EnglishLogo = "pack://application:,,,/Daynote.App;component/Assets/Brand/daynote-logo-en-trimmed.png";
    private const string KoreanLogoDark = "pack://application:,,,/Daynote.App;component/Assets/Brand/daynote-logo-dark-trimmed.png";
    private const string EnglishLogoDark = "pack://application:,,,/Daynote.App;component/Assets/Brand/daynote-logo-en-dark-trimmed.png";

    private static readonly ImageSource Korean = Load(KoreanLogo);
    private static readonly ImageSource English = Load(EnglishLogo);
    private static readonly ImageSource KoreanDark = Load(KoreanLogoDark);
    private static readonly ImageSource EnglishDark = Load(EnglishLogoDark);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool dark = parameter is string s && string.Equals(s, "Dark", StringComparison.Ordinal);
        return value is AppLanguage.English
            ? (dark ? EnglishDark : English)
            : (dark ? KoreanDark : Korean);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static ImageSource Load(string uri)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(uri);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
