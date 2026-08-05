using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Daynote.App.Localization;

/// <summary>
/// Maps the active <see cref="AppLanguage"/> to the titlebar wordmark image, so the English build shows
/// the English logo and Korean shows the Korean one. Bound to <c>LocalizationService.Instance.Language</c>,
/// which raises change notifications on a switch, so the logo swaps live. Images are frozen and cached.
/// </summary>
public sealed class LanguageLogoConverter : IValueConverter
{
    private const string KoreanLogo = "pack://application:,,,/Daynote.App;component/Assets/Brand/daynote-logo-trimmed.png";
    private const string EnglishLogo = "pack://application:,,,/Daynote.App;component/Assets/Brand/daynote-logo-en-trimmed.png";

    private static readonly ImageSource Korean = Load(KoreanLogo);
    private static readonly ImageSource English = Load(EnglishLogo);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is AppLanguage.English ? English : Korean;

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
