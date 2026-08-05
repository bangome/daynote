using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daynote.App.Showcase;

public enum ShowcaseLayout { Compact, Regular, Wide }

public sealed record ShowcasePrimitive(
    string Id,
    string Name,
    IReadOnlyList<string> States,
    string FocusOwner,
    string ScrollOwner,
    bool Animated);

public sealed record ShowcasePage(
    string Id,
    string PrimitiveId,
    string PrimitiveName,
    string State,
    ShowcaseLayout Layout,
    string AutomationName,
    string FocusOwner,
    string ScrollOwner,
    bool Animated);

public sealed record ShowcaseManifestDocument(
    string Schema,
    string BuildIdentity,
    DateTimeOffset BuildModifiedUtc,
    DateTimeOffset SourceModifiedUtc,
    IReadOnlyList<ShowcasePrimitive> Primitives,
    IReadOnlyList<ShowcasePage> Pages)
{
    public int PrimitiveCount => Primitives.Count;
    public int PageCount => Pages.Count;
}

public static class ShowcaseManifest
{
    private static readonly ShowcasePrimitive[] Definitions =
    [
        Primitive("app-shell", "AppShell", "command region", "none", true,
            "default", "focus", "disabled", "loading", "empty", "error"),
        Primitive("workspace-view-switch", "WorkspaceViewSwitch", "selected switch item", "selected view", false,
            "default", "hover", "active", "focus", "disabled", "loading", "error"),
        Primitive("pane-splitter", "PaneSplitter", "splitter", "none", false,
            "default", "hover", "active", "focus", "disabled"),
        Primitive("calendar-day", "CalendarDay", "calendar day", "calendar region", false,
            "default", "hover", "active", "focus", "disabled", "loading", "error"),
        Primitive("date-header", "DateHeader", "none", "editor region", true,
            "default", "loading", "empty", "error"),
        Primitive("note-tab", "NoteTabStrip / NoteTab", "selected note tab", "editor", true,
            "default", "hover", "active", "focus", "disabled", "loading", "empty", "error"),
        Primitive("markdown-editor", "MarkdownEditor", "editor caret", "MarkdownEditor", false,
            "default", "active", "focus", "disabled", "loading", "empty", "error"),
        Primitive("editor-toolbar", "EditorToolbar", "format command", "MarkdownEditor", true,
            "default", "hover", "active", "focus", "disabled", "loading", "error"),
        Primitive("clipboard-item", "ClipboardItem", "clipboard action", "clipboard list", true,
            "default", "hover", "active", "focus", "disabled", "loading", "empty", "error"),
        Primitive("sidebar-note-list", "SidebarNoteList", "selected note row", "sidebar note list", true,
            "default", "hover", "active", "focus", "disabled", "loading", "empty", "error"),
        Primitive("clipboard-drawer", "ClipboardDrawer", "drawer toggle", "clipboard list", true,
            "default", "hover", "active", "focus", "disabled", "loading", "empty", "error"),
        Primitive("search", "SearchBox / SearchOverlay / SearchResult", "search result", "search result list", true,
            "default", "hover", "active", "focus", "disabled", "loading", "empty", "error"),
        Primitive("button", "Button / IconButton", "primary button", "none", false,
            "default", "hover", "active", "focus", "disabled", "loading", "error"),
        Primitive("status-banner", "StatusBanner", "recovery action", "none", true,
            "default", "hover", "active", "focus", "disabled", "loading", "empty", "error"),
        Primitive("consent-panel", "ConsentPanel", "safe consent action", "consent body", true,
            "default", "hover", "active", "focus", "disabled", "loading", "error"),
        Primitive("settings-row", "SettingsRow", "settings control", "settings body", true,
            "default", "hover", "active", "focus", "disabled", "loading", "empty", "error"),
        Primitive("tray-menu", "TrayMenu representation", "menu command", "none", false,
            "default", "hover", "active", "focus", "disabled", "loading", "error"),
        Primitive("patterns", "Empty / Loading / Error patterns", "recovery action", "owning pane", false,
            "hover", "active", "focus", "disabled", "loading", "empty", "error")
    ];

    public static IReadOnlyList<ShowcasePrimitive> Primitives => Definitions;

    public static IReadOnlyList<ShowcasePage> Pages { get; } =
        Enum.GetValues<ShowcaseLayout>()
            .SelectMany(layout => Definitions.SelectMany(primitive => primitive.States.Select(state =>
                new ShowcasePage(
                    $"{Slug(layout)}.{primitive.Id}.{state}",
                    primitive.Id,
                    primitive.Name,
                    state,
                    layout,
                    $"{primitive.Name} {state} {layout} showcase",
                    primitive.FocusOwner,
                    primitive.ScrollOwner,
                    primitive.Animated))))
            .ToArray();

    public static ShowcasePage FindPage(string? id)
    {
        var requested = id ?? "wide.app-shell.default";
        return Pages.FirstOrDefault(page => string.Equals(page.Id, requested, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown showcase page '{requested}'. Use --list to enumerate page IDs.");
    }

    public static ShowcaseManifestDocument CreateDocument(string sourceRoot)
    {
        var assembly = typeof(ShowcaseManifest).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "unknown";
        var mvid = assembly.ManifestModule.ModuleVersionId.ToString("N");
        var buildModified = new DateTimeOffset(File.GetLastWriteTimeUtc(assembly.Location), TimeSpan.Zero);
        return new ShowcaseManifestDocument(
            "daynote.showcase/v1",
            $"{version}+mvid.{mvid}",
            buildModified,
            ShowcaseSourceClock.LatestWrite(sourceRoot),
            Primitives,
            Pages);
    }

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };

    private static ShowcasePrimitive Primitive(
        string id, string name, string focusOwner, string scrollOwner, bool animated, params string[] states) =>
        new(id, name, states, focusOwner, scrollOwner, animated);

    private static string Slug(ShowcaseLayout layout) => layout.ToString().ToLowerInvariant();
}

internal static class ShowcaseSourceClock
{
    public static DateTimeOffset LatestWrite(string root)
    {
        var directory = new DirectoryInfo(root);
        if (!directory.Exists)
            return DateTimeOffset.MinValue;

        var latest = directory.EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(file => file.Extension is ".cs" or ".xaml")
            .Where(file => !IsBuildArtifact(file.FullName))
            .Select(file => file.LastWriteTimeUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
        return new DateTimeOffset(DateTime.SpecifyKind(latest, DateTimeKind.Utc));
    }

    private static bool IsBuildArtifact(string path)
    {
        var separator = Path.DirectorySeparatorChar;
        return path.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase) ||
               path.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase);
    }
}
