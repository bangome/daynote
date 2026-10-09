using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Daynote.App.Composition;
using Daynote.Core.Domain;
using Daynote.Mobile.ViewModels;
using Daynote.Mobile.Views;
using Daynote.Motion;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// The motion spec's tokens and the switches around them - which curve a platform gets, what
/// reduced motion leaves, when the phone taps back - and frame-by-frame renders of M3 and M5.
/// </summary>
/// <remarks>
/// <see cref="MotionEnvironment"/> is process-wide, so these run one at a time and put it back.
/// The frame sequences land under <c>artifacts/motion-frames</c>, one PNG per frame and a strip of
/// all of them, to be set beside the spec's demos.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class MotionTests
{
    private static readonly string FramesDirectory =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "motion-frames");

    [TestCleanup]
    public void Restore()
    {
        MotionEnvironment.Flavor = MotionFlavor.Touch;
        MotionEnvironment.Platform = null;
        MotionEnvironment.ReduceMotionOverride = null;
        MotionEnvironment.Instant = true;
        MotionPlayer.Interceptor = null;
    }

    // ── Tokens ───────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(MotionToken.Snappy, 320)]
    [DataRow(MotionToken.Bouncy, 480)]
    [DataRow(MotionToken.Gentle, 450)]
    [DataRow(MotionToken.Exit, 240)]
    public void Every_token_runs_for_the_spec_duration_on_both_platforms(MotionToken token, double milliseconds)
    {
        foreach (MotionFlavor flavor in new[] { MotionFlavor.Touch, MotionFlavor.Desktop })
        {
            MotionSpec spec = MotionTokens.Get(token, flavor);
            Assert.AreEqual(milliseconds, spec.Duration.TotalMilliseconds, $"{token} on {flavor}");
            Assert.AreEqual(0, spec.Easing.Ease(0), 1e-9, $"{token} on {flavor} starts at 0");
            Assert.AreEqual(1, spec.Easing.Ease(1), 1e-9, $"{token} on {flavor} ends at 1");
        }
    }

    [TestMethod]
    public void Bouncy_overshoots_six_percent_on_a_phone_and_half_that_on_the_desktop()
    {
        Assert.AreEqual(0.06, Peak(MotionTokens.Get(MotionToken.Bouncy, MotionFlavor.Touch)) - 1, 0.002);
        Assert.AreEqual(0.03, Peak(MotionTokens.Get(MotionToken.Bouncy, MotionFlavor.Desktop)) - 1, 0.002);
        Assert.AreEqual(0.01, Peak(MotionTokens.Get(MotionToken.Snappy, MotionFlavor.Touch)) - 1, 0.002);
        Assert.AreEqual(0.005, Peak(MotionTokens.Get(MotionToken.Snappy, MotionFlavor.Desktop)) - 1, 0.002);

        // Gentle and exit never pass the end.
        Assert.AreEqual(1, Peak(MotionTokens.Get(MotionToken.Gentle, MotionFlavor.Touch)), 1e-6);
        Assert.AreEqual(1, Peak(MotionTokens.Get(MotionToken.Exit, MotionFlavor.Touch)), 1e-6);
    }

    [TestMethod]
    public void The_desktop_spring_rises_as_fast_as_the_phone_one_and_only_settles_flatter()
    {
        MotionSpec touch = MotionTokens.Get(MotionToken.Bouncy, MotionFlavor.Touch);
        MotionSpec desktop = MotionTokens.Get(MotionToken.Bouncy, MotionFlavor.Desktop);
        foreach (double at in new[] { 0.06, 0.14, 0.24, 0.3 })
        {
            Assert.AreEqual(touch.Easing.Ease(at), desktop.Easing.Ease(at), 1e-9, $"at {at}");
        }

        Assert.IsLessThan(touch.Easing.Ease(0.38), desktop.Easing.Ease(0.38));
    }

    [TestMethod]
    public void The_sampled_springs_pass_through_the_spec_stops()
    {
        // snappy: linear(0, 0.35 8%, 0.75 18%, 0.96 30%, 1.01 40%, 1)
        Assert.AreEqual(0.35, MotionTokens.SnappyCurve.Ease(0.08), 1e-9);
        Assert.AreEqual(1.01, MotionTokens.SnappyCurve.Ease(0.40), 1e-9);
        // bouncy: ... 1.05 33%, 1.06 38% ... 0.995 60%, 1
        Assert.AreEqual(1.05, MotionTokens.BouncyCurve.Ease(0.33), 1e-9);
        Assert.AreEqual(0.995, MotionTokens.BouncyCurve.Ease(0.60), 1e-9);
        Assert.AreEqual(0.9975, MotionTokens.BouncyCurve.Ease(0.80), 1e-9);
    }

    // ── Reduced motion ───────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Reduced_motion_follows_the_platform_and_its_changes()
    {
        var platform = new FakeMotionPlatform();
        int changes = 0;
        void Count(object? sender, EventArgs e) => changes++;
        MotionEnvironment.Changed += Count;
        try
        {
            MotionEnvironment.Platform = platform;
            Assert.IsFalse(MotionEnvironment.ReduceMotion);

            platform.Reduced = true;
            Assert.IsTrue(MotionEnvironment.ReduceMotion);
            Assert.IsGreaterThanOrEqualTo(2, changes, "Setting the platform and its change should both be announced.");

            MotionEnvironment.ReduceMotionOverride = false;
            Assert.IsFalse(MotionEnvironment.ReduceMotion, "An override wins over the platform.");
        }
        finally
        {
            MotionEnvironment.Changed -= Count;
        }
    }

    [TestMethod]
    public void Reduced_motion_turns_every_move_into_a_150_ms_fade()
    {
        MotionEnvironment.ReduceMotionOverride = true;
        var scrim = new Border();
        var sheet = new Border();
        var panel = new Border();
        var pill = new Border();

        foreach ((string name, Storyboard board, Visual moved) in new (string, Storyboard, Visual)[]
        {
            ("sheet", Choreography.SheetOpen(scrim, sheet, 400), sheet),
            ("panel", Choreography.PanelAppear(panel), panel),
            ("pill", Choreography.PillSlide(pill, 0, 120), pill),
        })
        {
            Assert.AreEqual(150, board.Duration.TotalMilliseconds, name);
            board.Seek(TimeSpan.FromMilliseconds(75));
            Assert.AreEqual(0.5, moved.Opacity, 0.01, $"{name}: halfway through the fade");
            board.Seek(board.Duration);
            Assert.AreEqual(1, moved.Opacity, 1e-9, name);
        }

        Assert.AreEqual(0, MotionTransform.For(sheet).Y, "The sheet fades in place instead of rising.");
        Assert.AreEqual(0, MotionTransform.For(panel).X, "The panel fades in place instead of sliding.");
        Assert.AreEqual(120, MotionTransform.For(pill).X, "The pill is at the new day with no travel.");
    }

    [TestMethod]
    public void Reduced_motion_keeps_the_confirmations_and_drops_the_pulse()
    {
        MotionEnvironment.ReduceMotionOverride = true;
        var fill = new Border();
        var strike = new Border();
        var pulse = new Border();
        double mark = 0;
        Storyboard tick = Choreography.Check(new CheckParts(fill, v => mark = v, strike, pulse));

        tick.Seek(tick.Duration);
        Assert.AreEqual(1, fill.Opacity, "The fill is the state; it stays.");
        Assert.AreEqual(1, strike.Opacity, "The strikethrough is the state; it stays.");
        Assert.AreEqual(1, mark);
        Assert.AreEqual(0, pulse.Opacity, "The pulse ring is motion and goes.");
        Assert.AreEqual(1, MotionTransform.For(strike).ScaleX, "The line is whole at once, not drawn across.");

        // M2: the orange background is held for its 640 ms, then fades, even with the slide gone.
        var slot = new Border();
        var row = new Border();
        var flash = new Border();
        Storyboard arrive = Choreography.RowArrive(slot, row, flash, 44);
        arrive.Seek(TimeSpan.FromMilliseconds(150 + Choreography.RowFlashHoldMs - 10));
        Assert.AreEqual(1, flash.Opacity, "Held.");
        Assert.AreEqual(0, MotionTransform.For(row).X, "No slide.");
        arrive.Seek(arrive.Duration);
        Assert.AreEqual(0, flash.Opacity, "Then drained.");
    }

    [TestMethod]
    public void Haptics_play_on_the_phones_only_and_whatever_the_motion_setting()
    {
        var platform = new FakeMotionPlatform { Reduced = true };
        MotionEnvironment.Platform = platform;

        Choreography.Check(new CheckParts(new Border(), _ => { }, new Border(), new Border())).Seek(TimeSpan.Zero);
        Choreography.RowArrive(new Border(), new Border(), new Border(), 44).Seek(TimeSpan.Zero);
        Choreography.AtBarSwitch(new Border(), 0, 46).Seek(TimeSpan.Zero);
        CollectionAssert.AreEqual(new[] { HapticKind.Confirm, HapticKind.Success, HapticKind.Selection }, platform.Played);

        MotionEnvironment.Flavor = MotionFlavor.Desktop;
        Choreography.Check(new CheckParts(new Border(), _ => { }, new Border(), null)).Seek(TimeSpan.Zero);
        Assert.HasCount(3, platform.Played, "The Mac has no haptics in the spec.");
    }

    [TestMethod]
    public void The_desktop_tick_has_no_pulse_and_no_overshoot()
    {
        MotionEnvironment.Flavor = MotionFlavor.Desktop;
        var fill = new Border();
        var pulse = new Border();
        Storyboard tick = Choreography.Check(new CheckParts(fill, _ => { }, new Border(), pulse));
        double peak = 0;
        for (int ms = 0; ms <= 200; ms += 5)
        {
            tick.Seek(TimeSpan.FromMilliseconds(ms));
            peak = Math.Max(peak, MotionTransform.For(fill).ScaleX);
        }

        Assert.AreEqual(1, peak, 1e-9, "ease-out 200 ms, which never passes full size");
        Assert.AreEqual(0, pulse.Opacity);
    }

    [TestMethod]
    [DataRow(100, 400, 900, true)]
    [DataRow(170, 400, 100, true)]
    [DataRow(150, 400, 700, false)]
    [DataRow(20, 400, 0, false)]
    public void A_sheet_closes_past_800_points_a_second_or_40_percent_down(double offset, double height, double velocity, bool closes) =>
        Assert.AreEqual(closes, Choreography.ShouldDismiss(offset, height, velocity));

    [TestMethod]
    public void A_storyboard_hands_a_property_from_one_track_to_the_next()
    {
        var target = new Border();
        Action<double> opacity = v => target.Opacity = v;
        var board = new Storyboard()
            .Add(opacity, 1, 1, MotionSpec.Ms(100, MotionTokens.GentleCurve))
            .Add(opacity, 1, 0, MotionSpec.Ms(100, MotionTokens.GentleCurve), 300);
        double height = 0;
        board.Add(v => height = v, 0, 44, MotionSpec.Ms(100, MotionTokens.GentleCurve)).Cue(150, () => height = double.NaN);

        board.Seek(TimeSpan.FromMilliseconds(200));
        Assert.AreEqual(1, target.Opacity, "Held between the tracks.");
        Assert.IsTrue(double.IsNaN(height), "A cue after a track has ended is not undone by it.");
        board.Seek(TimeSpan.FromMilliseconds(400));
        Assert.AreEqual(0, target.Opacity, 1e-9);
    }

    // ── Frames ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>M3 on the day screen's first open to-do: 16 frames over its 760 ms.</summary>
    [TestMethod]
    public void M3_tick_renders_frame_by_frame()
    {
        TestServices.WithInitialisedShell(402, 874, (view, shell) =>
        {
            view.PreviewSafeArea = new Thickness(0, 62, 0, 34);
            ScreenshotTests.Seed(shell, LocalDates.FromDateOnly(DateOnly.FromDateTime(DateTime.Now)));
            shell.GoToPageCommand.Execute(MobilePage.Day);
            Settle(view);

            TodoCheck check = view.GetVisualDescendants().OfType<TodoCheck>().First(c => !c.IsChecked && c.IsEffectivelyVisible);
            Control row = check.GetVisualAncestors().OfType<Control>().First(c => c.Classes.Contains("todorow"));
            Rect crop = Crop(row, view, padLeft: 24);

            var captured = new List<Storyboard>();
            MotionEnvironment.Instant = false;
            MotionPlayer.Interceptor = (_, _, board) =>
            {
                captured.Add(board);
                return new TaskCompletionSource<bool>().Task;
            };
            check.Toggle = null;
            var window = (Window)TopLevel.GetTopLevel(view)!;
            Point centre = check.TranslatePoint(new Point(check.Bounds.Width / 2, check.Bounds.Height / 2), window)!.Value;
            window.MouseDown(centre, Avalonia.Input.MouseButton.Left);
            window.MouseUp(centre, Avalonia.Input.MouseButton.Left);
            window.MouseMove(new Point(1, 1));

            Assert.HasCount(1, captured, "One tick, one storyboard.");
            Assert.AreEqual(760, captured[0].Duration.TotalMilliseconds, 1, "The strikethrough ends last: 320 + 440 ms.");
            RenderFrames(view, "m3", captured, 760, 16, crop);
        });
    }

    /// <summary>
    /// M5 going two days forward within the week, then a week back: the pill, the strip and the
    /// day's content, frame by frame.
    /// </summary>
    [TestMethod]
    public void M5_date_change_renders_frame_by_frame()
    {
        TestServices.WithInitialisedShell(402, 874, (view, shell) =>
        {
            view.PreviewSafeArea = new Thickness(0, 62, 0, 34);
            ScreenshotTests.Seed(shell, LocalDates.FromDateOnly(DateOnly.FromDateTime(DateTime.Now)));
            shell.GoToPageCommand.Execute(MobilePage.Day);

            // From the week's Tuesday, so two days on stays inside the week.
            ScreenshotTests.Pump(() => shell.Week[2].SelectCommand.ExecuteAsync(null));
            Settle(view);
            Rect crop = new(0, 0, 402, 620);

            var captured = new List<Storyboard>();
            MotionEnvironment.Instant = false;
            MotionPlayer.Interceptor = (_, _, board) =>
            {
                captured.Add(board);
                return new TaskCompletionSource<bool>().Task;
            };

            ScreenshotTests.Pump(() => shell.Week[4].SelectCommand.ExecuteAsync(null));
            Assert.HasCount(2, captured, "The pill and the content.");
            RenderFrames(view, "m5-day", captured, 600, 13, crop);

            captured.Clear();
            ScreenshotTests.Pump(() => shell.PreviousWeekCommand.ExecuteAsync(null));
            Assert.HasCount(2, captured, "The strip and the content.");
            RenderFrames(view, "m5-week", captured, 600, 13, crop);
        });
    }

    private static void RenderFrames(Views.MainView view, string name, IReadOnlyList<Storyboard> boards, double totalMs, int count, Rect crop)
    {
        string directory = Path.Combine(FramesDirectory, name);
        Directory.CreateDirectory(directory);
        var frames = new List<WriteableBitmap>();
        for (int i = 0; i < count; i++)
        {
            var at = TimeSpan.FromMilliseconds(totalMs * i / (count - 1));
            foreach (Storyboard board in boards)
            {
                board.Seek(at);
            }

            Settle(view);
            using WriteableBitmap frame = ((Window)TopLevel.GetTopLevel(view)!).CaptureRenderedFrame()!;
            WriteableBitmap part = Cut(frame, crop);
            part.Save(Path.Combine(directory, $"{name}-{i:00}-{at.TotalMilliseconds:000}ms.png"), new PngBitmapEncoderOptions());
            frames.Add(part);
        }

        using WriteableBitmap strip = Stack(frames);
        strip.Save(Path.Combine(FramesDirectory, $"{name}-strip.png"), new PngBitmapEncoderOptions());
        foreach (WriteableBitmap frame in frames)
        {
            frame.Dispose();
        }
    }

    private static Rect Crop(Control control, Control root, double padLeft)
    {
        Point at = control.TranslatePoint(default, root)!.Value;
        return new Rect(Math.Max(0, at.X - padLeft), Math.Max(0, at.Y - 8), control.Bounds.Width + padLeft + 8, control.Bounds.Height + 16);
    }

    /// <summary>Copies <paramref name="rect"/> (in points) out of a frame rendered at 1x.</summary>
    private static WriteableBitmap Cut(WriteableBitmap frame, Rect rect)
    {
        var size = new PixelSize((int)rect.Width, (int)rect.Height);
        var part = new WriteableBitmap(size, new Vector(96, 96), frame.Format ?? PixelFormat.Rgba8888, AlphaFormat.Premul);
        using ILockedFramebuffer from = frame.Lock();
        using ILockedFramebuffer to = part.Lock();
        int width = Math.Min(size.Width, from.Size.Width - (int)rect.X);
        var row = new byte[width * 4];
        for (int y = 0; y < size.Height && (int)rect.Y + y < from.Size.Height; y++)
        {
            Marshal.Copy(from.Address + (((int)rect.Y + y) * from.RowBytes) + ((int)rect.X * 4), row, 0, row.Length);
            Marshal.Copy(row, 0, to.Address + (y * to.RowBytes), row.Length);
        }

        return part;
    }

    /// <summary>The frames one above the other, with a two-pixel gap between.</summary>
    private static WriteableBitmap Stack(IReadOnlyList<WriteableBitmap> frames)
    {
        int width = frames[0].PixelSize.Width;
        int height = frames[0].PixelSize.Height;
        var strip = new WriteableBitmap(
            new PixelSize(width, (height + 2) * frames.Count), new Vector(96, 96), frames[0].Format ?? PixelFormat.Rgba8888, AlphaFormat.Premul);
        using ILockedFramebuffer to = strip.Lock();
        var row = new byte[width * 4];
        for (int i = 0; i < frames.Count; i++)
        {
            using ILockedFramebuffer from = frames[i].Lock();
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(from.Address + (y * from.RowBytes), row, 0, row.Length);
                Marshal.Copy(row, 0, to.Address + (((i * (height + 2)) + y) * to.RowBytes), row.Length);
            }
        }

        return strip;
    }

    private static double Peak(MotionSpec spec)
    {
        double peak = 0;
        for (int i = 0; i <= 1000; i++)
        {
            peak = Math.Max(peak, spec.Easing.Ease(i / 1000.0));
        }

        return peak;
    }

    private static void Settle(Control view)
    {
        for (int i = 0; i < 3; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            TopLevel.GetTopLevel(view)?.UpdateLayout();
        }
    }

    internal sealed class FakeMotionPlatform : IMotionPlatform
    {
        private bool _reduced;

        public List<HapticKind> Played { get; } = [];

        public bool Reduced
        {
            get => _reduced;
            set
            {
                _reduced = value;
                PreferenceChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool PrefersReducedMotion => _reduced;

        public event EventHandler? PreferenceChanged;

        public void Play(HapticKind kind) => Played.Add(kind);
    }
}
