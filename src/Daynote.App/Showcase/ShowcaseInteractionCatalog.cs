namespace Daynote.App.Showcase;

public enum ShowcaseInputModality
{
    Pointer,
    Keyboard
}

public sealed record ShowcaseInteractionDefinition(
    string FamilyId,
    string PageId,
    string InitiatorAutomationName,
    string InitiatorControlType,
    string SemanticAction,
    string PointerRoutedEvent,
    string KeyboardInitiatorAutomationName,
    string KeyboardKey,
    string MotionTargetAutomationName,
    string StateBefore,
    string StateAfter,
    string FocusBefore,
    string FocusAfter,
    string ScrollOwner,
    string ScrollOwnerAutomationName);

public static class ShowcaseInteractionCatalog
{
    public static IReadOnlyList<ShowcaseInteractionDefinition> Definitions { get; } =
    [
        Definition(
            "app-shell", "wide.app-shell.default", "July 15, 2026", "ToggleButton",
            "select-date", "Space", "AppShell isolated structure specimen",
            "selected-date=2026-07-16", "selected-date=2026-07-15",
            "Search notes and clipboard", "July 15, 2026", "none", "none"),
        Definition(
            "date-header", "wide.app-shell.default", "July 15, 2026", "ToggleButton",
            "select-date", "Space", "Selected date heading",
            "selected-date=2026-07-16", "selected-date=2026-07-15",
            "Search notes and clipboard", "July 15, 2026", "editor region",
            "Markdown editor for Thursday, July 16, Note 1"),
        Definition(
            "note-tab", "wide.app-shell.default", "Note 2", "TabItem",
            "select-note", "Right", "Note 2",
            "selected-note=Note 1", "selected-note=Note 2",
            "Note 1", "Note 2", "editor", "Markdown editor for Thursday, July 16, Note 1", "Note 1"),
        Definition(
            "editor-toolbar", "wide.app-shell.default", "Bold", "Button",
            "format-bold", "Enter", "Save status",
            "format=plain", "format=bold",
            "Bold", "Bold", "MarkdownEditor", "Markdown editor for Thursday, July 16, Note 1"),
        Definition(
            "clipboard-item", "wide.clipboard-drawer.active", "Copy clipboard item captured at 10:24", "Button",
            "copy-item", "Enter", "Copy clipboard item captured at 10:24",
            "copy-status=ready", "copy-status=copied",
            "Copy clipboard item captured at 10:24", "Copy clipboard item captured at 10:24",
            "clipboard list", "Clipboard list"),
        Definition(
            "sidebar-note-list", "wide.sidebar-note-list.default", "Note 2", "ListBoxItem",
            "select-note-row", "Enter", "Note 2",
            "selected-row=Note 1", "selected-row=Note 2",
            "Go to today", "Note 2", "sidebar note list", "Sidebar note list"),
        Definition(
            "clipboard-drawer", "wide.clipboard-drawer.default", "Toggle clipboard drawer", "Button",
            "toggle-drawer", "Enter", "Clipboard drawer panel",
            "drawer=collapsed", "drawer=open",
            "Toggle clipboard drawer", "Toggle clipboard drawer",
            "clipboard list", "Clipboard list"),
        Definition(
            "search", "wide.search.default", "Selected search result, note, July 16", "ListBoxItem",
            "open-search-result", "Enter", "Search overlay",
            "search-result=selected", "search-result=opened",
            "Search notes and clipboard", "Selected search result, note, July 16",
            "search result list", "Search result list"),
        Definition(
            "status-banner", "wide.status-banner.default", "Retry status action", "Button",
            "retry-status", "Enter", "Success StatusBanner",
            "status=recovery-available", "status=recovered",
            "Retry status action", "Retry status action", "none", "none"),
        Definition(
            "consent-panel", "wide.consent-panel.default", "Enable local clipboard capture", "Button",
            "enable-capture", "Enter", "Clipboard capture consent panel",
            "capture=off", "capture=enabled",
            "Enable local clipboard capture", "Enable local clipboard capture",
            "none", "none"),
        Definition(
            "settings-row", "wide.settings-row.default", "Toggle Start with Windows", "Button",
            "toggle-startup", "Enter", "Toggle Start with Windows",
            "startup=off", "startup=on",
            "Toggle Start with Windows", "Toggle Start with Windows",
            "none", "none")
    ];

    public static ShowcaseInteractionDefinition Find(string familyId) =>
        Definitions.FirstOrDefault(definition =>
            definition.FamilyId.Equals(familyId, StringComparison.Ordinal))
        ?? throw new ArgumentException(
            $"--interaction-sequence must name a supported animated family: {string.Join(", ", Definitions.Select(item => item.FamilyId))}.");

    private static ShowcaseInteractionDefinition Definition(
        string familyId,
        string pageId,
        string initiatorAutomationName,
        string initiatorControlType,
        string semanticAction,
        string keyboardKey,
        string motionTargetAutomationName,
        string stateBefore,
        string stateAfter,
        string focusBefore,
        string focusAfter,
        string scrollOwner,
        string scrollOwnerAutomationName,
        string? keyboardInitiatorAutomationName = null) =>
        new(
            familyId,
            pageId,
            initiatorAutomationName,
            initiatorControlType,
            semanticAction,
            "PreviewMouseLeftButtonUp",
            keyboardInitiatorAutomationName ?? initiatorAutomationName,
            keyboardKey,
            motionTargetAutomationName,
            stateBefore,
            stateAfter,
            focusBefore,
            focusAfter,
            scrollOwner,
            scrollOwnerAutomationName);
}
