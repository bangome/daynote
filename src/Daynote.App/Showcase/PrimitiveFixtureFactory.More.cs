using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfMenu = System.Windows.Controls.Menu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Daynote.App.Showcase;

internal static partial class PrimitiveFixtureFactory
{
    private static FrameworkElement ClipboardItem(ShowcaseSelection selection)
    {
        if (selection.Page.State == "empty")
            return Patterns(selection);

        var content = ShowcaseUi.Stack(WpfOrientation.Vertical, "Clipboard item content");
        content.Children.Add(RoleText("Text item | 10:24", "Status", "Clipboard item kind and time"));
        var preview = RoleText(StressClipboardPreview(selection.Stress), "Label", "Private preview withheld from global announcements");
        ShowcaseResources.Style(preview, "Daynote.Style.ClipboardPreview");
        content.Children.Add(preview);
        var actions = ShowcaseUi.Stack(WpfOrientation.Horizontal, "Clipboard item actions");
        actions.Children.Add(ShowcaseUi.Button("Copy", "Daynote.Style.Button.Ghost", "Copy clipboard item", true));
        actions.Children.Add(ShowcaseUi.Button("Delete", "Daynote.Style.Button.Destructive", "Delete clipboard item"));
        content.Children.Add(actions);
        var item = new ListBoxItem { Content = content };
        ShowcaseResources.Style(item, "Daynote.Style.ClipboardItem");
        AutomationProperties.SetName(item, "Clipboard item");
        return WithStateCue(item, selection);
    }

    private static FrameworkElement Search(ShowcaseSelection selection)
    {
        var content = ShowcaseUi.Stack(WpfOrientation.Vertical, "Search overlay content");
        var box = new WpfTextBox { Text = selection.Page.State == "empty" ? "No match" : StressQuery(selection.Stress) };
        ShowcaseResources.Style(box, "Daynote.Style.SearchBox");
        AutomationProperties.SetName(box, "Search notes and clipboard");
        content.Children.Add(box);
        if (selection.Page.State is "empty" or "error" or "loading")
            content.Children.Add(RoleText(StressStatus(selection), "Status", "Search state"));
        else
        {
            var result = new ListBoxItem
            {
                Content = RoleText($"Note | July 16 | {ShowcaseUi.StressText(selection.Stress)}", "Label", "Search result snippet"),
                IsSelected = true
            };
            ShowcaseResources.Style(result, "Daynote.Style.SearchResult");
            AutomationProperties.SetName(result, "Selected search result, note, July 16");
            ShowcaseFocus.SetIsPreferred(result, true);
            if (selection.Page.Id == "wide.search.default")
                ShowcaseInteractionBehavior.BindSearchResult(result);
            var results = new WpfListBox { HorizontalContentAlignment = WpfHorizontalAlignment.Stretch };
            AutomationProperties.SetName(results, "Search result list");
            results.Items.Add(result);
            content.Children.Add(results);
        }
        ShowcaseFocus.SetIsPreferred(box, selection.Page.State != "focus");
        var overlay = ShowcaseUi.Panel("Daynote.Style.SearchOverlay", "Search overlay", content);
        return WithStateCue(overlay, selection);
    }

    private static FrameworkElement Buttons(ShowcaseSelection selection)
    {
        var group = ShowcaseUi.Stack(WpfOrientation.Horizontal, "Button variants");
        group.Children.Add(ShowcaseUi.Button("Primary", "Daynote.Style.Button.Primary", "Primary button", true));
        group.Children.Add(ShowcaseUi.Button("Secondary", "Daynote.Style.Button.Secondary", "Secondary button"));
        group.Children.Add(ShowcaseUi.Button("Ghost", "Daynote.Style.Button.Ghost", "Ghost button"));
        group.Children.Add(ShowcaseUi.Button("Delete", "Daynote.Style.Button.Destructive", "Destructive button"));
        group.Children.Add(IconButton("Settings icon button", "Daynote.Icon.Geometry.Settings", "Daynote.Style.IconButton.Secondary"));
        return WithStateCue(group, selection);
    }

    private static FrameworkElement StatusBanner(ShowcaseSelection selection)
    {
        if (selection.Page.State == "empty")
            return RoleText("StatusBanner is intentionally unmounted.", "Status", "Unmounted status banner state");

        var variant = selection.Page.State == "error" ? "Error" : selection.Page.State == "loading" ? "Info" : "Success";
        var body = new Grid { HorizontalAlignment = WpfHorizontalAlignment.Stretch };
        body.SetResourceReference(FrameworkElement.MaxWidthProperty, "Daynote.Size.EditorMeasure.Max");
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var message = StatusText(StressStatus(selection), $"{variant} status message");
        var retry = ShowcaseUi.Button("Retry", "Daynote.Style.Button.Secondary", "Retry status action", true);
        AddColumn(body, message, 0);
        AddColumn(body, retry, 1);
        var banner = ShowcaseUi.Panel($"Daynote.Style.StatusBanner.{variant}", $"{variant} StatusBanner", body);
        if (selection.Page.Id == "wide.status-banner.default")
            ShowcaseInteractionBehavior.BindStatusBanner(retry, message, banner);
        return WithStateCue(banner, selection);
    }

    private static FrameworkElement ConsentPanel(ShowcaseSelection selection)
    {
        var body = ShowcaseUi.Stack(WpfOrientation.Vertical, "Consent panel content");
        body.Children.Add(RoleText("Local clipboard capture", "PaneTitle", "Consent heading"));
        body.Children.Add(RoleText(
            "Capture is off until explicit consent. Future text and image items remain local.",
            "Body", "Consent explanation"));
        var actions = ShowcaseUi.Stack(WpfOrientation.Horizontal, "Consent decisions");
        var enable = ShowcaseUi.Button("Enable capture", "Daynote.Style.Button.Primary", "Enable local clipboard capture", true);
        if (selection.Page.Id == "wide.consent-panel.default")
            ShowcaseInteractionBehavior.BindConsentPanel(enable);
        actions.Children.Add(enable);
        actions.Children.Add(ShowcaseUi.Button("Not now", "Daynote.Style.Button.Secondary", "Decline clipboard capture"));
        body.Children.Add(actions);
        return WithStateCue(ShowcaseUi.Panel("Daynote.Style.ConsentPanel", "Clipboard capture consent panel", body), selection);
    }

    private static FrameworkElement SettingsRow(ShowcaseSelection selection)
    {
        var row = ShowcaseUi.Stack(WpfOrientation.Horizontal, "Settings row content");
        row.Children.Add(IconButton("Start with Windows setting", "Daynote.Icon.Geometry.Settings", "Daynote.Style.IconButton.Secondary"));
        row.Children.Add(RoleText("Start with Windows", "Label", "Start with Windows setting label"));
        row.Children.Add(RoleText(StressStatus(selection), "Status", "Setting description and state"));
        var toggle = ShowcaseUi.Button("Toggle", "Daynote.Style.Button.Secondary", "Toggle Start with Windows", true);
        if (selection.Page.Id == "wide.settings-row.default")
            ShowcaseInteractionBehavior.BindSettingsRow(toggle);
        row.Children.Add(toggle);
        return WithStateCue(ShowcaseUi.Panel("Daynote.Style.SettingsRow", "Settings row", row), selection);
    }

    private static FrameworkElement TrayMenu(ShowcaseSelection selection)
    {
        var menu = new WpfMenu();
        ShowcaseResources.Style(menu, "Daynote.Style.TrayMenu");
        AutomationProperties.SetName(menu, "Tray menu representation");
        foreach (var label in new[] { "Show Daynote", "Pause capture", "Settings", "Quit" })
        {
            var item = new WpfMenuItem { Header = label };
            ShowcaseResources.Style(item, "Daynote.Style.TrayMenuItem");
            AutomationProperties.SetName(item, label);
            ShowcaseFocus.SetIsPreferred(item, label == "Show Daynote");
            menu.Items.Add(item);
        }
        return WithStateCue(menu, selection);
    }

    private static FrameworkElement Patterns(ShowcaseSelection selection)
    {
        var effectiveState = selection.Page.State is "loading" or "error" ? selection.Page.State : "empty";
        var style = effectiveState switch
        {
            "loading" => "Daynote.Style.LoadingPattern",
            "error" => "Daynote.Style.ErrorPattern",
            _ => "Daynote.Style.EmptyPattern"
        };
        var body = ShowcaseUi.Stack(WpfOrientation.Vertical, $"{effectiveState} pattern");
        body.Children.Add(RoleText(effectiveState.ToUpperInvariant(), "ItemTitle", $"{effectiveState} pattern heading"));
        body.Children.Add(RoleText(StressStatus(selection), "Body", $"{effectiveState} pattern explanation"));
        if (effectiveState == "error")
            body.Children.Add(ShowcaseUi.Button("Retry", "Daynote.Style.Button.Secondary", "Retry failed operation", true));
        var panel = ShowcaseUi.Panel(style, $"{effectiveState} content pattern", body);
        return WithStateCue(panel, selection);
    }
}
