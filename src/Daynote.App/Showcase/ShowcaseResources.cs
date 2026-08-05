using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Daynote.App.Showcase;

public static class ShowcaseResources
{
    public const string AggregateUri = "/Daynote.App;component/Themes/Daynote.Resources.xaml";
    public const string HighContrastUri = "/Daynote.App;component/Themes/Daynote.Resources.HighContrast.xaml";

    public static void Load(System.Windows.Application application, bool highContrast)
    {
        application.Resources.MergedDictionaries.Add(Dictionary(highContrast ? HighContrastUri : AggregateUri));
    }

    public static T Get<T>(string key)
    {
        var value = System.Windows.Application.Current.TryFindResource(key)
            ?? throw new InvalidOperationException($"Required design resource '{key}' is not loaded.");
        if (value is not T typed)
            throw new InvalidOperationException($"Design resource '{key}' is {value.GetType().Name}, not {typeof(T).Name}.");
        return typed;
    }

    public static void Style(FrameworkElement element, string key) =>
        element.SetResourceReference(FrameworkElement.StyleProperty, key);

    public static void Background(System.Windows.Controls.Control control, string key) =>
        control.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, key);

    public static void Background(Border border, string key) =>
        border.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, key);

    public static void Border(Border border, string key) =>
        border.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, key);

    public static void Foreground(System.Windows.Controls.Control control, string key) =>
        control.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, key);

    private static ResourceDictionary Dictionary(string source) => new()
    {
        Source = new Uri(source, UriKind.Relative)
    };
}

public sealed class DaynoteMotionPolicy
{
    private DaynoteMotionPolicy(bool reducedMotion) => ReducedMotion = reducedMotion;

    public bool ReducedMotion { get; }

    public static DaynoteMotionPolicy FromSystem() => new(!SystemParameters.ClientAreaAnimation);
    public static DaynoteMotionPolicy ForShowcase(bool reducedMotion) => new(reducedMotion);

    public Duration Duration(string resourceKey) => ReducedMotion
        ? ShowcaseResources.Get<Duration>("Daynote.Motion.Instant")
        : ShowcaseResources.Get<Duration>(resourceKey);

    public TimeSpan EvidenceMidpoint =>
        ShowcaseResources.Get<Duration>("Daynote.Motion.Evidence.Midpoint").TimeSpan;
}

public static partial class ShowcaseForcedState
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(string), typeof(ShowcaseForcedState),
        new PropertyMetadata("default"));

    public static readonly DependencyProperty IsTargetProperty = DependencyProperty.RegisterAttached(
        "IsTarget", typeof(bool), typeof(ShowcaseForcedState), new PropertyMetadata(false));

    public static void SetState(DependencyObject target, string value) => target.SetValue(StateProperty, value);
    public static string GetState(DependencyObject target) => (string)target.GetValue(StateProperty);

    public static void SetIsTarget(DependencyObject target, bool value) => target.SetValue(IsTargetProperty, value);
    public static bool GetIsTarget(DependencyObject target) => (bool)target.GetValue(IsTargetProperty);

    public static Border Apply(FrameworkElement specimen, ShowcasePage page)
    {
        var frame = new Border { Child = specimen };
        frame.SetResourceReference(FrameworkElement.MarginProperty, "Daynote.Inset.Pane.Compact");
        frame.SetResourceReference(Border.PaddingProperty, "Daynote.Inset.Pane.Regular");
        frame.SetResourceReference(Border.CornerRadiusProperty, "Daynote.Radius.Panel");
        frame.BorderThickness = UniformThickness("Daynote.Border.Thin");
        ShowcaseResources.Background(frame, "Daynote.Brush.Surface.Primary");
        ShowcaseResources.Border(frame, "Daynote.Brush.Border.Control");
        System.Windows.Automation.AutomationProperties.SetName(frame, page.AutomationName);
        System.Windows.Automation.AutomationProperties.SetHelpText(
            frame, $"forced-state={page.State}; focus-owner={page.FocusOwner}; scroll-owner={page.ScrollOwner}");
        return frame;
    }

    private static Thickness UniformThickness(string key) => new(ShowcaseResources.Get<double>(key));
}

public static class ShowcaseFocus
{
    public static readonly DependencyProperty IsPreferredProperty = DependencyProperty.RegisterAttached(
        "IsPreferred", typeof(bool), typeof(ShowcaseFocus), new PropertyMetadata(false));

    public static void SetIsPreferred(DependencyObject target, bool value) => target.SetValue(IsPreferredProperty, value);
    public static bool GetIsPreferred(DependencyObject target) => (bool)target.GetValue(IsPreferredProperty);
}
