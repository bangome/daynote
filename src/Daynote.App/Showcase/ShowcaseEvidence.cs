using System.Collections;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using WpfControl = System.Windows.Controls.Control;

namespace Daynote.App.Showcase;

public sealed record ShowcaseMotionProfile(string DurationResourceKey, string EasingResourceKey, bool Translates)
{
    public static ShowcaseMotionProfile For(string primitiveId) => primitiveId switch
    {
        "app-shell" or "date-header" => new("Daynote.Motion.Scope", "Daynote.Motion.Scope.Easing", false),
        "note-tab" or "editor-toolbar" or "clipboard-item" or "status-banner" or "settings-row" or "sidebar-note-list" =>
            new("Daynote.Motion.Micro", "Daynote.Motion.Micro.Easing", false),
        "search" or "clipboard-drawer" => new("Daynote.Motion.Panel", "Daynote.Motion.Panel.Easing", true),
        "consent-panel" => new("Daynote.Motion.Panel", "Daynote.Motion.Panel.Easing", false),
        _ => new("Daynote.Motion.Instant", string.Empty, false)
    };
}

public static class ShowcaseMotionTarget
{
    public static readonly DependencyProperty IsTargetProperty = DependencyProperty.RegisterAttached(
        "IsTarget", typeof(bool), typeof(ShowcaseMotionTarget), new PropertyMetadata(false));

    public static readonly DependencyProperty SampleTimeProperty = DependencyProperty.RegisterAttached(
        "SampleTime", typeof(TimeSpan), typeof(ShowcaseMotionTarget), new PropertyMetadata(TimeSpan.Zero));

    public static readonly DependencyProperty IsPreActionProperty = DependencyProperty.RegisterAttached(
        "IsPreAction", typeof(bool), typeof(ShowcaseMotionTarget), new PropertyMetadata(false));

    public static void SetIsTarget(DependencyObject target, bool value) => target.SetValue(IsTargetProperty, value);
    public static bool GetIsTarget(DependencyObject target) => (bool)target.GetValue(IsTargetProperty);
    public static void SetSampleTime(DependencyObject target, TimeSpan value) => target.SetValue(SampleTimeProperty, value);
    public static TimeSpan GetSampleTime(DependencyObject target) => (TimeSpan)target.GetValue(SampleTimeProperty);
    public static void SetIsPreAction(DependencyObject target, bool value) => target.SetValue(IsPreActionProperty, value);
    public static bool GetIsPreAction(DependencyObject target) => (bool)target.GetValue(IsPreActionProperty);
}

internal sealed record ShowcaseEvidenceTargets(FrameworkElement State, FrameworkElement Motion);

internal static class ShowcaseEvidence
{
    public static ShowcaseEvidenceTargets Bind(FrameworkElement specimen, ShowcasePage page)
    {
        var elements = Descendants(specimen).OfType<FrameworkElement>().ToArray();
        var stateTarget = FindStateTarget(elements, page);
        var motionTarget = FindMotionTarget(elements, page, stateTarget);
        ShowcaseForcedState.SetIsTarget(stateTarget, true);
        ShowcaseForcedState.SetState(stateTarget, page.State);
        ShowcaseMotionTarget.SetIsTarget(motionTarget, true);
        ApplyState(stateTarget, page.State);
        return new ShowcaseEvidenceTargets(stateTarget, motionTarget);
    }

    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalChildren(root))
        {
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static FrameworkElement FindStateTarget(IReadOnlyList<FrameworkElement> elements, ShowcasePage page)
    {
        if (page.State == "focus")
        {
            var focusable = elements.FirstOrDefault(element => ShowcaseFocus.GetIsPreferred(element) && element.Focusable);
            if (focusable is not null)
                return focusable;
            focusable = elements.FirstOrDefault(element => element.Focusable && element.IsEnabled);
            if (focusable is not null)
                return focusable;
        }

        if (page.State is "hover" or "active" or "disabled")
        {
            var preferred = elements.FirstOrDefault(ShowcaseFocus.GetIsPreferred);
            if (preferred is not null)
                return preferred;
        }

        return FindPrimitiveTarget(elements, page.PrimitiveId)
               ?? elements.FirstOrDefault(ShowcaseFocus.GetIsPreferred)
               ?? elements[0];
    }

    private static FrameworkElement FindMotionTarget(
        IReadOnlyList<FrameworkElement> elements,
        ShowcasePage page,
        FrameworkElement fallback)
    {
        var automationFragment = page.PrimitiveId switch
        {
            "app-shell" => "AppShell isolated structure specimen",
            "date-header" => "Selected date heading",
            "note-tab" => "Selected note tab",
            "editor-toolbar" => "Save status",
            "clipboard-item" => "Copy clipboard item",
            "sidebar-note-list" => "Sidebar note list",
            "clipboard-drawer" => "Clipboard drawer panel",
            "search" => "Search overlay",
            "status-banner" => "StatusBanner",
            "consent-panel" => "Clipboard capture consent panel",
            "settings-row" => "Toggle Start with Windows",
            _ => string.Empty
        };
        if (string.IsNullOrEmpty(automationFragment))
            return fallback;
        return elements.FirstOrDefault(element =>
                   AutomationProperties.GetName(element).Contains(automationFragment, StringComparison.Ordinal))
               ?? fallback;
    }

    private static void ApplyState(FrameworkElement target, string state)
    {
        if (state == "disabled")
            target.IsEnabled = false;
        if (state == "loading" && target is WpfControl busyControl)
            busyControl.Tag = "Busy";
        if (target is TextBlock text)
        {
            if (state == "error")
                text.SetResourceReference(TextBlock.ForegroundProperty, "Daynote.Brush.Status.Error");
            else if (state == "loading")
                text.SetResourceReference(TextBlock.ForegroundProperty, "Daynote.Brush.Text.Muted");
        }
        if (target is not WpfControl control)
            return;

        if (state == "hover")
            SetBrush(control, WpfControl.BackgroundProperty, IsPrimary(control)
                ? "Daynote.Brush.Accent.600" : "Daynote.Brush.Surface.Hover");
        else if (state == "active")
            SetBrush(control, WpfControl.BackgroundProperty, IsPrimary(control)
                ? "Daynote.Brush.Accent.700" : "Daynote.Brush.Surface.Pressed");
        else if (state == "focus")
        {
            SetBrush(control, WpfControl.BorderBrushProperty, "Daynote.Brush.Focus");
            control.BorderThickness = ShowcaseResources.Get<Thickness>("Daynote.Thickness.Border.Focus");
        }
        else if (state == "error")
            SetBrush(control, WpfControl.BorderBrushProperty, "Daynote.Brush.Status.Error");
    }

    private static FrameworkElement? FindPrimitiveTarget(
        IEnumerable<FrameworkElement> elements,
        string primitiveId)
    {
        var fragment = primitiveId switch
        {
            "app-shell" => "AppShell isolated structure specimen",
            "date-header" => "Selected date heading",
            "note-tab" => "Selected note tab",
            "markdown-editor" => "Markdown editor for",
            "editor-toolbar" => "Save status",
            "clipboard-item" => "Clipboard item",
            "sidebar-note-list" => "Sidebar note list",
            "clipboard-drawer" => "Toggle clipboard drawer",
            "search" => "Search overlay",
            "button" => "Primary button",
            "status-banner" => "StatusBanner",
            "consent-panel" => "Clipboard capture consent panel",
            "settings-row" => "Settings row",
            "tray-menu" => "Tray menu representation",
            "patterns" => "content pattern",
            _ => string.Empty
        };
        if (string.IsNullOrEmpty(fragment))
            return null;
        return elements.FirstOrDefault(element =>
            AutomationProperties.GetName(element).Contains(fragment, StringComparison.Ordinal));
    }

    private static bool IsPrimary(WpfControl control) =>
        AutomationProperties.GetName(control).Equals("Primary button", StringComparison.Ordinal) ||
        AutomationProperties.GetName(control).Contains("Enable local clipboard", StringComparison.Ordinal);

    private static void SetBrush(WpfControl control, DependencyProperty property, string resourceKey) =>
        control.SetResourceReference(property, resourceKey);

    private static IEnumerable<DependencyObject> LogicalChildren(DependencyObject root)
    {
        var children = LogicalTreeHelper.GetChildren(root);
        foreach (var child in children)
        {
            if (child is DependencyObject dependencyObject)
                yield return dependencyObject;
        }

        if (root is ItemsControl itemsControl)
        {
            foreach (var item in (IEnumerable)itemsControl.Items)
            {
                if (item is DependencyObject dependencyObject)
                    yield return dependencyObject;
            }
        }
    }
}
