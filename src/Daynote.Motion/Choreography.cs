using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Daynote.Motion;

/// <summary>
/// The parts of a to-do's tick that move (M3), whichever app draws them.
/// </summary>
/// <param name="Fill">The fill inside the ring, scaled from its centre.</param>
/// <param name="Mark">Draws the check stroke to a fraction, 0 to 1.</param>
/// <param name="Strike">The strikethrough line, scaled from its left end.</param>
/// <param name="Pulse">The ring that grows and fades behind the box. Null where there is none (the desktops).</param>
public sealed record CheckParts(Visual Fill, Action<double> Mark, Visual Strike, Visual? Pulse);

/// <summary>
/// The spec's interactions, M1 to M7, as storyboards. A screen says which interaction happened and
/// to what; the values - which token, how far, after how long - are only here.
/// </summary>
/// <remarks>
/// Each builder reads <see cref="MotionEnvironment"/> once, when it is called: the flavour for the
/// desktop variants the spec gives under "Mac/Win", and reduced motion, which turns every move and
/// scale into the 150 ms cross-fade of §04 while keeping the states that confirm something (M2's
/// held background, M3's fill and strikethrough).
/// </remarks>
public static class Choreography
{
    private static bool Reduced => MotionEnvironment.ReduceMotion;

    private static bool Desktop => MotionEnvironment.Flavor == MotionFlavor.Desktop;

    private static MotionSpec Fade => MotionTokens.ReducedFade;

    // ── M1 · the @ bar ──────────────────────────────────────────────────────────────────────────

    /// <summary>M1: the bar rises from behind the keyboard the moment the first reading appears.</summary>
    public static Storyboard AtBarRise(Visual bar, double height)
    {
        MotionTransform t = MotionTransform.For(bar);
        if (Reduced)
        {
            t.Reset();
            return new Storyboard().Add(v => bar.Opacity = v, 0, 1, Fade);
        }

        bar.Opacity = 1;
        return new Storyboard().Add(v => t.Y = v, height, 0, MotionEnvironment.Token(MotionToken.Bouncy));
    }

    /// <summary>
    /// M1: switching between the two readings - the highlight slides, the text stays put, and the
    /// phone ticks.
    /// </summary>
    public static Storyboard AtBarSwitch(Visual highlight, double fromY, double toY)
    {
        MotionTransform t = MotionTransform.For(highlight);
        var board = new Storyboard().Cue(0, () => MotionEnvironment.Haptic(HapticKind.Selection));
        if (Reduced)
        {
            t.Y = toY;
            return board.Add(v => highlight.Opacity = v, 0, 1, Fade);
        }

        highlight.Opacity = 1;
        return board.Add(v => t.Y = v, fromY, toY, MotionEnvironment.Token(MotionToken.Snappy));
    }

    /// <summary>
    /// M1 on a keyboard: the desktop popup, from the caret - opacity and a 2% scale over 160 ms,
    /// no bounce. Also the tablet's, when a hardware keyboard is attached.
    /// </summary>
    public static Storyboard AtPopupOpen(Visual popup)
    {
        MotionTransform t = MotionTransform.For(popup);
        if (Reduced)
        {
            t.Reset();
            return new Storyboard().Add(v => popup.Opacity = v, 0, 1, Fade);
        }

        var spec = MotionSpec.Ms(160, MotionTokens.GentleCurve);
        return new Storyboard()
            .Add(v => popup.Opacity = v, 0, 1, spec)
            .Add(v => t.Scale = v, 0.98, 1, spec);
    }

    /// <summary>M1: Esc or cancel - down and out, 220 ms ease-in. (No match closes with no animation at all.)</summary>
    public static Storyboard AtBarDismiss(Visual bar, double height)
    {
        MotionTransform t = MotionTransform.For(bar);
        if (Reduced)
        {
            return new Storyboard().Add(v => bar.Opacity = v, 1, 0, Fade);
        }

        return new Storyboard().Add(v => t.Y = v, 0, height, MotionEnvironment.Token(MotionToken.Exit, 220));
    }

    // ── M2 · a new to-do arrives ───────────────────────────────────────────────────────────────

    /// <summary>When M2's orange background starts to fade, after the row has arrived and held.</summary>
    public const double RowFlashHoldMs = 640;

    /// <summary>
    /// M2: the room opens first (height 0 to the row's), the row slides in from the right inside
    /// it, and the orange background stays a moment before it drains - the creation's confirmation.
    /// </summary>
    /// <param name="slot">What takes the height: the row's container.</param>
    /// <param name="row">What slides: the row's content.</param>
    /// <param name="flash">The orange background behind it.</param>
    /// <param name="height">The row's laid-out height.</param>
    public static Storyboard RowArrive(Layoutable slot, Visual row, Visual flash, double height)
    {
        ArgumentNullException.ThrowIfNull(slot);
        MotionTransform t = MotionTransform.For(row);
        Action<double> flashOpacity = v => flash.Opacity = v;
        var board = new Storyboard().Cue(0, () => MotionEnvironment.Haptic(HapticKind.Success));

        if (Reduced)
        {
            // No room opening and no slide; the background is the signal and it stays (§04).
            t.Reset();
            return board
                .Add(v => row.Opacity = v, 0, 1, Fade)
                .Add(flashOpacity, 1, 1, Fade)
                .Add(flashOpacity, 1, 0, MotionSpec.Ms(1000, MotionTokens.GentleCurve), Fade.Duration.TotalMilliseconds + RowFlashHoldMs);
        }

        MotionSpec bouncy = MotionEnvironment.Token(MotionToken.Bouncy);
        const double slideDelay = 80;
        double arrived = slideDelay + bouncy.Duration.TotalMilliseconds;
        return board
            .Add(v => slot.Height = v, 0, height, MotionEnvironment.Token(MotionToken.Snappy))
            .Add(v => t.X = v, 28, 0, bouncy, slideDelay)
            .Add(v => row.Opacity = v, 0, 1, bouncy, slideDelay)
            .Add(flashOpacity, 1, 1, bouncy, slideDelay)
            .Add(flashOpacity, 1, 0, MotionSpec.Ms(1000, MotionTokens.GentleCurve), arrived + RowFlashHoldMs)
            .Cue(arrived, () => slot.Height = double.NaN);
    }

    /// <summary>
    /// M2 on the phone's editor, where the day is out of sight: the "이 노트의 항목 N" button
    /// swells once. Nothing at all under reduced motion, which drops the roll-up too.
    /// </summary>
    public static Storyboard CountBump(Visual button)
    {
        MotionTransform t = MotionTransform.For(button);
        if (Reduced)
        {
            t.Reset();
            return new Storyboard();
        }

        Action<double> scale = v => t.Scale = v;
        return new Storyboard()
            .Add(scale, 1, 1.08, MotionSpec.Ms(160, MotionTokens.GentleCurve))
            .Add(scale, 1.08, 1, MotionEnvironment.Token(MotionToken.Snappy), 160);
    }

    // ── M3 · a tick ────────────────────────────────────────────────────────────────────────────

    /// <summary>How long a ticked row waits before it moves down to 완료: room to take the tap back.</summary>
    public const double CheckSettleMs = 600;

    /// <summary>
    /// M3: the fill grows from the middle, the check draws itself, the strikethrough passes left to
    /// right, and a ring pulses out - the order is the confirmation.
    /// </summary>
    public static Storyboard Check(CheckParts parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        MotionTransform fill = MotionTransform.For(parts.Fill);
        MotionTransform strike = MotionTransform.For(parts.Strike);
        var board = new Storyboard().Cue(0, () => MotionEnvironment.Haptic(HapticKind.Confirm));
        if (parts.Pulse is { } hidden)
        {
            hidden.Opacity = 0;
        }

        if (Reduced)
        {
            // The fill and the strikethrough still appear - they are the state - but in place.
            fill.Scale = 1;
            strike.ScaleX = 1;
            parts.Mark(1);
            return board
                .Add(v => parts.Fill.Opacity = v, 0, 1, Fade)
                .Add(v => parts.Strike.Opacity = v, 0, 1, Fade);
        }

        parts.Fill.Opacity = 1;
        parts.Strike.Opacity = 1;
        MotionSpec fillSpec = Desktop
            ? MotionSpec.Ms(200, MotionTokens.EaseOutCurve)
            : MotionEnvironment.Token(MotionToken.Bouncy);
        board
            .Add(v => fill.Scale = v, 0, 1, fillSpec)
            .Add(parts.Mark, 0, 1, MotionSpec.Ms(380, MotionTokens.GentleCurve), 190)
            .Add(v => strike.ScaleX = v, 0, 1, MotionSpec.Ms(440, MotionTokens.GentleCurve), 320);

        if (!Desktop && parts.Pulse is { } pulse)
        {
            MotionTransform ring = MotionTransform.For(pulse);
            MotionSpec spread = MotionEnvironment.Token(MotionToken.Gentle);
            board
                .Add(v => ring.Scale = v, 1, 2.1, spread)
                .Add(v => pulse.Opacity = v, 0.45, 0, spread);
        }

        return board;
    }

    /// <summary>M3 undone: not the tick in reverse - the fill shrinks away (160 ms ease-in) and the line just goes.</summary>
    public static Storyboard Uncheck(CheckParts parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        MotionTransform fill = MotionTransform.For(parts.Fill);
        parts.Strike.Opacity = 0;
        parts.Mark(0);
        if (parts.Pulse is { } pulse)
        {
            pulse.Opacity = 0;
        }

        if (Reduced)
        {
            fill.Scale = 1;
            return new Storyboard().Add(v => parts.Fill.Opacity = v, 1, 0, Fade);
        }

        parts.Fill.Opacity = 1;
        return new Storyboard().Add(v => fill.Scale = v, 1, 0, MotionEnvironment.Token(MotionToken.Exit, 160));
    }

    // ── M4 · "added to another date" ───────────────────────────────────────────────────────────

    /// <summary>How long M4's line stays open before it closes on its own (paused under a pointer).</summary>
    public static readonly TimeSpan NoticeHold = TimeSpan.FromSeconds(4);

    /// <summary>M4: the line opens its own room at the top of the panel, pushing the list down.</summary>
    public static Storyboard NoticeOpen(Layoutable line, double height)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (Reduced)
        {
            line.Height = double.NaN;
            return new Storyboard().Add(v => line.Opacity = v, 0, 1, Fade);
        }

        MotionSpec snappy = MotionEnvironment.Token(MotionToken.Snappy);
        return new Storyboard()
            .Add(v => line.Height = v, 0, height, snappy)
            .Add(v => line.Opacity = v, 0, 1, snappy)
            .Cue(snappy.Duration.TotalMilliseconds, () => line.Height = double.NaN);
    }

    /// <summary>M4: and closes slowly, gentle 450 ms.</summary>
    public static Storyboard NoticeClose(Layoutable line, double height)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (Reduced)
        {
            return new Storyboard().Add(v => line.Opacity = v, 1, 0, Fade);
        }

        MotionSpec gentle = MotionEnvironment.Token(MotionToken.Gentle);
        return new Storyboard()
            .Add(v => line.Height = v, height, 0, gentle)
            .Add(v => line.Opacity = v, 1, 0, gentle);
    }

    // ── M5 · moving to another date ────────────────────────────────────────────────────────────

    /// <summary>M5: the selected pill springs to the new day.</summary>
    public static Storyboard PillSlide(Visual pill, double fromX, double toX)
    {
        MotionTransform t = MotionTransform.For(pill);
        if (Reduced)
        {
            // No overshoot, no travel: the pill is simply at the new day, faded in.
            t.X = toX;
            return new Storyboard().Add(v => pill.Opacity = v, 0, 1, Fade);
        }

        pill.Opacity = 1;
        return new Storyboard().Add(v => t.X = v, fromX, toX, MotionEnvironment.Token(MotionToken.Bouncy));
    }

    /// <summary>
    /// M5 on the desktop's mini calendar: the cell highlight only fades, 120 ms.
    /// </summary>
    public static Storyboard HighlightFade(Visual highlight) =>
        new Storyboard().Add(v => highlight.Opacity = v, 0, 1, Reduced ? Fade : MotionSpec.Ms(120, MotionTokens.GentleCurve));

    /// <summary>
    /// M5: the day's content is pushed a short way in the direction of travel and cross-fades - to
    /// the left going forward in time, to the right going back.
    /// </summary>
    /// <param name="incoming">The content for the new date.</param>
    /// <param name="outgoing">A still of the old content laid over it, or null when there is none.</param>
    /// <param name="direction">+1 for a later date, -1 for an earlier one.</param>
    public static Storyboard ContentPush(Visual incoming, Visual? outgoing, int direction)
    {
        MotionTransform inT = MotionTransform.For(incoming);
        var board = new Storyboard();
        if (Reduced)
        {
            inT.Reset();
            board.Add(v => incoming.Opacity = v, 0, 1, Fade);
            if (outgoing is not null)
            {
                board.Add(v => outgoing.Opacity = v, 1, 0, Fade);
            }

            return board;
        }

        // The desktop moves less (12 points, not 18).
        double distance = (Desktop ? 12 : 18) * Math.Sign(direction == 0 ? 1 : direction);
        if (outgoing is not null)
        {
            MotionTransform outT = MotionTransform.For(outgoing);
            MotionSpec leave = MotionEnvironment.Token(MotionToken.Exit, 250);
            board
                .Add(v => outT.X = v, 0, -distance, leave)
                .Add(v => outgoing.Opacity = v, 1, 0, leave);
        }

        MotionSpec arrive = MotionEnvironment.Token(MotionToken.Gentle);
        return board
            .Add(v => inT.X = v, distance, 0, arrive, 130)
            .Add(v => incoming.Opacity = v, 0, 1, arrive, 130);
    }

    /// <summary>
    /// M5, a week change: the whole strip moves one width over and the pill goes with it.
    /// </summary>
    public static Storyboard WeekSlide(Visual incoming, Visual? outgoing, double width, int direction)
    {
        MotionTransform inT = MotionTransform.For(incoming);
        var board = new Storyboard();
        if (Reduced)
        {
            inT.Reset();
            board.Add(v => incoming.Opacity = v, 0, 1, Fade);
            if (outgoing is not null)
            {
                board.Add(v => outgoing.Opacity = v, 1, 0, Fade);
            }

            return board;
        }

        incoming.Opacity = 1;
        double distance = width * Math.Sign(direction == 0 ? 1 : direction);
        MotionSpec gentle = MotionEnvironment.Token(MotionToken.Gentle);
        if (outgoing is not null)
        {
            MotionTransform outT = MotionTransform.For(outgoing);
            board.Add(v => outT.X = v, 0, -distance, gentle);
        }

        return board.Add(v => inT.X = v, distance, 0, gentle);
    }

    // ── M6 · sheets and popovers ───────────────────────────────────────────────────────────────

    /// <summary>The release speed above which a dragged sheet closes, in points per second.</summary>
    public const double DismissVelocity = 800;

    /// <summary>How far down, as a share of its height, a dragged sheet closes however slowly it was let go.</summary>
    public const double DismissFraction = 0.4;

    /// <summary>M6's drag rule: let go faster than 800 pt/s, or more than 40% of the way down, and it closes.</summary>
    public static bool ShouldDismiss(double offset, double height, double velocity) =>
        velocity > DismissVelocity || (height > 0 && offset >= height * DismissFraction);

    /// <summary>M6: the scrim fades up linearly while the sheet rises with a slight overshoot.</summary>
    public static Storyboard SheetOpen(Visual scrim, Visual sheet, double height)
    {
        MotionTransform t = MotionTransform.For(sheet);
        if (Reduced)
        {
            t.Reset();
            return new Storyboard()
                .Add(v => scrim.Opacity = v, 0, 1, Fade)
                .Add(v => sheet.Opacity = v, 0, 1, Fade);
        }

        sheet.Opacity = 1;
        return new Storyboard()
            .Add(v => scrim.Opacity = v, 0, 1, MotionSpec.Ms(300, new Avalonia.Animation.Easings.LinearEasing()))
            .Add(v => t.Y = v, height, 0, MotionEnvironment.Token(MotionToken.Bouncy, 500));
    }

    /// <summary>
    /// M6: closing has no bounce - straight down, ease-in 280 ms, from wherever a drag left it.
    /// </summary>
    public static Storyboard SheetClose(Visual scrim, Visual sheet, double height, double fromY = 0)
    {
        MotionTransform t = MotionTransform.For(sheet);
        if (Reduced)
        {
            return new Storyboard()
                .Add(v => scrim.Opacity = v, scrim.Opacity, 0, Fade)
                .Add(v => sheet.Opacity = v, 1, 0, Fade);
        }

        MotionSpec exit = MotionEnvironment.Token(MotionToken.Exit, 280);
        return new Storyboard()
            .Add(v => scrim.Opacity = v, scrim.Opacity, 0, exit)
            .Add(v => t.Y = v, fromY, height, exit);
    }

    /// <summary>M6: a drag let go short of closing goes back up.</summary>
    public static Storyboard SheetSettle(Visual sheet, double fromY)
    {
        MotionTransform t = MotionTransform.For(sheet);
        return new Storyboard().Add(v => t.Y = v, fromY, 0, Reduced ? Fade : MotionEnvironment.Token(MotionToken.Snappy));
    }

    /// <summary>
    /// M6 on the desktop: a popover fades and grows from 96% towards its anchor in 180 ms.
    /// The origin is the caller's, set on the surface as its <c>RenderTransformOrigin</c>.
    /// </summary>
    public static Storyboard PopoverOpen(Visual surface)
    {
        MotionTransform t = MotionTransform.For(surface);
        if (Reduced)
        {
            t.Reset();
            return new Storyboard().Add(v => surface.Opacity = v, 0, 1, Fade);
        }

        var spec = MotionSpec.Ms(180, MotionTokens.GentleCurve);
        return new Storyboard()
            .Add(v => surface.Opacity = v, 0, 1, spec)
            .Add(v => t.Scale = v, 0.96, 1, spec);
    }

    /// <summary>M6 on the desktop: closing is a 120 ms fade.</summary>
    public static Storyboard PopoverClose(Visual surface) =>
        new Storyboard().Add(v => surface.Opacity = v, 1, 0, Reduced ? Fade : MotionSpec.Ms(120, MotionTokens.ExitCurve));

    // ── M7 · fold and unfold ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// M7: the system changes the window; the one thing the app adds is the panel appearing from
    /// the left, once the width has settled. The note stays where it is.
    /// </summary>
    public static Storyboard PanelAppear(Visual panel)
    {
        MotionTransform t = MotionTransform.For(panel);
        if (Reduced)
        {
            t.Reset();
            return new Storyboard().Add(v => panel.Opacity = v, 0, 1, Fade);
        }

        MotionSpec snappy = MotionEnvironment.Token(MotionToken.Snappy);
        return new Storyboard()
            .Add(v => panel.Opacity = v, 0, 1, snappy)
            .Add(v => t.X = v, -14, 0, snappy);
    }

    // ── A to-do row's swipe ────────────────────────────────────────────────────────────────────

    /// <summary>How much of a row's width a left swipe has to pass to delete without stopping at the buttons.</summary>
    public const double SwipeDeleteFraction = 0.6;

    /// <summary>
    /// A swiped row let go: it springs to rest, open on its buttons or closed. Under reduced motion
    /// it is simply there - the finger already moved it, and a spring back would be motion added.
    /// </summary>
    /// <param name="offset">Moves the row and sizes what it uncovers.</param>
    public static Storyboard SwipeSettle(Action<double> offset, double from, double to)
    {
        ArgumentNullException.ThrowIfNull(offset);
        return Reduced
            ? new Storyboard().Add(offset, to, to, Fade)
            : new Storyboard().Add(offset, from, to, MotionEnvironment.Token(MotionToken.Snappy));
    }

    /// <summary>
    /// A row deleted by a swipe: it carries on off the left edge, then its room closes (exit) and
    /// the rows below slide up into it. Under reduced motion it only fades.
    /// </summary>
    /// <param name="row">What gives up its height.</param>
    /// <param name="offset">Moves the row, from where the finger left it.</param>
    public static Storyboard RowDelete(Layoutable row, Action<double> offset, double from, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(offset);
        if (Reduced)
        {
            return new Storyboard().Add(v => row.Opacity = v, 1, 0, Fade);
        }

        MotionSpec exit = MotionEnvironment.Token(MotionToken.Exit);
        return new Storyboard()
            .Add(offset, from, -width, MotionEnvironment.Token(MotionToken.Exit, 180))
            .Add(v => row.Height = v, height, 0, exit, 180);
    }

    /// <summary>"삭제됨 · 실행 취소": the line rises a little into place as it fades in.</summary>
    public static Storyboard ToastOpen(Visual toast)
    {
        MotionTransform t = MotionTransform.For(toast);
        if (Reduced)
        {
            t.Reset();
            return new Storyboard().Add(v => toast.Opacity = v, 0, 1, Fade);
        }

        MotionSpec snappy = MotionEnvironment.Token(MotionToken.Snappy);
        return new Storyboard()
            .Add(v => t.Y = v, 16, 0, snappy)
            .Add(v => toast.Opacity = v, 0, 1, snappy);
    }

    /// <summary>And goes: a fade, easing in.</summary>
    public static Storyboard ToastClose(Visual toast) =>
        new Storyboard().Add(v => toast.Opacity = v, toast.Opacity, 0, Reduced ? Fade : MotionEnvironment.Token(MotionToken.Exit));
}
