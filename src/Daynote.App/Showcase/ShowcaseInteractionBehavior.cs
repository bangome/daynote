using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using WpfButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Daynote.App.Showcase;

internal static partial class ShowcaseInteractionBehavior
{
    internal const string DisableHandlersEnvironmentVariable =
        "DAYNOTE_SHOWCASE_DISABLE_INTERACTION_HANDLERS";

    private static readonly DependencyProperty ActionProperty = DependencyProperty.RegisterAttached(
        "Action",
        typeof(BoundAction),
        typeof(ShowcaseInteractionBehavior));

    private static readonly DependencyProperty ClickBridgeProperty = DependencyProperty.RegisterAttached(
        "ClickBridge",
        typeof(BoundAction),
        typeof(ShowcaseInteractionBehavior));

    private static long receiptSequence;

    internal static bool HandlersSuppressed =>
        string.Equals(
            Environment.GetEnvironmentVariable(DisableHandlersEnvironmentVariable),
            "1",
            StringComparison.Ordinal);

    internal static bool Reset(FrameworkElement initiator)
    {
        if (initiator.GetValue(ActionProperty) is not BoundAction action)
            return false;
        action.Reset();
        return true;
    }

    internal static ShowcaseSequenceActionReceipt? GetLastReceipt(FrameworkElement initiator) =>
        initiator.GetValue(ActionProperty) is BoundAction action
            ? action.LastReceipt
            : null;

    private static void Bind(
        FrameworkElement pointerInitiator,
        FrameworkElement keyboardInitiator,
        IReadOnlyList<Key> keyboardKeys,
        string semanticAction,
        string controlEvent,
        Action reset,
        Action execute)
    {
        var action = new BoundAction(semanticAction, controlEvent, reset, execute);
        pointerInitiator.SetValue(ActionProperty, action);
        keyboardInitiator.SetValue(ActionProperty, action);
        action.Reset();
        if (HandlersSuppressed)
            return;
        action.AttachPointer(pointerInitiator);
        action.AttachKeyboard(keyboardInitiator, keyboardKeys);
    }

    private static void MoveFocus(FrameworkElement target)
    {
        target.Focus();
        Keyboard.Focus(target);
        if (FocusManager.GetFocusScope(target) is { } scope)
            FocusManager.SetFocusedElement(scope, target);
    }

    private sealed class BoundAction(
        string semanticAction,
        string controlEvent,
        Action reset,
        Action execute)
    {
        internal ShowcaseSequenceActionReceipt? LastReceipt { get; private set; }

        internal void AttachPointer(FrameworkElement initiator) =>
            initiator.PreviewMouseUp += (_, args) =>
            {
                if (args.ChangedButton != MouseButton.Left)
                    return;
                Invoke(initiator, args);
            };

        internal void AttachKeyboard(FrameworkElement initiator, IReadOnlyList<Key> keys) =>
            initiator.PreviewKeyDown += (_, args) =>
            {
                if (!keys.Contains(args.Key))
                    return;
                Invoke(initiator, args);
            };

        internal void Reset()
        {
            LastReceipt = null;
            reset();
        }

        private void Invoke(FrameworkElement initiator, RoutedEventArgs input)
        {
            if (initiator is WpfButtonBase button)
            {
                EnsureClickBridge(button);
                button.RaiseEvent(new RoutedEventArgs(WpfButtonBase.ClickEvent, button));
            }
            else
            {
                ExecuteAndRecord(initiator, controlEvent);
            }
            input.Handled = true;
        }

        private void EnsureClickBridge(WpfButtonBase button)
        {
            if (Equals(button.GetValue(ClickBridgeProperty), this))
                return;
            button.SetValue(ClickBridgeProperty, this);
            button.Click += (_, _) => ExecuteAndRecord(button, "Click");
        }

        private void ExecuteAndRecord(FrameworkElement source, string observedControlEvent)
        {
            execute();
            LastReceipt = new ShowcaseSequenceActionReceipt(
                semanticAction,
                observedControlEvent,
                AutomationProperties.GetName(source),
                Interlocked.Increment(ref receiptSequence),
                DateTimeOffset.UtcNow);
        }
    }
}
