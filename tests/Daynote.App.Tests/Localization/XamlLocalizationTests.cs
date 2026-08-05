using System.Text.RegularExpressions;
using Daynote.App.Localization;

namespace Daynote.App.Tests.Localization;

/// <summary>
/// Source-level guards on how XAML reaches the string catalog.
/// </summary>
/// <remarks>
/// Both failures these catch are silent at runtime: a mistyped <c>{loc:Tr}</c> key renders the key
/// name into the UI (the service falls back rather than throwing), and an <c>{x:Static}</c>
/// reference resolves once at load and then never follows a language switch. Neither shows up as an
/// exception, so only a scan of the markup finds them.
/// </remarks>
[TestClass]
public sealed partial class XamlLocalizationTests
{
    [GeneratedRegex(@"\{loc:Tr\s+([A-Za-z0-9_]+)\s*\}")]
    private static partial Regex TrUsage();

    [GeneratedRegex(@"\{x:Static\s+loc:AppStrings\.")]
    private static partial Regex StaticAppStringsUsage();

    private static IEnumerable<(string Path, string Line, int Number)> AppXaml() =>
        Directory.EnumerateFiles(TestPaths.AppRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (Path: path, Line: line, Number: index + 1)));

    [TestMethod]
    public void Every_Tr_key_in_markup_exists_in_the_catalog()
    {
        var known = LocalizationService.KeysFor(AppLanguage.Korean).ToHashSet(StringComparer.Ordinal);

        string[] unknown = [.. AppXaml()
            .SelectMany(item => TrUsage().Matches(item.Line)
                .Select(match => (item.Path, item.Number, Key: match.Groups[1].Value)))
            .Where(usage => !known.Contains(usage.Key))
            .Select(usage => $"{Path.GetRelativePath(TestPaths.RepositoryRoot, usage.Path)}:{usage.Number} '{usage.Key}'")
            .Distinct(StringComparer.Ordinal)];

        Assert.AreEqual(0, unknown.Length, $"Unknown Tr keys: {string.Join(", ", unknown)}");
    }

    [TestMethod]
    public void No_markup_still_binds_strings_through_x_Static()
    {
        string[] stale = [.. AppXaml()
            .Where(item => StaticAppStringsUsage().IsMatch(item.Line))
            .Select(item => $"{Path.GetRelativePath(TestPaths.RepositoryRoot, item.Path)}:{item.Number}")];

        Assert.AreEqual(
            0,
            stale.Length,
            "x:Static resolves once at load, so these would not follow a language switch. " +
            $"Use {{loc:Tr Key}} instead: {string.Join(", ", stale)}");
    }

    [TestMethod]
    public void Markup_uses_the_catalog_rather_than_inline_korean()
    {
        // Korean literals in markup are strings no English user can ever see translated. Comments are
        // exempt: several explain the design in Korean and are never rendered.
        string[] literals = [.. AppXaml()
            .Where(item => !item.Line.TrimStart().StartsWith("<!--", StringComparison.Ordinal))
            .Where(item => item.Line.Any(character => character is >= '가' and <= '힣'))
            .Select(item => $"{Path.GetRelativePath(TestPaths.RepositoryRoot, item.Path)}:{item.Number}")];

        Assert.AreEqual(0, literals.Length, $"Untranslatable Korean in markup: {string.Join(", ", literals)}");
    }
}
