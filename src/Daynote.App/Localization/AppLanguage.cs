using System.Globalization;

namespace Daynote.App.Localization;

/// <summary>
/// The languages the shell ships copy for. Korean is the product's original language; English is
/// the fallback for every other system locale.
/// </summary>
public enum AppLanguage
{
    Korean = 0,
    English = 1,
}

/// <summary>
/// Conversions between <see cref="AppLanguage"/>, its persisted token, and the .NET culture used
/// for date and number formatting. The persisted token is a short BCP-47 tag so the settings row
/// stays readable in the database and survives a backup/restore round trip.
/// </summary>
public static class AppLanguages
{
    public const string KoreanTag = "ko";

    public const string EnglishTag = "en";

    /// <summary>The token written to the settings table.</summary>
    public static string ToTag(AppLanguage language) => language == AppLanguage.English ? EnglishTag : KoreanTag;

    /// <summary>
    /// Reads a persisted token. Anything unrecognized (including <see langword="null"/> from a
    /// profile that predates this setting) yields <see langword="null"/> so the caller can fall
    /// back to <see cref="FromSystem"/>.
    /// </summary>
    public static AppLanguage? FromTag(string? tag) => tag switch
    {
        KoreanTag => AppLanguage.Korean,
        EnglishTag => AppLanguage.English,
        _ => null,
    };

    /// <summary>
    /// The first-run default: follow the Windows display language, so a Korean desktop opens in
    /// Korean and everyone else opens in English.
    /// </summary>
    public static AppLanguage FromSystem() => FromCulture(CultureInfo.InstalledUICulture);

    /// <summary>Maps any culture onto a shipped language. Only Korean maps to Korean.</summary>
    public static AppLanguage FromCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return culture.TwoLetterISOLanguageName.Equals(KoreanTag, StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Korean
            : AppLanguage.English;
    }

    /// <summary>
    /// The culture used for date, weekday, and number formatting while this language is active.
    /// English uses the invariant-flavoured <c>en-US</c> rather than the machine's own English
    /// variant so screenshots and QA runs stay reproducible across regions.
    /// </summary>
    public static CultureInfo ToCulture(AppLanguage language) =>
        CultureInfo.GetCultureInfo(language == AppLanguage.English ? "en-US" : "ko-KR");
}
