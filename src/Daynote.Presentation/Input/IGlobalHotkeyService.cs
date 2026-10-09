namespace Daynote.App.Input;

/// <summary>Outcome of trying to register a global hotkey.</summary>
public enum HotkeySetResult
{
    /// <summary>Registered (or queued until the window handle is attached).</summary>
    Ok,

    /// <summary>The OS refused the chord — another application already owns it. Prior registration kept.</summary>
    Conflict,

    /// <summary>The chord is not a registrable hotkey (no modifier, or a modifier-only combo).</summary>
    Invalid,
}

/// <summary>
/// Owns the single system-wide "summon" hotkey. Registered against the resident window handle so it
/// fires even while the window is hidden to the tray; the press is surfaced as <see cref="Pressed"/>
/// for the lifecycle to restore the window. The Windows implementation is Win32 <c>RegisterHotKey</c>, the
/// macOS one Carbon <c>RegisterEventHotKey</c>; tests use a recording double.
/// </summary>
public interface IGlobalHotkeyService : IDisposable
{
    /// <summary>Raised on the UI thread each time the registered chord is pressed anywhere in Windows.</summary>
    event EventHandler? Pressed;

    /// <summary>Raised for the fixed Alt+` quick-note chord: create today's note and open it as a post-it.</summary>
    event EventHandler? QuickNotePressed;

    /// <summary>The chord currently registered (or queued for registration), if any.</summary>
    Hotkey? Current { get; }

    /// <summary>Binds the service to the resident window handle (Windows) and applies any queued hotkey. Other platforms ignore the handle.</summary>
    void Attach(nint hwnd);

    /// <summary>Registers <paramref name="hotkey"/>, replacing the previous one; keeps the old on conflict.</summary>
    HotkeySetResult TrySet(Hotkey hotkey);

    /// <summary>
    /// Raised for the quick-capture chord, which opens the menu bar / tray popover (menu bar design
    /// §01). A second configurable chord beside the summon key rather than a replacement for it.
    /// </summary>
    /// <remarks>
    /// Default members, so the WPF shell and the test doubles that predate the popover keep compiling
    /// and simply never fire it.
    /// </remarks>
    event EventHandler? CapturePressed
    {
        add { }
        remove { }
    }

    /// <summary>The quick-capture chord currently registered, if any.</summary>
    Hotkey? CurrentCapture => null;

    /// <summary>
    /// Registers the quick-capture chord, replacing the previous one. Refused as a conflict when it is
    /// the summon chord or the fixed quick-note chord, as well as when the OS refuses it.
    /// </summary>
    HotkeySetResult TrySetCapture(Hotkey hotkey) => HotkeySetResult.Invalid;
}
