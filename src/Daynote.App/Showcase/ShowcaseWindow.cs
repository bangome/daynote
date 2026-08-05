using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfControl = System.Windows.Controls.Control;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Daynote.App.Showcase;

internal sealed class ShowcaseWindow : Window
{
    private readonly ShowcaseInteractionLogger? _interactionLogger;

    public ShowcaseWindow(FrameworkElement content, ShowcasePage page, double width, double height, bool hold)
    {
        Title = $"Daynote Primitive Showcase - {page.Id}";
        Content = content;
        Width = width;
        Height = height;
        WindowStyle = hold ? WindowStyle.SingleBorderWindow : WindowStyle.None;
        ResizeMode = hold ? ResizeMode.CanResize : ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = hold;
        ShowActivated = hold;
        AutomationProperties.SetName(this, $"Daynote primitive showcase window: {page.AutomationName}");
        _interactionLogger = ShowcaseInteractionLogger.TryAttachFromProcessArguments(content);
        PreviewKeyDown += OnPreviewKeyDown;
        Closed += (_, _) => _interactionLogger?.Dispose();
        Loaded += (_, _) =>
        {
            if (page.State == "focus")
                FocusPreferred();
            else
                Keyboard.ClearFocus();
        };
    }

    private void OnPreviewKeyDown(object sender, WpfKeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.F6)
        {
            CycleFocus(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            var search = Descendants(this)
                .OfType<WpfTextBox>()
                .FirstOrDefault(element => AutomationProperties.GetName(element).Contains("Search", StringComparison.Ordinal));
            search?.Focus();
            eventArgs.Handled = search is not null;
            return;
        }

        if (eventArgs.Key == Key.Escape)
        {
            FocusPreferred();
            eventArgs.Handled = true;
        }
    }

    private void CycleFocus(int direction)
    {
        var candidates = Descendants(this)
            .OfType<WpfControl>()
            .Where(control => control.Focusable && control.IsEnabled && control.IsVisible && IsFocusCandidate(control))
            .ToArray();
        if (candidates.Length == 0)
            return;
        var current = Array.IndexOf(candidates, Keyboard.FocusedElement);
        var next = current < 0 ? 0 : (current + direction + candidates.Length) % candidates.Length;
        candidates[next].Focus();
    }

    private static bool IsFocusCandidate(WpfControl control) =>
        ShowcaseFocus.GetIsPreferred(control) ||
        control is WpfTextBox or System.Windows.Controls.Primitives.ButtonBase or
            System.Windows.Controls.ListBoxItem or System.Windows.Controls.Primitives.Thumb or
            System.Windows.Controls.MenuItem;

    private void FocusPreferred()
    {
        var target = Descendants(this)
            .OfType<FrameworkElement>()
            .FirstOrDefault(element => ShowcaseForcedState.GetIsTarget(element) && element.Focusable)
            ?? Descendants(this)
                .OfType<FrameworkElement>()
                .FirstOrDefault(element => ShowcaseFocus.GetIsPreferred(element) && element.Focusable);
        if (target is not null)
            Keyboard.Focus(target);
    }

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
