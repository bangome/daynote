namespace Daynote.App.Tests;

internal sealed record ShowcaseInteractionContract(
    string Page,
    string Target,
    string ControlType,
    string Action,
    string Key,
    string MotionTarget,
    string Before,
    string After,
    string FocusBefore,
    string ScrollOwner,
    string ScrollOwnerAutomationName,
    double DurationMilliseconds,
    bool Translates);

internal static class ShowcaseInteractionContractTable
{
    internal static IReadOnlyDictionary<string, ShowcaseInteractionContract> Contracts { get; } =
        new Dictionary<string, ShowcaseInteractionContract>(StringComparer.Ordinal)
        {
            ["app-shell"] = new(
                "wide.app-shell.default", "July 15, 2026", "ToggleButton", "select-date", "Space",
                "AppShell isolated structure specimen", "selected-date=2026-07-16", "selected-date=2026-07-15",
                "Search notes and clipboard", "none", "none", 200, false),
            ["date-header"] = new(
                "wide.app-shell.default", "July 15, 2026", "ToggleButton", "select-date", "Space",
                "Selected date heading", "selected-date=2026-07-16", "selected-date=2026-07-15",
                "Search notes and clipboard", "editor region", "Markdown editor for Thursday, July 16, Note 1", 200, false),
            ["note-tab"] = new(
                "wide.app-shell.default", "Note 2", "TabItem", "select-note", "Right", "Note 2",
                "selected-note=Note 1", "selected-note=Note 2", "Note 1", "editor",
                "Markdown editor for Thursday, July 16, Note 1", 120, false),
            ["editor-toolbar"] = new(
                "wide.app-shell.default", "Bold", "Button", "format-bold", "Enter", "Save status",
                "format=plain", "format=bold", "Bold", "MarkdownEditor",
                "Markdown editor for Thursday, July 16, Note 1", 120, false),
            ["clipboard-item"] = new(
                "wide.clipboard-drawer.active", "Copy clipboard item captured at 10:24", "Button", "copy-item", "Enter",
                "Copy clipboard item captured at 10:24", "copy-status=ready", "copy-status=copied",
                "Copy clipboard item captured at 10:24", "clipboard list", "Clipboard list", 120, false),
            ["sidebar-note-list"] = new(
                "wide.sidebar-note-list.default", "Note 2", "ListBoxItem", "select-note-row", "Enter", "Note 2",
                "selected-row=Note 1", "selected-row=Note 2", "Go to today", "sidebar note list",
                "Sidebar note list", 120, false),
            ["clipboard-drawer"] = new(
                "wide.clipboard-drawer.default", "Toggle clipboard drawer", "Button", "toggle-drawer", "Enter",
                "Clipboard drawer panel", "drawer=collapsed", "drawer=open", "Toggle clipboard drawer",
                "clipboard list", "Clipboard list", 180, true),
            ["search"] = new(
                "wide.search.default", "Selected search result, note, July 16", "ListBoxItem", "open-search-result", "Enter",
                "Search overlay", "search-result=selected", "search-result=opened", "Search notes and clipboard",
                "search result list", "Search result list", 180, true),
            ["status-banner"] = new(
                "wide.status-banner.default", "Retry status action", "Button", "retry-status", "Enter",
                "Success StatusBanner", "status=recovery-available", "status=recovered", "Retry status action",
                "none", "none", 120, false),
            ["consent-panel"] = new(
                "wide.consent-panel.default", "Enable local clipboard capture", "Button", "enable-capture", "Enter",
                "Clipboard capture consent panel", "capture=off", "capture=enabled", "Enable local clipboard capture",
                "none", "none", 180, false),
            ["settings-row"] = new(
                "wide.settings-row.default", "Toggle Start with Windows", "Button", "toggle-startup", "Enter",
                "Toggle Start with Windows", "startup=off", "startup=on", "Toggle Start with Windows",
                "none", "none", 120, false)
        };
}
