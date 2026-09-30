using System.Windows;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The two things the design-B control styles need that a WPF <see cref="System.Windows.Controls.Control"/>
/// has no property for: a corner radius, and the "this one is on" flag the styles trigger on.
/// </summary>
/// <remarks>
/// Avalonia says both inline — a <c>CornerRadius</c> setter on the button and a
/// <c>Classes.active="{Binding …}"</c> on the call site. WPF has neither, and the usual workarounds
/// each cost something: a template per radius multiplies the templates, and <c>Tag</c> is one slot
/// that the note rows already use for something else. Two attached properties keep the call sites
/// reading the way the Avalonia ones do.
/// </remarks>
public static class DeskVisual
{
    /// <summary>The radius the bare button template rounds its border to.</summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius",
        typeof(CornerRadius),
        typeof(DeskVisual),
        new FrameworkPropertyMetadata(default(CornerRadius)));

    /// <summary>
    /// The element is in its active state: the selected nav item, the toggled header tool, a ticked
    /// check. The styles trigger on this, so a call site says which binding decides it.
    /// </summary>
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
        "IsActive",
        typeof(bool),
        typeof(DeskVisual),
        new FrameworkPropertyMetadata(false));

    public static CornerRadius GetCornerRadius(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (CornerRadius)element.GetValue(CornerRadiusProperty);
    }

    public static void SetCornerRadius(DependencyObject element, CornerRadius value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(CornerRadiusProperty, value);
    }

    public static bool GetIsActive(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsActiveProperty);
    }

    public static void SetIsActive(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsActiveProperty, value);
    }
}
