using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

/// <summary>
/// The two Windows shells say the same things about the design-B layout.
/// </summary>
/// <remarks>
/// The WPF shell's <c>ProductShellViewModel.Desk.cs</c> and the Avalonia shell's
/// <c>DesktopShellViewModel.Views.cs</c> are deliberate mirrors: the same surface over the same panel
/// view models, one per framework, because the two are otherwise bound to different XAML dialects.
/// Duplication like that drifts silently — one shell gains a property and the other window quietly
/// keeps an older layout — so the member names are compared as text here rather than trusted.
/// <para>
/// This reads the sources rather than reflecting on the types, because this assembly cannot reference
/// the Avalonia shell. That also means a member has to be declared, not merely inherited, in both.
/// </para>
/// </remarks>
[TestClass]
public sealed class DeskSurfaceParityTests
{
    private const string WpfSource = "src/Daynote.App/Shell/Product/ProductShellViewModel.Desk.cs";
    private const string AvaloniaSource = "src/Daynote.Desktop/ViewModels/DesktopShellViewModel.Views.cs";
    private const string WpfSections = "src/Daynote.App/Settings/SettingsViewModel.Sections.cs";
    private const string AvaloniaSections = "src/Daynote.Desktop/ViewModels/DesktopSettingsViewModel.Sections.cs";

    [TestMethod]
    [DataRow(WpfSource, AvaloniaSource, DisplayName = "the shell's view surface")]
    [DataRow(WpfSections, AvaloniaSections, DisplayName = "the settings dialog's sections")]
    public void Both_shells_declare_the_same_design_b_surface(string wpfPath, string avaloniaPath)
    {
        string[] wpf = Surface(wpfPath);
        string[] avalonia = Surface(avaloniaPath);

        string[] onlyWpf = [.. wpf.Except(avalonia, StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        string[] onlyAvalonia = [.. avalonia.Except(wpf, StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        Assert.AreEqual(
            0,
            onlyWpf.Length + onlyAvalonia.Length,
            $"The two shells' design-B surfaces have drifted. Only in WPF: {Join(onlyWpf)}. " +
            $"Only in Avalonia: {Join(onlyAvalonia)}.");
    }

    [TestMethod]
    public void The_surface_is_large_enough_that_an_empty_read_cannot_pass()
    {
        // Both files parsing to nothing would satisfy the comparison above. The design's header,
        // navigation, lists, palette and account row are dozens of members between them.
        Assert.IsTrue(
            Surface(WpfSource).Length >= 40,
            "Far fewer members than the design-B surface has — the parser above is reading the wrong thing.");
    }

    /// <summary>The declared members: properties, commands and the backing fields the toolkit promotes.</summary>
    private static string[] Surface(string relativePath)
    {
        string text = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        // Only the shell's own partial: PaletteActionViewModel sits in the same file in both and is
        // its own type.
        int end = text.IndexOf("/// <summary>One quick action in the palette", StringComparison.Ordinal);
        if (end > 0)
        {
            text = text[..end];
        }

        var names = new SortedSet<string>(StringComparer.Ordinal);

        // public string Foo => ..., public ObservableCollection<X> Foo { get; }, public static string Foo
        foreach (Match match in Regex.Matches(text, @"^\s{4}public\s+(?:static\s+)?[\w<>,?\[\]\. ]+?\s(\w+)\s*(?:=>|\{)", RegexOptions.Multiline))
        {
            names.Add(match.Groups[1].Value);
        }

        // [ObservableProperty] private bool _isListMode; -> IsListMode
        foreach (Match match in Regex.Matches(text, @"\[ObservableProperty\]\s*\r?\n\s*private\s+[\w<>?\.]+\s+_(\w+)", RegexOptions.Multiline))
        {
            string field = match.Groups[1].Value;
            names.Add(char.ToUpperInvariant(field[0]) + field[1..]);
        }

        // [RelayCommand] private void ShowEditor() -> ShowEditorCommand
        foreach (Match match in Regex.Matches(text, @"\[RelayCommand\]\s*\r?\n\s*private\s+(?:async\s+)?[\w<>?\.]+\s+(\w+)\s*\(", RegexOptions.Multiline))
        {
            names.Add(match.Groups[1].Value + "Command");
        }

        return [.. names];
    }

    private static string Join(string[] names) => names.Length == 0 ? "(none)" : string.Join(", ", names);
}
