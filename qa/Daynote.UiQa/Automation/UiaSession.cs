using System.Diagnostics;
using System.Windows.Automation;

namespace Daynote.UiQa.Automation;

/// <summary>
/// Thin Windows UI Automation client wrapper scoped to one product process. It locates the real
/// Daynote window by process id and exposes deterministic find/invoke/set helpers with bounded
/// polling. It drives the shipping product surface (not the showcase host), so the observables it
/// reads are the real app's automation tree.
/// </summary>
public sealed class UiaSession
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly int _processId;

    public UiaSession(int processId)
    {
        _processId = processId;
    }

    /// <summary>Waits for the process's top-level window to appear in the automation tree.</summary>
    public AutomationElement WaitForMainWindow(TimeSpan? timeout = null)
    {
        AutomationElement? window = WaitFor(
            () =>
            {
                Condition condition = new PropertyCondition(AutomationElement.ProcessIdProperty, _processId);
                return AutomationElement.RootElement.FindFirst(TreeScope.Children, condition);
            },
            timeout);

        return window ?? throw new TimeoutException(
            $"No top-level window appeared for process {_processId} within the timeout.");
    }

    /// <summary>Finds a descendant by AutomationId (preferred) then by Name. Returns null if absent.</summary>
    public static AutomationElement? Find(AutomationElement scope, string automationIdOrName)
    {
        Condition byId = new PropertyCondition(AutomationElement.AutomationIdProperty, automationIdOrName);
        AutomationElement? element = scope.FindFirst(TreeScope.Descendants, byId);
        if (element is not null)
        {
            return element;
        }

        Condition byName = new PropertyCondition(AutomationElement.NameProperty, automationIdOrName);
        return scope.FindFirst(TreeScope.Descendants, byName);
    }

    /// <summary>Polls until a descendant with the id/name exists, or throws on timeout.</summary>
    public AutomationElement WaitForElement(AutomationElement scope, string automationIdOrName, TimeSpan? timeout = null)
    {
        AutomationElement? element = WaitFor(() => Find(scope, automationIdOrName), timeout);
        return element ?? throw new TimeoutException(
            $"Element '{automationIdOrName}' did not appear within the timeout.");
    }

    /// <summary>Invokes a control exposing the Invoke pattern (buttons, menu items).</summary>
    public static void Invoke(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out object pattern))
        {
            ((InvokePattern)pattern).Invoke();
            return;
        }

        throw new InvalidOperationException(
            $"Element '{element.Current.Name}' does not support the Invoke pattern.");
    }

    /// <summary>Sets text into a control exposing the Value pattern (editor, search box).</summary>
    public static void SetValue(AutomationElement element, string value)
    {
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern))
        {
            ((ValuePattern)pattern).SetValue(value);
            return;
        }

        throw new InvalidOperationException(
            $"Element '{element.Current.Name}' does not support the Value pattern.");
    }

    public static string ReadValue(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern))
        {
            return ((ValuePattern)pattern).Current.Value;
        }

        return element.Current.Name;
    }

    /// <summary>Counts descendants of a control type (e.g. how many note tabs / clipboard rows).</summary>
    public static int CountByControlType(AutomationElement scope, ControlType controlType)
    {
        Condition condition = new PropertyCondition(AutomationElement.ControlTypeProperty, controlType);
        return scope.FindAll(TreeScope.Descendants, condition).Count;
    }

    private static T? WaitFor<T>(Func<T?> probe, TimeSpan? timeout)
        where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        TimeSpan limit = timeout ?? DefaultTimeout;
        while (stopwatch.Elapsed < limit)
        {
            T? value = probe();
            if (value is not null)
            {
                return value;
            }

            Thread.Sleep(PollInterval);
        }

        return null;
    }
}
