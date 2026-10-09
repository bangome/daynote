using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Daynote.Motion;

/// <summary>
/// One interaction's tracks on one clock: each a value running from one number to another under a
/// token, after a delay.
/// </summary>
/// <remarks>
/// <para>
/// Built on Avalonia's own frame callback rather than its keyframe animations, for two reasons.
/// The spec's interactions are several tracks with their own delays on one timeline (M3's fill,
/// stroke, strikethrough and pulse), which keyframe animations express only as separate clocks that
/// drift. And a storyboard can be <see cref="Seek">sought</see> to any instant, which is what lets a
/// test render frame 6 of a tick and compare it to the spec instead of trusting that it played.
/// </para>
/// <para>
/// A track applies its value through a setter, so the same storyboard moves a transform, an
/// opacity, a height or a dash offset.
/// </para>
/// </remarks>
public sealed class Storyboard
{
    private readonly List<Track> _tracks = [];
    private readonly List<(TimeSpan At, Action Action)> _cues = [];

    private sealed record Track(Action<double> Apply, double From, double To, MotionSpec Spec, TimeSpan Delay);

    /// <summary>When the last track settles.</summary>
    public TimeSpan Duration { get; private set; }

    /// <summary>Adds a track from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public Storyboard Add(Action<double> apply, double from, double to, MotionSpec spec, double delayMs = 0)
    {
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(spec);
        var delay = TimeSpan.FromMilliseconds(delayMs);
        _tracks.Add(new Track(apply, from, to, spec, delay));
        Duration = TimeSpan.FromTicks(Math.Max(Duration.Ticks, (delay + spec.Duration).Ticks));
        return this;
    }

    /// <summary>Runs <paramref name="action"/> once the clock passes <paramref name="atMs"/> - a haptic, say.</summary>
    public Storyboard Cue(double atMs, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _cues.Add((TimeSpan.FromMilliseconds(atMs), action));
        return this;
    }

    private TimeSpan _lastSeek = TimeSpan.MinValue;

    /// <summary>
    /// Puts every track where it is at <paramref name="time"/>. Tracks that have not started hold
    /// their start value; tracks that have ended hold their end value.
    /// </summary>
    /// <remarks>
    /// Tracks that share a setter (the same delegate instance) hand the property on: the one that
    /// started last drives it, and before any has started the first one holds its start value. That
    /// is how M2's background is held and then faded, and M5's content leaves and comes back.
    /// </remarks>
    public void Seek(TimeSpan time)
    {
        // Seeking backwards (a test stepping through frames out of order) starts over.
        if (time < _lastSeek)
        {
            _lastSeek = TimeSpan.MinValue;
        }

        foreach (IGrouping<Action<double>, Track> property in _tracks.GroupBy(static t => t.Apply))
        {
            Track? driving = property.Where(t => t.Delay <= time).MaxBy(static t => t.Delay);
            if (driving is null)
            {
                Track first = property.MinBy(static t => t.Delay)!;
                first.Apply(first.From);
                continue;
            }

            // A track that has ended writes its end value once and then lets go, so a cue after it
            // (M2 handing the row's height back to layout) is not undone on the next frame.
            TimeSpan end = driving.Delay + driving.Spec.Duration;
            if (time > end && _lastSeek > end)
            {
                continue;
            }

            double local = driving.Spec.Duration <= TimeSpan.Zero
                ? 1
                : Math.Clamp((time - driving.Delay).TotalMilliseconds / driving.Spec.Duration.TotalMilliseconds, 0, 1);
            driving.Apply(driving.From + ((driving.To - driving.From) * driving.Spec.Easing.Ease(local)));
        }

        foreach ((TimeSpan at, Action action) in _cues)
        {
            if (at <= time && at > _lastSeek)
            {
                action();
            }
        }

        _lastSeek = time;
    }

    /// <summary>
    /// Plays from the start on <paramref name="host"/>'s frames; true when it ran to the end, false
    /// when <paramref name="cancellationToken"/> stopped it first.
    /// </summary>
    /// <remarks>
    /// With no window to draw in, or in <see cref="MotionEnvironment.Instant"/> mode, it lands on
    /// the end state at once: nothing would be seen of the frames between.
    /// </remarks>
    public Task<bool> PlayAsync(Visual host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        _lastSeek = TimeSpan.MinValue;
        if (MotionEnvironment.Instant || TopLevel.GetTopLevel(host) is not { } top)
        {
            Seek(Duration);
            return Task.FromResult(true);
        }

        var done = new TaskCompletionSource<bool>();
        TimeSpan? start = null;
        Seek(TimeSpan.Zero);

        void Frame(TimeSpan now)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                done.TrySetResult(false);
                return;
            }

            start ??= now;
            TimeSpan elapsed = now - start.Value;
            if (elapsed >= Duration)
            {
                Seek(Duration);
                done.TrySetResult(true);
                return;
            }

            Seek(elapsed);
            top.RequestAnimationFrame(Frame);
        }

        top.RequestAnimationFrame(Frame);
        return done.Task;
    }
}

/// <summary>
/// Plays storyboards on controls one at a time per channel: a second tick on a row stops the first
/// rather than fighting it for the same transform.
/// </summary>
public static class MotionPlayer
{
    private static readonly ConditionalWeakTable<Visual, Dictionary<string, CancellationTokenSource>> Running = [];

    /// <summary>
    /// Takes over playing, for rendering an interaction frame by frame: the storyboards the screens
    /// start are handed here instead of to the clock, and finish when the task returned does.
    /// </summary>
    public static Func<Visual, string, Storyboard, Task<bool>>? Interceptor { get; set; }

    /// <summary>Plays <paramref name="storyboard"/> on <paramref name="owner"/>, stopping what ran there on <paramref name="channel"/>.</summary>
    public static async Task<bool> Play(Visual owner, string channel, Storyboard storyboard)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(storyboard);
        if (Interceptor is { } intercept)
        {
            return await intercept(owner, channel, storyboard).ConfigureAwait(true);
        }

        Dictionary<string, CancellationTokenSource> channels = Running.GetOrCreateValue(owner);
        if (channels.Remove(channel, out CancellationTokenSource? previous))
        {
            await previous.CancelAsync().ConfigureAwait(true);
            previous.Dispose();
        }

        using var cancel = new CancellationTokenSource();
        channels[channel] = cancel;
        try
        {
            return await storyboard.PlayAsync(owner, cancel.Token).ConfigureAwait(true);
        }
        finally
        {
            if (channels.TryGetValue(channel, out CancellationTokenSource? current) && ReferenceEquals(current, cancel))
            {
                channels.Remove(channel);
            }
        }
    }

    /// <summary>Stops whatever runs on <paramref name="owner"/>'s <paramref name="channel"/>, leaving it where it is.</summary>
    public static void Stop(Visual owner, string channel)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (Running.TryGetValue(owner, out Dictionary<string, CancellationTokenSource>? channels) &&
            channels.Remove(channel, out CancellationTokenSource? running))
        {
            running.Cancel();
            running.Dispose();
        }
    }
}

/// <summary>
/// The translate and scale motion moves a control by, kept apart from anything else so a control's
/// own layout is never touched: everything here is a render transform.
/// </summary>
public sealed class MotionTransform
{
    private static readonly ConditionalWeakTable<Visual, MotionTransform> Attached = [];

    private readonly TranslateTransform _translate = new();
    private readonly ScaleTransform _scale = new();

    private MotionTransform(Visual visual)
    {
        visual.RenderTransform = new TransformGroup { Children = { _scale, _translate } };
    }

    /// <summary>The control's motion transform, created on first use.</summary>
    /// <remarks>
    /// A control that already carries a transform of its own (the image viewer's zoom) is not
    /// given one: the two would fight, and nothing in the spec moves those controls.
    /// </remarks>
    public static MotionTransform For(Visual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        if (Attached.TryGetValue(visual, out MotionTransform? existing) &&
            visual.RenderTransform is TransformGroup group && group.Children.Contains(existing._translate))
        {
            return existing;
        }

        var created = new MotionTransform(visual);
        Attached.AddOrUpdate(visual, created);
        return created;
    }

    public double X { get => _translate.X; set => _translate.X = value; }

    public double Y { get => _translate.Y; set => _translate.Y = value; }

    public double ScaleX { get => _scale.ScaleX; set => _scale.ScaleX = value; }

    public double ScaleY { get => _scale.ScaleY; set => _scale.ScaleY = value; }

    public double Scale
    {
        set
        {
            _scale.ScaleX = value;
            _scale.ScaleY = value;
        }
    }

    /// <summary>Back to where layout put it.</summary>
    public void Reset()
    {
        X = 0;
        Y = 0;
        Scale = 1;
    }
}
