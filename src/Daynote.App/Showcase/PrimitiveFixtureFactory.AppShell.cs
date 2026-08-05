using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Daynote.App.Showcase;

internal static partial class PrimitiveFixtureFactory
{
    private static FrameworkElement AppShell(ShowcaseSelection selection)
    {
        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var command = ShellCommandRegion(selection);
        var workspace = selection.Page.Layout switch
        {
            ShowcaseLayout.Compact => CompactWorkspace(selection),
            ShowcaseLayout.Regular => RegularWorkspace(selection),
            _ => WideWorkspace(selection)
        };
        var status = StatusText(StressStatus(selection), "AppShell status region");
        Grid.SetRow(workspace, 1);
        Grid.SetRow(status, 2);
        shell.Children.Add(command);
        shell.Children.Add(workspace);
        shell.Children.Add(status);
        ShowcaseFocus.SetIsPreferred(command, true);

        var specimen = ShowcaseUi.Panel("Daynote.Style.AppShell", "AppShell isolated structure specimen", shell);
        specimen.IsEnabled = selection.Page.State != "disabled";
        if (selection.Page.Id == "wide.app-shell.default" && selection.Stress == ShowcaseStress.Default)
            ShowcaseInteractionBehavior.BindAppShellWorkspace(specimen);
        return specimen;
    }

    private static FrameworkElement ShellCommandRegion(ShowcaseSelection selection)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var search = new WpfTextBox { Text = StressQuery(selection.Stress) };
        ShowcaseResources.Style(search, "Daynote.Style.SearchBox");
        AutomationProperties.SetName(search, "Search notes and clipboard");
        var capture = StatusContent("Capture enabled", "Daynote.Style.StatusBanner.CaptureEnabled", "Clipboard capture enabled");
        var drawerToggle = ShowcaseUi.Button("Clipboard", "Daynote.Style.Button.Secondary", "Toggle clipboard drawer");
        var settings = IconButton("Open settings", "Daynote.Icon.Geometry.Settings");
        AddColumn(row, search, 0);
        AddColumn(row, capture, 1);
        AddColumn(row, drawerToggle, 2);
        AddColumn(row, settings, 3);
        AutomationProperties.SetHelpText(row, $"{selection.Page.Layout} command region remains fixed; the clipboard drawer stays collapsed until its toggle is used.");
        return row;
    }

    private static FrameworkElement CompactWorkspace(ShowcaseSelection selection)
    {
        var grid = new Grid();
        AutomationProperties.SetName(grid, "Compact one-view workspace");
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var workspaceSwitch = WorkspaceSwitch(["Navigate", "Notes", "Clipboard"], "Notes", "Compact workspace switch");
        var editor = EditorRegion(selection);
        Grid.SetRow(editor, 1);
        grid.Children.Add(workspaceSwitch);
        grid.Children.Add(editor);
        return grid;
    }

    private static FrameworkElement RegularWorkspace(ShowcaseSelection selection) => SidebarWorkspace(selection);

    private static FrameworkElement WideWorkspace(ShowcaseSelection selection) => SidebarWorkspace(selection);

    private static FrameworkElement SidebarWorkspace(ShowcaseSelection selection)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ShowcaseResources.Get<double>("Daynote.Size.Sidebar.Default")) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ShowcaseResources.Get<double>("Daynote.Size.Splitter.Visual")) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddColumn(grid, SidebarRegion(selection), 0);
        AddColumn(grid, ShellDivider("Sidebar and editor divider"), 1);
        AddColumn(grid, EditorRegion(selection), 2);
        return grid;
    }

    private static FrameworkElement SidebarRegion(ShowcaseSelection selection)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var today = ShowcaseUi.Button("Today", "Daynote.Style.Button.Ghost", "Go to today");
        var list = SidebarNoteRows(selection);
        var calendar = MiniCalendar(selection);
        Grid.SetRow(list, 1);
        Grid.SetRow(calendar, 2);
        grid.Children.Add(today);
        grid.Children.Add(list);
        grid.Children.Add(calendar);
        var region = ShellRegion(grid, "Daynote.Brush.Surface.Secondary", "Sidebar navigation region");
        region.SetResourceReference(Border.PaddingProperty, "Daynote.Inset.Pane.Compact");
        return region;
    }

    private static FrameworkElement SidebarNoteRows(ShowcaseSelection selection)
    {
        var list = new WpfListBox { HorizontalContentAlignment = WpfHorizontalAlignment.Stretch };
        AutomationProperties.SetName(list, "Sidebar note list");
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        foreach (var title in new[] { StressTitle(selection.Stress), "Note 2" })
        {
            var row = new ListBoxItem
            {
                Content = RoleText(title, "Label", $"{title} sidebar row title"),
                IsSelected = title != "Note 2"
            };
            ShowcaseResources.Style(row, "Daynote.Style.SidebarNoteRow");
            AutomationProperties.SetName(row, title);
            ToolTipService.SetToolTip(row, title);
            list.Items.Add(row);
        }
        return list;
    }

    private static FrameworkElement MiniCalendar(ShowcaseSelection selection)
    {
        var stack = ShowcaseUi.Stack(WpfOrientation.Vertical, "Sidebar mini calendar");
        stack.Children.Add(RoleText(
            selection.Stress == ShowcaseStress.Cjk ? "2026년 7월" : "July 2026",
            "PaneTitle", "Calendar month"));
        stack.Children.Add(RoleText(
            selection.Stress == ShowcaseStress.Cjk ? "일   월   화   수   목   금   토" : "Su   Mo   Tu   We   Th   Fr   Sa",
            "Status", "Calendar weekday headings"));
        stack.Children.Add(CalendarWeek());
        stack.Children.Add(RoleText(StressStatus(selection), "Status", "Calendar scope state"));
        return stack;
    }

    private static FrameworkElement EditorRegion(ShowcaseSelection selection)
    {
        var editor = new Grid();
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var date = RoleText(
            selection.Stress == ShowcaseStress.Cjk ? "2026년 7월 16일 목요일" : "Thursday, July 16",
            "DateTitle", "Selected date heading");
        ShowcaseResources.Style(date, "Daynote.Style.DateHeader");
        var tabs = NoteTabs(selection);
        var bodyText = selection.Page.Layout == ShowcaseLayout.Compact && selection.Stress == ShowcaseStress.Cjk
            ? "입력기 조합 시험 · 안녕하세요 Daynote · 한글 Latin mixed-script"
            : StressEditorBody(selection);
        var body = new WpfTextBox
        {
            AcceptsReturn = true,
            Text = selection.Page.State == "empty" ? string.Empty : bodyText,
            TextWrapping = TextWrapping.WrapWithOverflow,
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        body.CaretIndex = 0;
        ShowcaseResources.Style(body, "Daynote.Style.MarkdownEditor");
        if (selection.Page.Layout == ShowcaseLayout.Compact)
            body.SetResourceReference(WpfTextBox.PaddingProperty, "Daynote.Inset.Pane.Compact");
        AutomationProperties.SetName(body, "Markdown editor for Thursday, July 16, Note 1");
        AutomationProperties.SetHelpText(body, selection.Stress == ShowcaseStress.Cjk
            ? "Committed mixed-script fixture only; OS IME preedit, caret, and candidate window require later interaction QA."
            : "Sole vertical document scroll owner; unbroken tokens wrap without a horizontal scrollbar.");
        var toolbar = EditorCommands(selection);
        Grid.SetRow(date, 0);
        Grid.SetRow(tabs, 1);
        Grid.SetRow(body, 2);
        Grid.SetRow(toolbar, 3);
        editor.Children.Add(date);
        editor.Children.Add(tabs);
        editor.Children.Add(body);
        editor.Children.Add(toolbar);
        return ShellRegion(editor, "Daynote.Brush.Surface.Primary", "Notes editor region");
    }

    private static FrameworkElement ClipboardRegion(ShowcaseSelection selection, string heading)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var title = RoleText(
            selection.Stress == ShowcaseStress.Cjk ? "클립보드" : heading,
            "PaneTitle", "Clipboard heading");
        var status = StatusContent("Capture enabled", "Daynote.Style.StatusBanner.CaptureEnabled", "Clipboard capture state");
        var list = new WpfListBox { HorizontalContentAlignment = WpfHorizontalAlignment.Stretch };
        AutomationProperties.SetName(list, "Clipboard list");
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.Items.Add(ClipboardListItem("Text item", "10:24", StressClipboardPreview(selection.Stress)));
        list.Items.Add(ClipboardListItem("Text item", "09:48", StressClipboardStatus(selection)));
        Grid.SetRow(status, 1);
        Grid.SetRow(list, 2);
        grid.Children.Add(title);
        grid.Children.Add(status);
        grid.Children.Add(list);
        return ShellRegion(grid, "Daynote.Brush.Surface.Secondary", "Clipboard region");
    }

    private static Border ShellRegion(UIElement content, string background, string name)
    {
        var region = new Border { Child = content };
        region.SetResourceReference(Border.BackgroundProperty, background);
        region.SetResourceReference(Border.BorderBrushProperty, "Daynote.Brush.Border.Subtle");
        region.SetResourceReference(Border.BorderThicknessProperty, "Daynote.Thickness.Border.Thin");
        region.SetResourceReference(Border.PaddingProperty, "Daynote.Inset.Pane.Regular");
        AutomationProperties.SetName(region, name);
        return region;
    }

    private static Border ShellDivider(string name)
    {
        var divider = new Border();
        divider.SetResourceReference(Border.BackgroundProperty, "Daynote.Brush.Border.Control");
        AutomationProperties.SetName(divider, name);
        return divider;
    }

    private static void AddColumn(Grid grid, UIElement content, int column)
    {
        Grid.SetColumn(content, column);
        grid.Children.Add(content);
    }
}
