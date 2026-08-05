using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfSize = System.Windows.Size;

namespace Daynote.App.Showcase;

internal static partial class ShowcaseInteractionSequenceRunner
{
    private static void ResetMotion(
        FrameworkElement target,
        ShowcaseInteractionDefinition definition,
        ShowcaseMotion motion)
    {
        CompleteMotion(target);
        target.Opacity = 0;
        var profile = ShowcaseMotionProfile.For(definition.FamilyId);
        if (!profile.Translates || motion == ShowcaseMotion.Reduced) return;
        var transform = MutableTransform(target);
        transform.Y = ShowcaseResources.Get<double>("Daynote.Motion.Offset.Subtle");
    }

    private static void ApplyMotionSample(
        FrameworkElement target,
        ShowcaseInteractionDefinition definition,
        ShowcaseMotion motion,
        ShowcaseFrame frame)
    {
        var profile = ShowcaseMotionProfile.For(definition.FamilyId);
        var sample = ShowcaseMotionSampler.Sample(profile, motion, frame);
        target.ApplyAnimationClock(UIElement.OpacityProperty, null);
        target.Opacity = sample.Opacity;
        if (!profile.Translates) return;
        var transform = MutableTransform(target);
        transform.ApplyAnimationClock(TranslateTransform.YProperty, null);
        transform.Y = sample.TranslateY;
    }

    private static TranslateTransform MutableTransform(FrameworkElement target)
    {
        var transform = target.RenderTransform switch
        {
            TranslateTransform existing when !existing.IsFrozen => existing,
            TranslateTransform existing => existing.Clone(),
            _ => new TranslateTransform()
        };
        target.RenderTransform = transform;
        return transform;
    }

    private static void CompleteMotion(FrameworkElement target)
    {
        target.ApplyAnimationClock(UIElement.OpacityProperty, null);
        target.Opacity = 1;
        if (target.RenderTransform is TranslateTransform { IsFrozen: false } transform)
        {
            transform.ApplyAnimationClock(TranslateTransform.YProperty, null);
            transform.Y = 0;
        }
    }

    private static ShowcaseSequenceFrame Capture(
        FrameworkElement surface,
        FrameworkElement motionTarget,
        FrameworkElement? scrollOwner,
        ShowcaseInteractionDefinition definition,
        string transitionId,
        ShowcaseMotion motion,
        string semantic,
        string output,
        double width,
        double height,
        int scale,
        double elapsedMilliseconds)
    {
        Layout(surface, width, height);
        Pump(surface.Dispatcher, TimeSpan.FromMilliseconds(1));
        var pixelWidth = checked((int)Math.Round(width * scale, MidpointRounding.AwayFromZero));
        var pixelHeight = checked((int)Math.Round(height * scale, MidpointRounding.AwayFromZero));
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96d * scale, 96d * scale, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        bitmap.Freeze();
        var fileName = $"{definition.FamilyId}.{motion.ToString().ToLowerInvariant()}.{semantic.ToLowerInvariant()}.png";
        var path = Path.Combine(output, fileName);
        using (var stream = File.Create(path))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
        }
        var translateY = motionTarget.RenderTransform is TranslateTransform transform ? transform.Y : 0;
        return new ShowcaseSequenceFrame(
            transitionId, Environment.ProcessId, semantic, fileName,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), pixelWidth, pixelHeight,
            elapsedMilliseconds, motionTarget.Opacity, translateY, ObserveSemanticState(surface, definition),
            FocusName(), scrollOwner is null ? "none" : Name(scrollOwner), DateTimeOffset.UtcNow);
    }

    private static TimeSpan Duration(ShowcaseInteractionDefinition definition)
    {
        var profile = ShowcaseMotionProfile.For(definition.FamilyId);
        return ShowcaseResources.Get<Duration>(profile.DurationResourceKey).TimeSpan;
    }

    private static void Layout(FrameworkElement surface, double width, double height)
    {
        surface.Measure(new WpfSize(width, height));
        surface.Arrange(new Rect(0, 0, width, height));
        surface.UpdateLayout();
    }

    private static void Pump(Dispatcher dispatcher, TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero) return;
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = delay };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
