using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfToggleButton = System.Windows.Controls.Primitives.ToggleButton;

namespace Daynote.App.Showcase;

internal static partial class ShowcaseInteractionBehavior
{
    private static readonly Key[] ActivationKeys = [Key.Enter, Key.Return, Key.Space];

    internal static void BindAppShellWorkspace(DependencyObject shellRoot)
    {
        var day15 = Find<WpfToggleButton>(shellRoot, "July 15, 2026");
        var day16 = Find<WpfToggleButton>(shellRoot, "July 16, 2026, selected today with note and clipboard");
        var heading = Find<TextBlock>(shellRoot, "Selected date heading");
        var note1 = Find<TabItem>(shellRoot, "Note 1");
        var note2 = Find<TabItem>(shellRoot, "Note 2");
        var sidebarNote1 = Find<ListBoxItem>(shellRoot, "Note 1");
        var sidebarNote2 = Find<ListBoxItem>(shellRoot, "Note 2");
        var bold = Find<WpfButton>(shellRoot, "Bold");
        var saveStatus = Find<TextBlock>(shellRoot, "Save status");
        var editor = Find<WpfTextBox>(shellRoot, "Markdown editor for Thursday, July 16, Note 1");

        Bind(
            day15,
            day15,
            ActivationKeys,
            "select-date",
            "Click",
            () =>
            {
                day15.IsChecked = false;
                day16.IsChecked = true;
                heading.Text = "Thursday, July 16";
            },
            () =>
            {
                day16.IsChecked = false;
                day15.IsChecked = true;
                heading.Text = "Wednesday, July 15";
                MoveFocus(day15);
            });

        Bind(
            note2,
            note1,
            [Key.Right],
            "select-note",
            "Selected",
            () =>
            {
                note2.IsSelected = false;
                note1.IsSelected = true;
                sidebarNote2.IsSelected = false;
                sidebarNote1.IsSelected = true;
            },
            () =>
            {
                note1.IsSelected = false;
                note2.IsSelected = true;
                sidebarNote1.IsSelected = false;
                sidebarNote2.IsSelected = true;
                MoveFocus(note2);
            });

        var originalBody = editor.Text;
        Bind(
            bold,
            bold,
            ActivationKeys,
            "format-bold",
            "Click",
            () =>
            {
                editor.Text = originalBody;
                saveStatus.Text = "Saved";
            },
            () =>
            {
                editor.Text = $"**{originalBody}**";
                saveStatus.Text = "Bold applied";
                MoveFocus(bold);
            });

    }

    internal static void BindClipboardCopy(DependencyObject root)
    {
        var copy = Find<WpfButton>(root, "Copy clipboard item captured at 10:24");
        Bind(
            copy,
            copy,
            ActivationKeys,
            "copy-item",
            "Click",
            () =>
            {
                copy.Content = "Copy";
                AutomationProperties.SetItemStatus(copy, "Ready");
            },
            () =>
            {
                copy.Content = "Copied";
                AutomationProperties.SetItemStatus(copy, "Copied");
                MoveFocus(copy);
            });
    }

    internal static void BindSidebarNoteList(ListBoxItem row1, ListBoxItem row2) =>
        Bind(
            row2,
            row2,
            [Key.Enter, Key.Return],
            "select-note-row",
            "Selected",
            () =>
            {
                row2.IsSelected = false;
                row1.IsSelected = true;
            },
            () =>
            {
                row1.IsSelected = false;
                row2.IsSelected = true;
                MoveFocus(row2);
            });

    internal static void BindClipboardDrawer(
        WpfButton toggle,
        TextBlock state,
        ContentControl panel) =>
        Bind(
            toggle,
            toggle,
            ActivationKeys,
            "toggle-drawer",
            "Click",
            () =>
            {
                state.Text = "Drawer collapsed";
                AutomationProperties.SetItemStatus(panel, "Collapsed");
            },
            () =>
            {
                state.Text = "Drawer open";
                AutomationProperties.SetItemStatus(panel, "Open");
                MoveFocus(toggle);
            });

    internal static void BindSearchResult(ListBoxItem result) =>
        Bind(
            result,
            result,
            [Key.Enter, Key.Return],
            "open-search-result",
            "Invoked",
            () =>
            {
                result.IsSelected = true;
                result.Tag = null;
                AutomationProperties.SetItemStatus(result, "Selected");
            },
            () =>
            {
                result.Tag = "Opened";
                AutomationProperties.SetItemStatus(result, "Opened");
                MoveFocus(result);
            });

    internal static void BindStatusBanner(
        WpfButton retry,
        TextBlock message,
        ContentControl banner) =>
        Bind(
            retry,
            retry,
            ActivationKeys,
            "retry-status",
            "Click",
            () =>
            {
                message.Text = "Recovery available";
                AutomationProperties.SetItemStatus(banner, "Recovery available");
            },
            () =>
            {
                message.Text = "Recovered";
                AutomationProperties.SetItemStatus(banner, "Recovered");
                MoveFocus(retry);
            });

    internal static void BindConsentPanel(WpfButton enable) =>
        Bind(
            enable,
            enable,
            ActivationKeys,
            "enable-capture",
            "Click",
            () => enable.Content = "Enable capture",
            () =>
            {
                enable.Content = "Capture enabled";
                MoveFocus(enable);
            });

    internal static void BindSettingsRow(WpfButton toggle) =>
        Bind(
            toggle,
            toggle,
            ActivationKeys,
            "toggle-startup",
            "Click",
            () => toggle.Content = "Toggle",
            () =>
            {
                toggle.Content = "On";
                MoveFocus(toggle);
            });

    private static T Find<T>(DependencyObject root, string automationName)
        where T : FrameworkElement
    {
        var matches = ShowcaseEvidence.Descendants(root).OfType<T>()
            .Where(element => AutomationProperties.GetName(element) == automationName)
            .Distinct()
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Expected exactly one {typeof(T).Name} named '{automationName}', found {matches.Length}.");
    }
}
