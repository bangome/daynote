using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Infrastructure.Portable.Tests.Design;

/// <summary>
/// The WPF and Avalonia apps paint the same product, so they carry the same palette.
/// </summary>
/// <remarks>
/// This is a text comparison of two resource dictionaries, which is why it can live in a project that
/// otherwise tests infrastructure: it needs neither UI framework, and this is the one test project
/// both CI legs run — Windows builds the whole solution, macOS runs Core and this. A design test that
/// only ran on one of them would miss drift introduced from the other side, which is exactly how the
/// palettes came apart in the first place: <c>OkSoft</c>, <c>WarnSoft</c> and the four Google brand
/// colours were added to WPF for the account window and the Avalonia side never heard about it.
/// <para>
/// Colours are compared as well as keys. Two apps agreeing on the name of a brush but not its value
/// is worse than not sharing it at all, because it looks correct in a diff.
/// </para>
/// </remarks>
[TestClass]
public sealed class PaletteParityTests
{
    /// <summary>Brushes whose value is deliberately allowed to differ, with the reason.</summary>
    private static readonly Dictionary<string, string> AllowedToDiffer = new(StringComparer.Ordinal);

    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public void The_two_apps_carry_the_same_palette(string variant)
    {
        IReadOnlyDictionary<string, string> wpf = WpfPalette(variant);
        IReadOnlyDictionary<string, string> avalonia = AvaloniaPalette(variant);

        string[] onlyWpf = [.. wpf.Keys.Except(avalonia.Keys).Order()];
        string[] onlyAvalonia = [.. avalonia.Keys.Except(wpf.Keys).Order()];

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            onlyWpf,
            $"The {variant} palette has brushes the Avalonia app does not: {string.Join(", ", onlyWpf)}. "
                + "Add them to src/Daynote.Desktop/Themes/Daynote.Product.axaml.");
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            onlyAvalonia,
            $"The Avalonia {variant} palette has brushes the WPF app does not: {string.Join(", ", onlyAvalonia)}. "
                + "Add them to src/Daynote.App/Themes/Daynote.Product.*.xaml.");

        string[] differing =
        [
            .. wpf.Keys
                .Where(key => !AllowedToDiffer.ContainsKey(key))
                .Where(key => !string.Equals(wpf[key], avalonia[key], StringComparison.OrdinalIgnoreCase))
                .Order()
                .Select(key => $"{key} (WPF {wpf[key]}, Avalonia {avalonia[key]})"),
        ];

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            differing,
            $"Same name, different colour in {variant}: {string.Join("; ", differing)}");
    }

    [TestMethod]
    public void Neither_palette_defines_a_brush_in_only_one_variant()
    {
        // A brush present in light but not dark reads as black-on-black the first time someone
        // switches theme, and nothing before that moment says so.
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            WpfPalette("Light").Keys.ToHashSet()
                .SymmetricExcept(WpfPalette("Dark").Keys),
            "The WPF light and dark palettes disagree about which brushes exist.");
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            AvaloniaPalette("Light").Keys.ToHashSet()
                .SymmetricExcept(AvaloniaPalette("Dark").Keys),
            "The Avalonia light and dark theme dictionaries disagree about which brushes exist.");
    }

    private static IReadOnlyDictionary<string, string> WpfPalette(string variant) =>
        Brushes(File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Daynote.App", "Themes", $"Daynote.Product.{variant}.xaml")));

    /// <summary>
    /// The Avalonia palette keeps both variants in one file, under <c>ThemeDictionaries</c>, so the
    /// requested variant's block is cut out before parsing.
    /// </summary>
    private static IReadOnlyDictionary<string, string> AvaloniaPalette(string variant)
    {
        string all = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Daynote.Desktop", "Themes", "Daynote.Product.axaml"));

        int start = all.IndexOf($"<ResourceDictionary x:Key=\"{variant}\">", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start, $"No {variant} theme dictionary in Daynote.Product.axaml.");
        int end = all.IndexOf("</ResourceDictionary>", start, StringComparison.Ordinal);
        return Brushes(all[start..end]);
    }

    private const string BrushPattern =
        "<SolidColorBrush x:Key=\"(?<key>Daynote\\.Product\\.Brush\\.[^\"]+)\" Color=\"(?<color>[^\"]+)\"";

    private static IReadOnlyDictionary<string, string> Brushes(string markup) => Regex
        .Matches(markup, BrushPattern)
        .ToDictionary(match => match.Groups["key"].Value, match => match.Groups["color"].Value, StringComparer.Ordinal);

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

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

        throw new DirectoryNotFoundException(
            $"Could not find the Daynote repository above '{AppContext.BaseDirectory}'.");
    }
}

file static class SetExtensions
{
    /// <summary>The keys in one set but not the other, either way round, sorted.</summary>
    internal static string[] SymmetricExcept(this HashSet<string> left, IEnumerable<string> right)
    {
        var other = right.ToHashSet(StringComparer.Ordinal);
        return [.. left.Except(other).Concat(other.Except(left)).Order()];
    }
}
