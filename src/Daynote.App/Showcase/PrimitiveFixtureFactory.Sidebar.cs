using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace Daynote.App.Showcase;

internal static partial class PrimitiveFixtureFactory
{
    private static FrameworkElement SidebarNoteList(ShowcaseSelection selection)
    {
        var body = ShowcaseUi.Stack(WpfOrientation.Vertical, "Sidebar note list content");
        body.Children.Add(ShowcaseUi.Button("Today", "Daynote.Style.Button.Ghost", "Go to today"));
        var list = new WpfListBox { HorizontalContentAlignment = WpfHorizontalAlignment.Stretch };
        AutomationProperties.SetName(list, "Sidebar note list");
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        var titles = selection.Page.State == "empty"
            ? new[] { StressTitle(selection.Stress) }
            : new[] { StressTitle(selection.Stress), "Note 2" };
        foreach (var title in titles)
        {
            var row = new ListBoxItem
            {
                Content = RoleText(title, "Label", $"{title} sidebar row title"),
                IsSelected = title != "Note 2"
            };
            ShowcaseResources.Style(row, "Daynote.Style.SidebarNoteRow");
            AutomationProperties.SetName(row, title);
            ToolTipService.SetToolTip(row, title);
            ShowcaseFocus.SetIsPreferred(row, title != "Note 2");
            list.Items.Add(row);
        }
        body.Children.Add(list);
        body.Children.Add(IconButton("Add note", "Daynote.Icon.Geometry.Add"));
        if (selection.Page.Id == "wide.sidebar-note-list.default" &&
            selection.Stress == ShowcaseStress.Default)
        {
            ShowcaseInteractionBehavior.BindSidebarNoteList(
                (ListBoxItem)list.Items[0],
                (ListBoxItem)list.Items[1]);
        }
        var region = ShowcaseUi.Stack(WpfOrientation.Vertical, "Sidebar note list region");
        region.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "Daynote.Brush.Surface.Secondary");
        region.Children.Add(body);
        return WithStateCue(region, selection);
    }

    private static FrameworkElement ClipboardDrawer(ShowcaseSelection selection)
    {
        var body = ShowcaseUi.Stack(WpfOrientation.Vertical, "Clipboard drawer content");
        var toggle = ShowcaseUi.Button("Clipboard", "Daynote.Style.Button.Secondary", "Toggle clipboard drawer", true);
        var state = StatusText("Drawer collapsed", "Clipboard drawer state");
        var panelContent = ShowcaseUi.Stack(WpfOrientation.Vertical, "Clipboard drawer body");
        panelContent.Children.Add(state);
        panelContent.Children.Add(selection.Page.State == "empty"
            ? Patterns(selection)
            : ClipboardRegion(selection, "Clipboard"));
        var panel = ShowcaseUi.Panel(
            selection.Page.Layout == ShowcaseLayout.Regular
                ? "Daynote.Style.ClipboardDrawer.Overlay"
                : "Daynote.Style.ClipboardDrawer.Inline",
            "Clipboard drawer panel",
            panelContent);
        body.Children.Add(toggle);
        body.Children.Add(panel);
        if (selection.Page.Id is "wide.clipboard-drawer.default" or "wide.clipboard-drawer.active" &&
            selection.Stress == ShowcaseStress.Default)
        {
            ShowcaseInteractionBehavior.BindClipboardDrawer(toggle, state, panel);
            if (selection.Page.State != "empty")
                ShowcaseInteractionBehavior.BindClipboardCopy(panel);
        }
        return WithStateCue(body, selection);
    }
}
