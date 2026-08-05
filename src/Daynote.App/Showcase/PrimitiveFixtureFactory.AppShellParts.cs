using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Shapes;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using WpfToolBar = System.Windows.Controls.ToolBar;

namespace Daynote.App.Showcase;

internal static partial class PrimitiveFixtureFactory
{
    private static FrameworkElement WorkspaceSwitch(string[] labels, string selected, string name)
    {
        var strip = TabStrip(name);
        foreach (var label in labels)
        {
            var item = new TabItem { Header = TabHeader(label), IsSelected = label == selected };
            ShowcaseResources.Style(item, "Daynote.Style.WorkspaceViewSwitchItem");
            AutomationProperties.SetName(item, $"{label} workspace view");
            strip.Items.Add(item);
        }
        return strip;
    }

    private static FrameworkElement CalendarWeek()
    {
        var week = new Grid();
        AutomationProperties.SetName(week, "July 12 through July 18 calendar week");
        for (var column = 0; column < 7; column++)
            week.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < 7; index++)
        {
            var dayNumber = 12 + index;
            var day = new WpfToggleButton { Content = dayNumber.ToString(), IsChecked = dayNumber == 16 };
            ShowcaseResources.Style(day, "Daynote.Style.CalendarDay");
            AutomationProperties.SetName(day, $"July {dayNumber}, 2026{(dayNumber == 16 ? ", selected today with note and clipboard" : string.Empty)}");
            if (dayNumber == 16)
                day.Tag = CalendarCues();
            AddColumn(week, day, index);
        }
        return week;
    }

    private static FrameworkElement CalendarCues()
    {
        var cues = ShowcaseUi.Stack(WpfOrientation.Horizontal, "Today, note, and clipboard cues");
        var today = new Ellipse();
        var note = new Ellipse();
        var clipboard = new WpfRectangle();
        ShowcaseResources.Style(today, "Daynote.Style.CalendarCue.Today");
        ShowcaseResources.Style(note, "Daynote.Style.CalendarCue.Note");
        ShowcaseResources.Style(clipboard, "Daynote.Style.CalendarCue.Clipboard");
        cues.Children.Add(today);
        cues.Children.Add(note);
        cues.Children.Add(clipboard);
        return cues;
    }

    private static FrameworkElement NoteTabs(ShowcaseSelection selection)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var strip = TabStrip("Ordered note tabs");
        foreach (var label in new[] { StressTitle(selection.Stress), "Note 2" })
        {
            var tab = new TabItem
            {
                Header = NoteTabHeader(label, label != "Note 2" && selection.Stress == ShowcaseStress.Long),
                IsSelected = label != "Note 2"
            };
            ShowcaseResources.Style(tab, "Daynote.Style.NoteTab");
            AutomationProperties.SetName(tab, label);
            ToolTipService.SetToolTip(tab, label);
            strip.Items.Add(tab);
        }
        AddColumn(row, strip, 0);
        AddColumn(row, IconButton($"Close {StressTitle(selection.Stress)}", "Daynote.Icon.Geometry.Close"), 1);
        AddColumn(row, IconButton("Add note", "Daynote.Icon.Geometry.Add"), 2);
        return row;
    }

    private static FrameworkElement EditorCommands(ShowcaseSelection selection)
    {
        var toolbar = new WpfToolBar();
        ShowcaseResources.Style(toolbar, "Daynote.Style.EditorToolbar");
        AutomationProperties.SetName(toolbar, "Editor toolbar");
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var status = StatusText(
            selection.Stress == ShowcaseStress.Default ? SaveState(selection) : StressStatus(selection),
            "Save status");
        var commands = ShowcaseUi.Stack(WpfOrientation.Horizontal, "Markdown formatting commands");
        foreach (var command in MarkdownCommands)
            commands.Children.Add(IconButton(command.Name, command.Geometry));
        AddColumn(row, status, 0);
        AddColumn(row, commands, 1);
        toolbar.Items.Add(row);
        return toolbar;
    }

    private static FrameworkElement ClipboardListItem(string kind, string time, string preview)
    {
        var content = ShowcaseUi.Stack(WpfOrientation.Vertical, $"{kind} clipboard item content");
        content.Children.Add(RoleText($"{kind} | {time}", "Status", "Clipboard item kind and time"));
        var previewText = RoleText(preview, "Label", "Clipboard preview");
        ShowcaseResources.Style(previewText, "Daynote.Style.ClipboardPreview");
        content.Children.Add(previewText);
        var actions = ShowcaseUi.Stack(WpfOrientation.Horizontal, "Clipboard item actions");
        actions.Children.Add(ShowcaseUi.Button(
            "Copy", "Daynote.Style.Button.Ghost", $"Copy clipboard item captured at {time}"));
        actions.Children.Add(ShowcaseUi.Button("Delete", "Daynote.Style.Button.Destructive", "Delete clipboard item"));
        content.Children.Add(actions);
        var item = new ListBoxItem { Content = content, HorizontalContentAlignment = WpfHorizontalAlignment.Stretch };
        ShowcaseResources.Style(item, "Daynote.Style.ClipboardItem");
        AutomationProperties.SetName(item, $"{kind} clipboard item captured at {time}");
        return item;
    }

    private static FrameworkElement StatusContent(string text, string style, string name)
    {
        var content = new ContentControl { Content = StatusText(text, name) };
        ShowcaseResources.Style(content, style);
        AutomationProperties.SetName(content, name);
        return content;
    }

    private static string SaveState(ShowcaseSelection selection) => selection.Page.State switch
    {
        "loading" => "Saving",
        "error" => "Save failed",
        "disabled" => "Read only",
        "empty" => "Not saved yet",
        _ => "Saved"
    };
}
