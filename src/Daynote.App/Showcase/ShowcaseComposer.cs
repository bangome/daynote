using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfButton = System.Windows.Controls.Button;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace Daynote.App.Showcase;

public sealed record ShowcaseSelection(
    ShowcasePage Page,
    ShowcasePalette Palette,
    ShowcaseMotion Motion,
    ShowcaseStress Stress,
    ShowcaseFrame Frame);

public sealed class ShowcaseComposer
{
    public FrameworkElement Compose(ShowcaseSelection selection)
    {
        var root = new Grid();
        root.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "Daynote.Brush.Canvas");
        AutomationProperties.SetName(root, $"Daynote primitive showcase: {selection.Page.AutomationName}");
        AutomationProperties.SetHelpText(root, Metadata(selection));
        KeyboardNavigation.SetTabNavigation(root, KeyboardNavigationMode.Cycle);
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = CreateHeader(selection);
        var specimen = PrimitiveFixtureFactory.Create(selection);
        var targets = ShowcaseEvidence.Bind(specimen, selection.Page);
        var frame = ShowcaseForcedState.Apply(specimen, selection.Page);
        var footer = CreateFooter(selection);
        Grid.SetRow(header, 0);
        Grid.SetRow(frame, 1);
        Grid.SetRow(footer, 2);
        root.Children.Add(header);
        root.Children.Add(frame);
        root.Children.Add(footer);
        ApplyMotion(targets.Motion, selection);
        return root;
    }

    private static FrameworkElement CreateHeader(ShowcaseSelection selection)
    {
        var panel = ShowcaseUi.Stack(WpfOrientation.Vertical, "Showcase command region");
        panel.SetResourceReference(FrameworkElement.MarginProperty, "Daynote.Inset.Pane.Compact");
        panel.Children.Add(ShowcaseUi.Text(selection.Page.PrimitiveName, "Daynote.Type.PaneTitle", "Primitive name"));
        panel.Children.Add(ShowcaseUi.Text(
            $"{selection.Page.Id} | {selection.Palette} | {selection.Motion} | {selection.Stress}",
            "Daynote.Type.Status", "Deterministic showcase selection"));
        return panel;
    }

    private static FrameworkElement CreateFooter(ShowcaseSelection selection)
    {
        var footer = ShowcaseUi.Text(
            $"Forced {selection.Page.State}. Declared focus target: {selection.Page.FocusOwner}. " +
            $"Declared scroll owner: {selection.Page.ScrollOwner}. Interaction not executed.",
            "Daynote.Type.Status", "Showcase state metadata");
        footer.SetResourceReference(FrameworkElement.MarginProperty, "Daynote.Inset.Pane.Compact");
        return footer;
    }

    private static void ApplyMotion(FrameworkElement target, ShowcaseSelection selection)
    {
        var profile = ShowcaseMotionProfile.For(selection.Page.PrimitiveId);
        var sample = ShowcaseMotionSampler.Sample(profile, selection.Motion, selection.Frame);
        ShowcaseMotionTarget.SetSampleTime(target, sample.Time);
        ShowcaseMotionTarget.SetIsPreAction(target, sample.IsPreAction);
        target.Opacity = sample.Opacity;
        if (!profile.Translates)
            return;
        var transform = target.RenderTransform switch
        {
            TranslateTransform existing when !existing.IsFrozen => existing,
            TranslateTransform existing => existing.Clone(),
            _ => new TranslateTransform()
        };
        target.RenderTransform = transform;
        transform.Y = sample.TranslateY;
    }

    private static string Metadata(ShowcaseSelection selection) =>
        $"page={selection.Page.Id}; state={selection.Page.State}; layout={selection.Page.Layout}; " +
        $"palette={selection.Palette}; motion={selection.Motion}; frame={selection.Frame}; stress={selection.Stress}; " +
        $"focus-owner={selection.Page.FocusOwner}; scroll-owner={selection.Page.ScrollOwner}";

}

internal sealed record ShowcaseMotionSample(TimeSpan Time, bool IsPreAction, double Opacity, double TranslateY);

internal static class ShowcaseMotionSampler
{
    public static ShowcaseMotionSample Sample(
        ShowcaseMotionProfile profile,
        ShowcaseMotion motion,
        ShowcaseFrame frame)
    {
        var duration = ShowcaseResources.Get<Duration>(profile.DurationResourceKey);
        if (!duration.HasTimeSpan || duration.TimeSpan == TimeSpan.Zero)
            return Settled(TimeSpan.Zero);

        var offset = profile.Translates
            ? ShowcaseResources.Get<double>("Daynote.Motion.Offset.Subtle")
            : 0;
        if (frame == ShowcaseFrame.Rest)
            return new ShowcaseMotionSample(TimeSpan.Zero, true, 0, offset);
        if (motion == ShowcaseMotion.Reduced)
            return Settled(TimeSpan.Zero);
        if (frame == ShowcaseFrame.Settled)
            return Settled(duration.TimeSpan);

        var time = DaynoteMotionPolicy.ForShowcase(reducedMotion: false).EvidenceMidpoint;
        var easing = ShowcaseResources.Get<IEasingFunction>(profile.EasingResourceKey);
        var progress = Math.Clamp(time.TotalMilliseconds / duration.TimeSpan.TotalMilliseconds, 0, 1);
        var opacity = easing.Ease(progress);
        return new ShowcaseMotionSample(time, false, opacity, offset * (1 - opacity));
    }

    private static ShowcaseMotionSample Settled(TimeSpan time) => new(time, false, 1, 0);
}

internal static class ShowcaseUi
{
    public static StackPanel Stack(WpfOrientation orientation, string name)
    {
        var panel = new StackPanel { Orientation = orientation };
        AutomationProperties.SetName(panel, name);
        return panel;
    }

    public static TextBlock Text(string content, string role, string name)
    {
        var text = new TextBlock { Text = content };
        ShowcaseResources.Style(
            text,
            role.Replace("Daynote.Type.", "Daynote.Style.Type.", StringComparison.Ordinal));
        AutomationProperties.SetName(text, name);
        return text;
    }

    public static WpfButton Button(string label, string style, string name, bool preferred = false)
    {
        var button = new WpfButton { Content = label };
        ShowcaseResources.Style(button, style);
        AutomationProperties.SetName(button, name);
        ShowcaseFocus.SetIsPreferred(button, preferred);
        return button;
    }

    public static ContentControl Panel(string style, string name, UIElement child)
    {
        var control = new ContentControl { Content = child };
        ShowcaseResources.Style(control, style);
        AutomationProperties.SetName(control, name);
        return control;
    }

    public static string StressText(ShowcaseStress stress) => stress switch
    {
        ShowcaseStress.Cjk => "목요일 기록은 입력기 조합 중에도 안전하게 유지됩니다. Daynote에서 한글과 Latin 문장을 함께 씁니다.",
        ShowcaseStress.Long => "A deliberately long plain-language title verifies two-line wrapping, stable actions, complete automation names, and bounded content without changing the shell measure.",
        ShowcaseStress.Unbroken => "https://daynote.invalid/verification/ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_abcdefghijklmnopqrstuvwxyz_0123456789",
        _ => "A deterministic Daynote-owned specimen verifies the primitive without product data."
    };
}
