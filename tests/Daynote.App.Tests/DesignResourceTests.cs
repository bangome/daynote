using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests;

[TestClass]
public sealed partial class DesignResourceTests
{
    private static readonly string[] RequiredIconKeys =
    [
        "Daynote.Icon.Geometry.Add", "Daynote.Icon.Geometry.Bold", "Daynote.Icon.Geometry.BulletedList",
        "Daynote.Icon.Geometry.Calendar", "Daynote.Icon.Geometry.Capture", "Daynote.Icon.Geometry.Checkmark",
        "Daynote.Icon.Geometry.ChevronLeft", "Daynote.Icon.Geometry.ChevronRight",
        "Daynote.Icon.Geometry.ChevronUp", "Daynote.Icon.Geometry.ChevronDown", "Daynote.Icon.Geometry.Clipboard",
        "Daynote.Icon.Geometry.Close", "Daynote.Icon.Geometry.Copy", "Daynote.Icon.Geometry.Delete",
        "Daynote.Icon.Geometry.Dismiss", "Daynote.Icon.Geometry.Error", "Daynote.Icon.Geometry.ImageItem",
        "Daynote.Icon.Geometry.Info", "Daynote.Icon.Geometry.InlineCode", "Daynote.Icon.Geometry.Italic",
        "Daynote.Icon.Geometry.Notes", "Daynote.Icon.Geometry.NumberedList", "Daynote.Icon.Geometry.Pause",
        "Daynote.Icon.Geometry.Quit", "Daynote.Icon.Geometry.Resume", "Daynote.Icon.Geometry.Retry",
        "Daynote.Icon.Geometry.Search", "Daynote.Icon.Geometry.Settings", "Daynote.Icon.Geometry.ShowWindow",
        "Daynote.Icon.Geometry.TextItem", "Daynote.Icon.Geometry.Warning"
    ];

    private static readonly string[] SurfaceRoles =
    [
        "Canvas", "Surface.Primary", "Surface.Secondary", "Surface.Hover", "Surface.Pressed",
        "Surface.Selected", "Surface.Disabled", "Status.Success.Surface", "Status.Warning.Surface",
        "Status.Error.Surface"
    ];

    [TestMethod]
    public void StandardPalette_EveryColorHasTheOnlyPermittedPairedBrush()
    {
        var dictionary = Load("Daynote.Colors.xaml");
        var entries = Entries(dictionary);
        var colors = entries.Keys.Where(key => key.StartsWith("Daynote.Color.", StringComparison.Ordinal)).ToArray();
        var brushes = entries.Keys.Where(key => key.StartsWith("Daynote.Brush.", StringComparison.Ordinal)).ToArray();

        Assert.AreEqual(colors.Length, brushes.Length, "A semantic color and brush must be registered as a pair.");
        foreach (var colorKey in colors)
        {
            var role = colorKey["Daynote.Color.".Length..];
            var brushKey = $"Daynote.Brush.{role}";
            Assert.IsTrue(entries.TryGetValue(brushKey, out var brush), $"Missing {brushKey}.");
            Assert.AreEqual($"{{StaticResource {colorKey}}}", brush!.Attribute("Color")?.Value, brushKey);
        }
    }

    [TestMethod]
    public void StandardPalette_ContrastMeetsTextBoundaryFocusAndAccentContracts()
    {
        var colors = ReadColors();

        foreach (var surfaceRole in SurfaceRoles)
        {
            AssertContrast(colors, "Text.Muted", surfaceRole, 4.5);
            AssertContrast(colors, "Border.Control", surfaceRole, 3.0);
            AssertContrast(colors, "Focus", surfaceRole, 3.0);
            AssertContrast(colors, "Accent.500", surfaceRole, 3.0);
            AssertContrast(colors, "Accent.600", surfaceRole, 3.0);
            AssertContrast(colors, "Accent.700", surfaceRole, 3.0);
        }

        AssertContrast(colors, "Text.OnAccent", "Accent.500", 4.5);
        AssertContrast(colors, "Text.OnAccent", "Accent.600", 4.5);
        AssertContrast(colors, "Text.OnAccent", "Accent.700", 4.5);
    }

    [TestMethod]
    public void HighContrastPalette_MapsEveryStandardBrushToAWindowsSystemRole()
    {
        var standardBrushes = Entries(Load("Daynote.Colors.xaml")).Keys
            .Where(key => key.StartsWith("Daynote.Brush.", StringComparison.Ordinal))
            .ToArray();
        var highContrast = Entries(Load("Daynote.Colors.HighContrast.xaml"));

        CollectionAssert.AreEquivalent(standardBrushes, highContrast.Keys.ToArray());
        foreach (var key in standardBrushes)
        {
            var color = highContrast[key].Attribute("Color")?.Value;
            StringAssert.StartsWith(color, "{DynamicResource {x:Static SystemColors.", key);
            StringAssert.EndsWith(color, "ColorKey}}", key);
        }

        AssertSystemRole(highContrast, "Daynote.Brush.Canvas", "WindowColorKey");
        AssertSystemRole(highContrast, "Daynote.Brush.Surface.Selected", "HighlightColorKey");
        AssertSystemRole(highContrast, "Daynote.Brush.Text.Primary", "WindowTextColorKey");
        AssertSystemRole(highContrast, "Daynote.Brush.Text.Muted", "GrayTextColorKey");
        AssertSystemRole(highContrast, "Daynote.Brush.Border.Control", "ControlTextColorKey");
        AssertSystemRole(highContrast, "Daynote.Brush.Accent.500", "HighlightColorKey");
        AssertSystemRole(highContrast, "Daynote.Brush.Text.OnAccent", "HighlightTextColorKey");
        AssertSystemRole(highContrast, "Daynote.Brush.Focus", "HighlightColorKey");
    }

    [TestMethod]
    public void ProductSources_ContainNoRawColorTokensOutsideThePaletteDeclaration()
    {
        // Palette-declaration files are the only permitted homes for raw ARGB tokens: the legacy
        // Daynote.Colors.xaml and the redesign's paired product theme dictionaries (Revision 2026-07-21).
        var paletteFiles = new[]
        {
            "Daynote.Colors.xaml", "Daynote.Product.Light.xaml", "Daynote.Product.Dark.xaml",
        };
        var candidates = Directory.EnumerateFiles(TestPaths.AppRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".cs" or ".xaml")
            .Where(path => !paletteFiles.Any(name => path.EndsWith(name, StringComparison.OrdinalIgnoreCase)))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        var violations = candidates
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, number: index + 1)))
            .Where(item => RawColorToken().IsMatch(item.line))
            .Select(item => $"{Path.GetRelativePath(TestPaths.RepositoryRoot, item.path)}:{item.number}")
            .ToArray();

        Assert.AreEqual(0, violations.Length, $"Raw color tokens: {string.Join(", ", violations)}");
    }

    [TestMethod]
    public void ThemeDictionaries_KeepRequiredSpacingTypeMotionAndLayoutKeysUnique()
    {
        var themeFiles = Directory.EnumerateFiles(Path.Combine(TestPaths.AppRoot, "Themes"), "*.xaml")
            .Where(path => !path.EndsWith("HighContrast.xaml", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var keys = themeFiles.SelectMany(path => Entries(XDocument.Load(path)).Keys).ToArray();

        // The redesign's Light/Dark product dictionaries are INTENTIONALLY parallel: same keys, different
        // values, so the shell swaps one for the other (Revision 2026-07-21). Exclude that pair from the
        // no-accidental-redefinition scan; a separate assertion below pins their key sets to be identical.
        string[] productThemePair = ["Daynote.Product.Light.xaml", "Daynote.Product.Dark.xaml"];
        var uniquenessKeys = themeFiles
            .Where(path => !productThemePair.Any(name => path.EndsWith(name, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(path => Entries(XDocument.Load(path)).Keys)
            .ToArray();
        var duplicates = uniquenessKeys.GroupBy(key => key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        var lightKeys = Entries(Load("Daynote.Product.Light.xaml")).Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var darkKeys = Entries(Load("Daynote.Product.Dark.xaml")).Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(lightKeys, darkKeys, "Product Light and Dark must declare an identical key set so a theme swap resolves every brush.");
        var required = new[]
        {
            "Daynote.Inset.Control", "Daynote.Inset.Pane.Compact", "Daynote.Inset.Pane.Regular",
            "Daynote.Inset.Pane.Wide", "Daynote.Border.Focus", "Daynote.Border.FocusGap",
            "Daynote.Border.SelectionMarker", "Daynote.Size.Target.Primary", "Daynote.Size.Splitter.HitTarget",
            "Daynote.Layout.CompactMax", "Daynote.Layout.RegularMin", "Daynote.Layout.RegularMax",
            "Daynote.Layout.WideMin", "Daynote.Layout.Hysteresis", "Daynote.Motion.Instant",
            "Daynote.Motion.Micro", "Daynote.Motion.Scope", "Daynote.Motion.Panel",
            "Daynote.Motion.Evidence.Midpoint", "Daynote.Type.Body.FontSize", "Daynote.Type.Status.LineHeight"
        };

        Assert.AreEqual(0, duplicates.Length, $"Duplicate resource keys: {string.Join(", ", duplicates)}");
        foreach (var key in required)
            CollectionAssert.Contains(keys, key);
    }

    [TestMethod]
    public void LayoutThresholdResources_FormAContiguousPartitionWithEightDipHysteresis()
    {
        var metrics = Entries(Load("Daynote.Metrics.xaml"));
        var compactMax = DoubleValue(metrics, "Daynote.Layout.CompactMax");
        var regularMin = DoubleValue(metrics, "Daynote.Layout.RegularMin");
        var regularMax = DoubleValue(metrics, "Daynote.Layout.RegularMax");
        var wideMin = DoubleValue(metrics, "Daynote.Layout.WideMin");
        var hysteresis = DoubleValue(metrics, "Daynote.Layout.Hysteresis");

        Assert.AreEqual(1, regularMin - compactMax, "Compact and Regular must have no ambiguous whole-DIP width.");
        Assert.AreEqual(1, wideMin - regularMax, "Regular and Wide must have no ambiguous whole-DIP width.");
        Assert.AreEqual(8, hysteresis, "Layout transitions must retain the Section 4 anti-flapping band.");
        Assert.IsGreaterThan(regularMin + (2 * hysteresis), regularMax);
    }

    [TestMethod]
    public void IconRegistry_ContainsEveryContractGeometryExactlyOnceAndFreezesIt()
    {
        var icons = Entries(Load("Daynote.Icons.xaml"));
        XNamespace presentationOptions = "http://schemas.microsoft.com/winfx/2006/xaml/presentation/options";

        CollectionAssert.AreEquivalent(RequiredIconKeys, icons.Keys.ToArray());
        foreach (var key in RequiredIconKeys)
        {
            var geometry = icons[key];
            Assert.AreEqual("StreamGeometry", geometry.Name.LocalName, key);
            Assert.IsFalse(string.IsNullOrWhiteSpace(geometry.Value), key);
            Assert.AreEqual("True", geometry.Attribute(presentationOptions + "Freeze")?.Value, key);
        }
    }

    [TestMethod]
    public void ThemeResourceReferences_ResolveToDeclaredDaynoteKeys()
    {
        var themeFiles = Directory.EnumerateFiles(Path.Combine(TestPaths.AppRoot, "Themes"), "*.xaml").ToArray();
        var declaredKeys = themeFiles
            .SelectMany(path => Entries(XDocument.Load(path)).Keys)
            .ToHashSet(StringComparer.Ordinal);
        var unresolved = themeFiles
            .SelectMany(path => File.ReadLines(path).SelectMany((line, index) =>
                ResourceReference().Matches(line).Select(match => new
                {
                    Path = path,
                    Line = index + 1,
                    Key = match.Groups[1].Value
                })))
            .Where(reference => reference.Key.StartsWith("Daynote.", StringComparison.Ordinal))
            .Where(reference => !declaredKeys.Contains(reference.Key))
            .Select(reference =>
                $"{Path.GetRelativePath(TestPaths.RepositoryRoot, reference.Path)}:{reference.Line} -> {reference.Key}")
            .ToArray();

        Assert.AreEqual(0, unresolved.Length, $"Unresolved design resources: {string.Join(", ", unresolved)}");
    }

    private static XDocument Load(string fileName) => XDocument.Load(TestPaths.Theme(fileName));

    private static Dictionary<string, XElement> Entries(XDocument document)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document.Root!.Elements()
            .Where(element => element.Attribute(x + "Key") is not null)
            .ToDictionary(element => element.Attribute(x + "Key")!.Value, StringComparer.Ordinal);
    }

    private static Dictionary<string, Argb> ReadColors() => Entries(Load("Daynote.Colors.xaml"))
        .Where(entry => entry.Key.StartsWith("Daynote.Color.", StringComparison.Ordinal))
        .ToDictionary(
            entry => entry.Key["Daynote.Color.".Length..],
            entry => Argb.Parse(entry.Value.Value),
            StringComparer.Ordinal);

    private static void AssertContrast(
        IReadOnlyDictionary<string, Argb> colors,
        string foregroundRole,
        string backgroundRole,
        double minimum)
    {
        var ratio = Contrast(colors[foregroundRole], colors[backgroundRole]);
        Assert.IsGreaterThanOrEqualTo(
            minimum, ratio,
            $"{foregroundRole} on {backgroundRole} was {ratio.ToString("F2", CultureInfo.InvariantCulture)}:1.");
    }

    private static double Contrast(Argb first, Argb second)
    {
        var lighter = Math.Max(first.Luminance, second.Luminance);
        var darker = Math.Min(first.Luminance, second.Luminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static void AssertSystemRole(
        IReadOnlyDictionary<string, XElement> entries,
        string brushKey,
        string systemColorKey) =>
        StringAssert.Contains(entries[brushKey].Attribute("Color")?.Value, $"SystemColors.{systemColorKey}");

    private static double DoubleValue(IReadOnlyDictionary<string, XElement> entries, string key) =>
        double.Parse(entries[key].Value, NumberStyles.Float, CultureInfo.InvariantCulture);

    [GeneratedRegex("#[0-9A-Fa-f]{3,8}(?![0-9A-Fa-f])", RegexOptions.CultureInvariant)]
    private static partial Regex RawColorToken();

    [GeneratedRegex("\\{(?:Static|Dynamic)Resource\\s+(Daynote\\.[^}\\s]+)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex ResourceReference();

    private readonly record struct Argb(byte Red, byte Green, byte Blue)
    {
        public double Luminance => 0.2126 * Linear(Red) + 0.7152 * Linear(Green) + 0.0722 * Linear(Blue);

        public static Argb Parse(string value)
        {
            Assert.AreEqual(9, value.Length, $"Expected an ARGB token but found '{value}'.");
            return new Argb(
                byte.Parse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(value.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        private static double Linear(byte component)
        {
            var value = component / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
    }
}
