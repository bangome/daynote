using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Daynote.App.Localization;

namespace Daynote.App.Tests.Localization;

/// <summary>
/// Guards the invariant the whole localization scheme rests on: <see cref="AppStrings"/>, the Korean
/// catalog, and the English catalog describe exactly the same set of keys, with matching format
/// placeholders. A gap here would surface at runtime as a key name rendered into the UI, which the
/// service's fallback deliberately makes survivable — these tests are what keep it from shipping.
/// </summary>
// Switching languages mutates process-wide state (the active catalog, the thread cultures, and
// the untitled-note format), so these classes must not run alongside tests that read it.
[DoNotParallelize]
[TestClass]
public sealed partial class StringCatalogTests
{
    [GeneratedRegex(@"\{(\d+)")]
    private static partial Regex PlaceholderIndex();

    /// <summary>Every public string member of <see cref="AppStrings"/>, by name.</summary>
    private static IReadOnlyList<string> AccessorNames { get; } =
        [.. typeof(AppStrings)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)];

    private static IReadOnlyDictionary<string, string> CatalogFor(AppLanguage language)
    {
        // The catalogs are internal, so read them the same way the service does: switch to the
        // language and resolve through the public indexer.
        AppLanguage original = LocalizationService.Instance.Language;
        try
        {
            LocalizationService.Instance.SetLanguage(language);
            return AccessorNames.ToDictionary(
                name => name,
                name => LocalizationService.Instance[name],
                StringComparer.Ordinal);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(original);
        }
    }

    [TestMethod]
    public void Both_catalogs_define_exactly_the_accessor_set()
    {
        // Checked against the catalogs directly, not through the indexer: the indexer falls back to
        // the key name, and several English entries legitimately equal their key ("Bold", "Copy"),
        // so a fallback-based check cannot tell a translation from a hole.
        foreach (AppLanguage language in Enum.GetValues<AppLanguage>())
        {
            var keys = LocalizationService.KeysFor(language).ToHashSet(StringComparer.Ordinal);

            string[] missing = [.. AccessorNames.Where(name => !keys.Contains(name))];
            string[] orphaned = [.. keys.Where(key => !AccessorNames.Contains(key)).OrderBy(key => key, StringComparer.Ordinal)];

            Assert.AreEqual(0, missing.Length, $"{language} is missing: {string.Join(", ", missing)}");
            Assert.AreEqual(
                0,
                orphaned.Length,
                $"{language} defines keys with no AppStrings accessor: {string.Join(", ", orphaned)}");
        }
    }

    [TestMethod]
    public void No_entry_is_blank()
    {
        foreach (AppLanguage language in Enum.GetValues<AppLanguage>())
        {
            IReadOnlyDictionary<string, string> catalog = CatalogFor(language);
            string[] blank = [.. catalog.Where(pair => pair.Value.Length == 0).Select(pair => pair.Key)];

            Assert.AreEqual(0, blank.Length, $"{language} has blank copy for: {string.Join(", ", blank)}");
        }
    }

    [TestMethod]
    public void Korean_and_English_differ_wherever_the_copy_is_prose()
    {
        IReadOnlyDictionary<string, string> korean = CatalogFor(AppLanguage.Korean);
        IReadOnlyDictionary<string, string> english = CatalogFor(AppLanguage.English);

        // A handful of entries are deliberately identical across languages: proper nouns, an email
        // address, the language endonyms, and pure-punctuation format strings.
        var sharedByDesign = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(AppStrings.AuthorName),
            nameof(AppStrings.AuthorEmail),
            nameof(AppStrings.TrayAppName),
            nameof(AppStrings.LanguageKorean),
            nameof(AppStrings.LanguageEnglish),
            nameof(AppStrings.TutorialProgressFormat),
            // "Pro" is the plan's name, the avatar tooltip is pure separators, and the privacy page
            // is one document at one path in both languages.
            nameof(AppStrings.AccountPlanPro),
            nameof(AppStrings.AccountAvatarTooltipFormat),
            nameof(AppStrings.AccountPrivacyUrl),
        };

        string[] untranslated = [.. korean
            .Where(pair => !sharedByDesign.Contains(pair.Key))
            .Where(pair => string.Equals(pair.Value, english[pair.Key], StringComparison.Ordinal))
            .Select(pair => pair.Key)];

        Assert.AreEqual(
            0,
            untranslated.Length,
            $"English copy is identical to Korean for: {string.Join(", ", untranslated)}");
    }

    [TestMethod]
    public void Format_placeholders_match_across_languages()
    {
        IReadOnlyDictionary<string, string> korean = CatalogFor(AppLanguage.Korean);
        IReadOnlyDictionary<string, string> english = CatalogFor(AppLanguage.English);

        foreach (KeyValuePair<string, string> pair in korean)
        {
            // Order is irrelevant — a translation may reorder {0} and {1} — but the SET of indexes
            // must match, or string.Format throws or silently drops a value.
            var koreanIndexes = PlaceholderIndex().Matches(pair.Value)
                .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            var englishIndexes = PlaceholderIndex().Matches(english[pair.Key])
                .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

            CollectionAssert.AreEquivalent(
                koreanIndexes.ToArray(),
                englishIndexes.ToArray(),
                $"Placeholder mismatch for '{pair.Key}': " +
                $"Korean '{pair.Value}' vs English '{english[pair.Key]}'.");
        }
    }

    [TestMethod]
    public void Date_patterns_render_without_leaking_the_other_language()
    {
        var date = new DateOnly(2026, 7, 27);

        LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
        string koreanLong = date.ToString(AppStrings.DateFormatLong, LocalizationService.Instance.Culture);
        StringAssert.Contains(koreanLong, "2026", StringComparison.Ordinal);
        StringAssert.Contains(koreanLong, "년", StringComparison.Ordinal);

        LocalizationService.Instance.SetLanguage(AppLanguage.English);
        string englishLong = date.ToString(AppStrings.DateFormatLong, LocalizationService.Instance.Culture);
        StringAssert.Contains(englishLong, "2026", StringComparison.Ordinal);
        StringAssert.Contains(englishLong, "July", StringComparison.Ordinal);
        Assert.IsFalse(
            englishLong.Contains('년', StringComparison.Ordinal),
            $"The English long-date pattern still carries Korean literals: '{englishLong}'.");

        LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
    }
}
