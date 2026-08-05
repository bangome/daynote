using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using WpfBinding = System.Windows.Data.Binding;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfToolBar = System.Windows.Controls.ToolBar;

namespace Daynote.App.Showcase;

internal static partial class PrimitiveFixtureFactory
{
    public static FrameworkElement Create(ShowcaseSelection selection) => selection.Page.PrimitiveId switch
    {
        "app-shell" => AppShell(selection),
        "workspace-view-switch" => WorkspaceViewSwitch(selection),
        "pane-splitter" => PaneSplitter(selection),
        "calendar-day" => CalendarDay(selection),
        "date-header" => DateHeader(selection),
        "note-tab" => NoteTab(selection),
        "markdown-editor" => MarkdownEditor(selection),
        "editor-toolbar" => EditorToolbar(selection),
        "clipboard-item" => ClipboardItem(selection),
        "sidebar-note-list" => SidebarNoteList(selection),
        "clipboard-drawer" => ClipboardDrawer(selection),
        "search" => Search(selection),
        "button" => Buttons(selection),
        "status-banner" => StatusBanner(selection),
        "consent-panel" => ConsentPanel(selection),
        "settings-row" => SettingsRow(selection),
        "tray-menu" => TrayMenu(selection),
        "patterns" => Patterns(selection),
        _ => throw new InvalidOperationException($"No fixture exists for {selection.Page.PrimitiveId}.")
    };

    private static FrameworkElement WorkspaceViewSwitch(ShowcaseSelection selection)
    {
        var strip = TabStrip("Workspace view switch");
        foreach (var label in new[] { "Navigate", "Notes", "Clipboard" })
        {
            var item = new TabItem { Header = TabHeader(label) };
            ShowcaseResources.Style(item, "Daynote.Style.WorkspaceViewSwitchItem");
            AutomationProperties.SetName(item, $"{label} workspace view");
            item.IsSelected = label == "Notes";
            strip.Items.Add(item);
        }
        ShowcaseFocus.SetIsPreferred((DependencyObject)strip.Items[0], true);
        return WithStateCue(strip, selection);
    }

    private static FrameworkElement PaneSplitter(ShowcaseSelection selection)
    {
        var splitter = new System.Windows.Controls.Primitives.Thumb
        {
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = true,
            Cursor = System.Windows.Input.Cursors.SizeWE
        };
        ShowcaseResources.Style(splitter, "Daynote.Style.PaneSplitter");
        AutomationProperties.SetName(splitter,
            selection.Page.Layout == ShowcaseLayout.Wide ? "Clipboard rail splitter" : "Support rail splitter");
        AutomationProperties.SetHelpText(splitter, "Left and Right adjust; Home and End select bounds; Escape restores width.");
        ShowcaseFocus.SetIsPreferred(splitter, true);
        return WithStateCue(splitter, selection);
    }

    private static FrameworkElement CalendarDay(ShowcaseSelection selection)
    {
        var day = new System.Windows.Controls.Primitives.ToggleButton
        {
            Content = "16",
            Tag = CalendarCues(),
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ShowcaseResources.Style(day, "Daynote.Style.CalendarDay");
        AutomationProperties.SetName(day, "Thursday, July 16, today, has note and clipboard");
        ShowcaseFocus.SetIsPreferred(day, true);
        AutomationProperties.SetItemStatus(day, StateMessage(selection));
        return WithStateCue(day, selection);
    }

    private static FrameworkElement DateHeader(ShowcaseSelection selection)
    {
        var header = ShowcaseUi.Stack(WpfOrientation.Vertical, "Selected date heading");
        var date = RoleText("Thursday, July 16", "DateTitle", "Selected local date");
        ShowcaseResources.Style(date, "Daynote.Style.DateHeader");
        header.Children.Add(date);
        header.Children.Add(RoleText(StressStatus(selection), "Status", "Date projection status"));
        return header;
    }

    private static FrameworkElement NoteTab(ShowcaseSelection selection)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var strip = TabStrip("Note tab strip");
        var title = StressTitle(selection.Stress);
        var tab = new TabItem
        {
            Header = NoteTabHeader(title, selection.Stress == ShowcaseStress.Long),
            IsSelected = true
        };
        ShowcaseResources.Style(tab, "Daynote.Style.NoteTab");
        AutomationProperties.SetName(tab, title);
        ToolTipService.SetToolTip(tab, title);
        ShowcaseFocus.SetIsPreferred(tab, true);
        strip.Items.Add(tab);
        AddColumn(row, strip, 0);
        AddColumn(row, IconButton($"Close {title}", "Daynote.Icon.Geometry.Close"), 1);
        AddColumn(row, IconButton("Add note", "Daynote.Icon.Geometry.Add"), 2);
        return WithStateCue(row, selection);
    }

    private static FrameworkElement MarkdownEditor(ShowcaseSelection selection)
    {
        var editor = new WpfTextBox
        {
            AcceptsReturn = true,
            Text = selection.Page.State == "empty" ? string.Empty : ShowcaseUi.StressText(selection.Stress),
            TextWrapping = TextWrapping.WrapWithOverflow,
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        editor.CaretIndex = 0;
        ShowcaseResources.Style(editor, "Daynote.Style.MarkdownEditor");
        AutomationProperties.SetName(editor, "Markdown editor for Thursday, July 16, Note 1");
        AutomationProperties.SetHelpText(editor, "Sole vertical document scroll owner; Ctrl+S flushes save.");
        ShowcaseFocus.SetIsPreferred(editor, true);
        return WithStateCue(editor, selection);
    }

    private static FrameworkElement EditorToolbar(ShowcaseSelection selection)
    {
        var toolbar = new WpfToolBar();
        ShowcaseResources.Style(toolbar, "Daynote.Style.EditorToolbar");
        AutomationProperties.SetName(toolbar, "Editor toolbar");
        var row = new Grid();
        row.SetBinding(FrameworkElement.WidthProperty, new WpfBinding(nameof(FrameworkElement.ActualWidth)) { Source = toolbar });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var status = StatusText(StressStatus(selection), "Save status");
        var commands = ShowcaseUi.Stack(WpfOrientation.Horizontal, "Markdown formatting commands");
        foreach (var command in MarkdownCommands)
            commands.Children.Add(IconButton(command.Name, command.Geometry));
        AddColumn(row, status, 0);
        AddColumn(row, commands, 1);
        toolbar.Items.Add(row);
        return WithStateCue(toolbar, selection);
    }

    private static FrameworkElement WithStateCue(FrameworkElement content, ShowcaseSelection selection)
    {
        var panel = ShowcaseUi.Stack(WpfOrientation.Vertical, $"{selection.Page.PrimitiveName} state fixture");
        panel.Children.Add(content);
        if (selection.Page.State is "loading")
        {
            var progress = new WpfProgressBar { IsIndeterminate = selection.Motion == ShowcaseMotion.Normal };
            AutomationProperties.SetName(progress, $"{selection.Page.PrimitiveName} loading");
            panel.Children.Add(progress);
        }
        else if (selection.Page.State is "empty" or "error")
        {
            panel.Children.Add(StatusText(StressStatus(selection), "Forced state explanation"));
        }
        return panel;
    }

    private static string StateMessage(ShowcaseSelection selection) => selection.Page.State switch
    {
        "loading" => "Loading is bounded and keeps the eventual region stable.",
        "empty" => "No deterministic specimen content is available in this forced empty state.",
        "error" => "The forced failure states its effect and offers a safe recovery action.",
        "disabled" => "This transition is guarded; content remains identifiable.",
        _ => $"The {selection.Page.State} state is forced without pointer timing."
    };
}
