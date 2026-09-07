using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// Every catalog key the Avalonia markup asks for by name exists in both catalogs.
/// </summary>
/// <remarks>
/// The markup reaches most copy through named properties on the strings proxy, which the compiler
/// checks. The rest goes through its indexer — <c>Strings[CloudSyncTitle]</c> — and that is a string,
/// so nothing checks it. A misspelt key compiles, raises no binding error, and renders whatever the
/// catalog returns for a key it does not have. Verified by trying it: a made-up key passed every
/// other test in this project.
/// <para>
/// The catalogs are read as text rather than through the API so the test says which file is missing
/// the key, and so it runs the same on macOS CI where only some of these projects are built.
/// </para>
/// </remarks>
[TestClass]
public sealed class LocalizationKeyTests
{
    [TestMethod]
    public void Indexed_keys_in_the_markup_exist_in_both_catalogs()
    {
        IReadOnlySet<string> korean = CatalogKeys("KoreanStrings.cs");
        IReadOnlySet<string> english = CatalogKeys("EnglishStrings.cs");
        Assert.IsGreaterThan(100, korean.Count, "the catalog scan found suspiciously few keys");

        List<string> missing = [];
        foreach ((string key, string file) in IndexedKeys())
        {
            if (!korean.Contains(key))
            {
                missing.Add($"{key} — not in KoreanStrings.cs (used by {file})");
            }

            if (!english.Contains(key))
            {
                missing.Add($"{key} — not in EnglishStrings.cs (used by {file})");
            }
        }

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            missing.Distinct().Order().ToArray(),
            string.Join(Environment.NewLine, missing.Distinct().Order()));
    }

    private static IEnumerable<(string Key, string File)> IndexedKeys()
    {
        foreach (string path in Directory.EnumerateFiles(DesktopRoot, "*.axaml", SearchOption.AllDirectories))
        {
            string markup = File.ReadAllText(path);
            foreach (Match match in Regex.Matches(markup, @"Strings\[([A-Za-z0-9_]+)\]"))
            {
                yield return (match.Groups[1].Value, Path.GetFileName(path));
            }
        }
    }

    private static IReadOnlySet<string> CatalogKeys(string fileName)
    {
        string path = Path.Combine(RepositoryRoot, "src", "Daynote.Presentation", "Localization", fileName);
        return Regex
            .Matches(File.ReadAllText(path), """\["([A-Za-z0-9_]+)"\]\s*=""")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string DesktopRoot { get; } = Path.Combine(RepositoryRoot, "src", "Daynote.Desktop");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DESIGN.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"No Daynote repository above '{AppContext.BaseDirectory}'.");
    }
}
