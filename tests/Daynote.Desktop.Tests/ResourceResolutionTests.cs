using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// Every resource key the Avalonia markup asks for resolves, in both theme variants.
/// </summary>
/// <remarks>
/// The WPF app has had this rule for a long time (<c>DesignResourceTests</c>); the Avalonia app has
/// had none, which is why sixteen brushes could go missing from its palette without anything
/// noticing. A key that does not resolve is not a crash — Avalonia falls back and paints something
/// else — so this is the only kind of test that catches it.
/// <para>
/// Both variants are checked because a brush defined only under Light is invisible until someone
/// switches to Dark, and the switch is one click away in the app.
/// </para>
/// </remarks>
[TestClass]
public sealed class ResourceResolutionTests
{
    /// <summary>Keys that come from the Fluent theme rather than Daynote's own dictionaries.</summary>
    private static readonly string[] ForeignPrefixes = ["System", "Theme", "Control"];

    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public void Every_key_the_markup_asks_for_resolves(string variantName)
    {
        ThemeVariant variant = variantName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        IReadOnlyList<(string Key, string Where)> requested = RequestedKeys();
        Assert.IsGreaterThan(20, requested.Count, "the scan found suspiciously few resource references");

        List<string> unresolved = [];
        HeadlessAppFixture.OnUiThread(() =>
        {
            Application application = Application.Current
                ?? throw new InvalidOperationException("Headless Avalonia did not create an Application.");

            application.RequestedThemeVariant = variant;

            foreach ((string key, string where) in requested)
            {
                if (!application.TryFindResource(key, variant, out object? value) || value is null)
                {
                    unresolved.Add($"{key} ({where})");
                }
            }
        });

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            unresolved.Distinct().Order().ToArray(),
            $"Unresolved in the {variantName} variant:{Environment.NewLine}"
                + string.Join(Environment.NewLine, unresolved.Distinct().Order()));
    }

    /// <summary>Every <c>{DynamicResource X}</c> and <c>{StaticResource X}</c> in the app's markup.</summary>
    private static IReadOnlyList<(string Key, string Where)> RequestedKeys()
    {
        List<(string, string)> found = [];
        foreach (string path in Directory.EnumerateFiles(DesktopRoot, "*.axaml", SearchOption.AllDirectories))
        {
            string markup = File.ReadAllText(path);

            // A file may define its own keys — a DataTemplate declared where it is used, say. Those
            // resolve from the control, never from the application, so asking the app about them
            // would fail for the wrong reason.
            HashSet<string> local = [.. Regex.Matches(markup, @"x:Key=""([^""]+)""").Select(m => m.Groups[1].Value)];

            foreach (Match match in Regex.Matches(markup, @"\{(?:Dynamic|Static)Resource\s+([^}\s]+)\s*\}"))
            {
                string key = match.Groups[1].Value;
                if (local.Contains(key))
                {
                    continue;
                }

                if (!ForeignPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    found.Add((key, Path.GetFileName(path)));
                }
            }
        }

        return found;
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
