namespace Daynote.Mobile.Platform;

/// <summary>
/// The OS side of the phone's widgets and watch: where the shared folder is, and what to poke
/// once a new snapshot is in it (docs/APPLE_EXTENSIONS.md).
/// </summary>
/// <remarks>
/// iOS fills this in with the App Group container, WidgetKit's reload, the watch relay and the
/// Live Activity; a head without extensions leaves it null and nothing is written anywhere.
/// </remarks>
public interface IGlanceHost
{
    /// <summary>
    /// The folder the app and its extensions share, or null when the build has no access to one
    /// (an unsigned simulator build without the App Group entitlement).
    /// </summary>
    string? Folder { get; }

    /// <summary>
    /// A new snapshot was written: reload the widgets, hand it to the watch, start or end the Live
    /// Activity. Called on the UI thread, after the file is in place.
    /// </summary>
    void Published(string snapshotJson);

    /// <summary>
    /// Raised when something put actions in the queue while the app is running — the watch relay,
    /// a notification action — so they are carried out now rather than on the next resume.
    /// </summary>
    event EventHandler? ActionsArrived;
}
