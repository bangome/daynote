using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Daynote.App.Showcase;

internal static partial class ShowcaseInteractionSequenceRunner
{
    private static DependencyObject? logicalFocusScope;

    private static void ResetSemanticState(
        DependencyObject surface,
        FrameworkElement motionTarget,
        ShowcaseInteractionDefinition definition,
        ShowcaseMotion motion)
    {
        ResetMotion(motionTarget, definition, motion);
        ShowcaseInteractionState.SetValue(surface, definition.StateBefore);
        if (!ShowcaseInteractionBehavior.Reset(
                FindExact(surface, definition.InitiatorAutomationName, definition.InitiatorControlType)))
            throw new InvalidOperationException(
                $"The {definition.FamilyId} fixture action is not bound.");
        Pump(surface.Dispatcher, TimeSpan.FromMilliseconds(1));
    }

    private static string ObserveSemanticState(DependencyObject surface, ShowcaseInteractionDefinition definition) =>
        definition.FamilyId switch
        {
            "app-shell" or "date-header" =>
                ((ToggleButton)FindExact(surface, definition.InitiatorAutomationName, "ToggleButton")).IsChecked == true &&
                ((TextBlock)FindExact(surface, "Selected date heading", "TextBlock")).Text == "Wednesday, July 15"
                    ? "selected-date=2026-07-15" : "selected-date=2026-07-16",
            "note-tab" => ((TabItem)FindExact(surface, "Note 2", "TabItem")).IsSelected
                ? "selected-note=Note 2" : "selected-note=Note 1",
            "editor-toolbar" => ObserveBoldMarkdown(surface),
            "clipboard-item" => Equals(((WpfButton)FindExact(surface, definition.InitiatorAutomationName, "Button")).Content, "Copied")
                ? "copy-status=copied" : "copy-status=ready",
            "sidebar-note-list" => ((ListBoxItem)FindExact(surface, "Note 2", "ListBoxItem")).IsSelected
                ? "selected-row=Note 2" : "selected-row=Note 1",
            "clipboard-drawer" => ((TextBlock)FindExact(surface, "Clipboard drawer state", "TextBlock")).Text == "Drawer open"
                ? "drawer=open" : "drawer=collapsed",
            "search" => Equals(((ListBoxItem)FindExact(surface, definition.InitiatorAutomationName, "ListBoxItem")).Tag, "Opened")
                ? "search-result=opened" : "search-result=selected",
            "status-banner" => ((TextBlock)FindExact(surface, "Success status message", "TextBlock")).Text == "Recovered"
                ? "status=recovered" : "status=recovery-available",
            "consent-panel" => Equals(((WpfButton)FindExact(surface, definition.InitiatorAutomationName, "Button")).Content, "Capture enabled")
                ? "capture=enabled" : "capture=off",
            "settings-row" => Equals(((WpfButton)FindExact(surface, definition.InitiatorAutomationName, "Button")).Content, "On")
                ? "startup=on" : "startup=off",
            _ => throw new InvalidOperationException($"No semantic observation exists for '{definition.FamilyId}'.")
        };

    private static string ObserveBoldMarkdown(DependencyObject surface)
    {
        var editor = (WpfTextBox)FindExact(
            surface, "Markdown editor for Thursday, July 16, Note 1", "TextBox");
        var status = (TextBlock)FindExact(surface, "Save status", "TextBlock");
        return editor.Text.StartsWith("**", StringComparison.Ordinal) &&
               editor.Text.EndsWith("**", StringComparison.Ordinal) &&
               status.Text == "Bold applied"
            ? "format=bold"
            : "format=plain";
    }

    private static bool IsActualScrollProvider(FrameworkElement element) =>
        element is ScrollViewer ||
        ShowcaseWindow.Descendants(element).OfType<ScrollViewer>().Any();

    private static void ProcessPointer(FrameworkElement initiator, ISet<InputEventArgs> stagedInputs)
    {
        var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = initiator };
        var up = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = Mouse.PreviewMouseUpEvent, Source = initiator };
        stagedInputs.Add(down);
        stagedInputs.Add(up);
        InputManager.Current.ProcessInput(down);
        InputManager.Current.ProcessInput(up);
    }

    private static void ProcessKeyboard(FrameworkElement initiator, string keyName, ISet<InputEventArgs> stagedInputs)
    {
        Focus(null, initiator);
        var source = PresentationSource.FromVisual(initiator)
            ?? throw new InvalidOperationException("The keyboard target is not connected to a WPF presentation source.");
        var key = Enum.Parse<Key>(keyName, false);
        var down = new WpfKeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = initiator };
        var up = new WpfKeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        { RoutedEvent = Keyboard.PreviewKeyUpEvent, Source = initiator };
        stagedInputs.Add(down);
        stagedInputs.Add(up);
        InputManager.Current.ProcessInput(down);
        InputManager.Current.ProcessInput(up);
    }

    private static FrameworkElement FindExact(DependencyObject root, string automationName, string? controlType)
    {
        var matches = ShowcaseWindow.Descendants(root).Prepend(root).OfType<FrameworkElement>()
            .Where(element => AutomationProperties.GetName(element) == automationName)
            .Where(element => controlType is null || element.GetType().Name == controlType).ToArray();
        return matches.Length == 1 ? matches[0] : throw new InvalidOperationException(
            $"Expected exactly one {controlType ?? "element"} named '{automationName}', found {matches.Length}.");
    }

    private static void Focus(ShowcaseWindow? window, FrameworkElement target)
    {
        window?.Activate();
        target.Focus();
        Keyboard.Focus(target);
        logicalFocusScope = FocusManager.GetFocusScope(target);
        FocusManager.SetFocusedElement(logicalFocusScope, target);
        Pump(target.Dispatcher, TimeSpan.FromMilliseconds(1));
    }

    private static string FocusName()
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        if (focused is null && logicalFocusScope is not null)
            focused = FocusManager.GetFocusedElement(logicalFocusScope) as DependencyObject;
        return Name(focused);
    }

    private static string Name(DependencyObject? element)
    {
        if (element is null) return "none";
        var name = AutomationProperties.GetName(element);
        return string.IsNullOrWhiteSpace(name) ? element.GetType().Name : name;
    }

    private static string KeyName(Key key) => key == Key.Return ? "Enter" : key.ToString();
    private static string InitiatorName(ShowcaseInteractionDefinition definition, ShowcaseInputModality modality) =>
        modality == ShowcaseInputModality.Keyboard ? definition.KeyboardInitiatorAutomationName : definition.InitiatorAutomationName;

    private static IReadOnlyList<string> AncestorNames(DependencyObject element)
    {
        var names = new List<string>();
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            var name = AutomationProperties.GetName(current);
            if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
        }
        return names;
    }

    private static string Hwnd(IntPtr hwnd) => $"0x{hwnd.ToInt64():X}";
}
