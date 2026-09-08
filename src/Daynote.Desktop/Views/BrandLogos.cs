using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Daynote.App.Localization;

namespace Daynote.Desktop.Views;

/// <summary>
/// The titlebar wordmark: the brand lockup (calendar mark plus "데이노트" / "Daynote") in the active
/// language and theme.
/// </summary>
/// <remarks>
/// The Avalonia shell used to draw the favicon beside the literal text "Daynote", which was neither
/// the brand lockup nor localized. The WPF shell has shown the real wordmark all along through
/// <c>LanguageLogoConverter</c>; this is the same four assets, chosen the same way.
/// <para>
/// Both axes matter. The wordmark is drawn in dark ink, so the dark theme needs the light-ink
/// variant or it disappears into the ground; and the Korean lockup in an English build is simply the
/// wrong logo.
/// </para>
/// <para>
/// Decoded once each and cached. A titlebar image is re-read on every theme and language switch.
/// </para>
/// <para>
/// Decoded <em>to the size it is drawn at</em>, not at source size. The assets are 314px tall and the
/// title bar draws them at 26, and letting the renderer do that 12x reduction produced hard, unsmoothed
/// edges — the wordmark looked like stairs. The codec resamples properly on the way in.
/// <see cref="DecodeHeight"/> leaves headroom for a scaled display, since the decode happens once and
/// cannot know the DPI of the screen the window ends up on.
/// </para>
/// </remarks>
public static class BrandLogos
{
    private const string Root = "avares://Daynote.Desktop/Assets/";

    private static readonly Lazy<Bitmap> KoreanLight = Load("daynote-logo-trimmed.png");
    private static readonly Lazy<Bitmap> KoreanDark = Load("daynote-logo-dark-trimmed.png");
    private static readonly Lazy<Bitmap> EnglishLight = Load("daynote-logo-en-trimmed.png");
    private static readonly Lazy<Bitmap> EnglishDark = Load("daynote-logo-en-dark-trimmed.png");

    /// <summary>The wordmark for a language and theme.</summary>
    public static Bitmap For(AppLanguage language, bool dark) => language switch
    {
        AppLanguage.English => dark ? EnglishDark.Value : EnglishLight.Value,
        _ => dark ? KoreanDark.Value : KoreanLight.Value,
    };

    /// <summary>Four times the 26px the title bar draws, so a 300% display still has pixels to spare.</summary>
    private const int DecodeHeight = 104;

    private static Lazy<Bitmap> Load(string file) =>
        new(() => Bitmap.DecodeToHeight(AssetLoader.Open(new Uri(Root + file)), DecodeHeight));
}
