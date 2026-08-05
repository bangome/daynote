namespace Daynote.Core.Diagnostics;

/// <summary>
/// Lifecycle diagnostic events. The set is fixed and payload-free by construction: a caller can only
/// name an event and an optional numeric code, never note or clipboard content, titles, or queries.
/// </summary>
public enum LifecycleEvent
{
    PrimaryInstanceStarted = 1,
    SecondaryInstanceActivatedPrimary = 2,
    ConsentGranted = 3,
    ConsentDeclined = 4,
    CapturePaused = 5,
    CaptureResumed = 6,
    WindowHiddenToTray = 7,
    WindowShownFromTray = 8,
    QuitRequested = 9,
    QuitBlockedByFlushFailure = 10,
    QuitCompleted = 11,
    StartupStateReported = 12,
    CaptureStateApplied = 13,
}

/// <summary>
/// Payload-free structured logger. The API deliberately accepts only <see cref="LifecycleEvent"/>
/// values and integer codes so no diagnostic can ever carry user payload (DESIGN Section 8).
/// </summary>
public interface ISanitizedLog
{
    void Record(LifecycleEvent lifecycleEvent);

    void Record(LifecycleEvent lifecycleEvent, long code);
}

/// <summary>A logger that discards every event. Used where diagnostics are not collected.</summary>
public sealed class NullSanitizedLog : ISanitizedLog
{
    public static NullSanitizedLog Instance { get; } = new();

    public void Record(LifecycleEvent lifecycleEvent)
    {
    }

    public void Record(LifecycleEvent lifecycleEvent, long code)
    {
    }
}

/// <summary>Writes each event as a single deterministic, payload-free line to a text writer.</summary>
public sealed class TextWriterSanitizedLog : ISanitizedLog
{
    private readonly TextWriter _writer;
    private readonly object _gate = new();

    public TextWriterSanitizedLog(TextWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public void Record(LifecycleEvent lifecycleEvent)
    {
        lock (_gate)
        {
            _writer.WriteLine($"lifecycle event={lifecycleEvent}");
        }
    }

    public void Record(LifecycleEvent lifecycleEvent, long code)
    {
        lock (_gate)
        {
            _writer.WriteLine($"lifecycle event={lifecycleEvent} code={code}");
        }
    }
}
