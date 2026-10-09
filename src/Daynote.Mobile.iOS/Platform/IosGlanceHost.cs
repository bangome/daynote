using System.Runtime.InteropServices;
using System.Text.Json;
using Daynote.App.Glance;
using Daynote.Mobile.Platform;
using Foundation;
using WatchConnectivity;

namespace Daynote.Mobile.iOS.Platform;

/// <summary>
/// The iPhone's widgets, Live Activity and watch, as the shared app sees them
/// (docs/APPLE_EXTENSIONS.md).
/// </summary>
/// <remarks>
/// <para>
/// The folder is <c>Glance</c> inside the App Group container, which the widget extension and the
/// watch relay can read too. A build signed without the App Group — an unsigned simulator build —
/// gets no container, and then there is simply no folder and nothing is written.
/// </para>
/// <para>
/// WidgetKit's reload and ActivityKit are Swift-only, so they are reached through the two C
/// functions <c>DaynoteBridge.framework</c> exports — looked up at run time in the framework the
/// app bundle carries, never declared with <c>[DllImport]</c>. A declared import is a symbol the
/// native link requires, so a build without the framework (a plain <c>dotnet build</c> that skipped
/// the native targets) would fail to link rather than run without it. Without it the widgets
/// refresh on their own timeline instead of at once.
/// </para>
/// </remarks>
internal sealed class IosGlanceHost : IGlanceHost
{
    /// <summary>The group every Daynote target on the device shares; the same string is in each entitlements file.</summary>
    internal const string AppGroup = "group.cc.arachat.daynote";

    private readonly IosWatchRelay _watch;

    public IosGlanceHost()
    {
        Folder = NSFileManager.DefaultManager.GetContainerUrl(AppGroup)?.Path is { } container
            ? Path.Combine(container, "Glance")
            : null;
        _watch = new IosWatchRelay(this);
    }

    public string? Folder { get; }

    public event EventHandler? ActionsArrived;

    /// <summary>Starts the watch session. Called during launch, so a transfer waiting for the app is delivered.</summary>
    public void Start() => _watch.Activate();

    public void Published(string snapshotJson)
    {
        daynote_glance_reload();
        daynote_glance_sync_activity();
        _watch.Send(snapshotJson);
    }

    /// <summary>
    /// The app came back to the foreground: a Live Activity may be due, or over, without the
    /// snapshot having changed.
    /// </summary>
    public void Resumed() => daynote_glance_sync_activity();

    /// <summary>
    /// An action from the watch or a notification: written into the queue on the caller's thread,
    /// at once, and only the signal to drain is posted to the main thread.
    /// </summary>
    internal void Enqueue(GlanceAction action)
    {
        if (Folder is not { } folder)
        {
            return;
        }

        new GlanceFolder(folder).Enqueue(action);
        NSRunLoop.Main.BeginInvokeOnMainThread(() => ActionsArrived?.Invoke(this, EventArgs.Empty));
    }

    private static unsafe void daynote_glance_reload()
    {
        if (Bridge.Reload != 0)
        {
            ((delegate* unmanaged<void>)Bridge.Reload)();
        }
    }

    private static unsafe void daynote_glance_sync_activity()
    {
        if (Bridge.SyncActivity != 0)
        {
            ((delegate* unmanaged<void>)Bridge.SyncActivity)();
        }
    }

    /// <summary>The two exports, found once; zero when the framework is not in the bundle.</summary>
    private static class Bridge
    {
        internal static readonly nint Reload;
        internal static readonly nint SyncActivity;

        static Bridge()
        {
            string path = Path.Combine(NSBundle.MainBundle.PrivateFrameworksPath ?? string.Empty, "DaynoteBridge.framework", "DaynoteBridge");
            if (!NativeLibrary.TryLoad(path, out nint library))
            {
                System.Diagnostics.Trace.TraceWarning("DaynoteBridge is not in the bundle; widgets will refresh on their own timeline.");
                return;
            }

            NativeLibrary.TryGetExport(library, "daynote_glance_reload", out Reload);
            NativeLibrary.TryGetExport(library, "daynote_glance_sync_activity", out SyncActivity);
        }
    }
}

/// <summary>
/// The phone's half of the watch relay (docs/APPLE_EXTENSIONS.md §5): the snapshot goes out as the
/// application context, and actions come in as user-info transfers.
/// </summary>
/// <remarks>
/// The context is the right channel for a snapshot because the latest replaces any not yet
/// delivered. A transfer is the right one for an action because the system queues every one and
/// hands them over in order, including those sent while the phone was away; they arrive the next
/// time this app runs.
/// </remarks>
internal sealed class IosWatchRelay(IosGlanceHost host) : WCSessionDelegate
{
    private const string SnapshotKey = "snapshot";
    private const string ActionKey = "glanceAction";

    private string? _pending;

    public void Activate()
    {
        if (!WCSession.IsSupported)
        {
            return;
        }

        WCSession.DefaultSession.Delegate = this;
        WCSession.DefaultSession.ActivateSession();
    }

    public void Send(string snapshotJson)
    {
        _pending = snapshotJson;
        Flush();
    }

    private void Flush()
    {
        if (!WCSession.IsSupported || _pending is not { } json)
        {
            return;
        }

        WCSession session = WCSession.DefaultSession;
        if (session.ActivationState != WCSessionActivationState.Activated || !session.Paired || !session.WatchAppInstalled)
        {
            return;
        }

        var context = new NSDictionary<NSString, NSObject>(new NSString(SnapshotKey), new NSString(json));
        if (!session.UpdateApplicationContext(context, out NSError? error))
        {
            System.Diagnostics.Trace.TraceWarning($"Watch context not sent: {error?.LocalizedDescription}");
            return;
        }

        _pending = null;
    }

    public override void ActivationDidComplete(WCSession session, WCSessionActivationState activationState, NSError? error) =>
        NSRunLoop.Main.BeginInvokeOnMainThread(Flush);

    public override void DidBecomeInactive(WCSession session)
    {
    }

    public override void DidDeactivate(WCSession session) => session.ActivateSession();

    /// <summary>A watch that was just paired or had the app installed is sent the day straight away.</summary>
    public override void SessionWatchStateDidChange(WCSession session) => NSRunLoop.Main.BeginInvokeOnMainThread(Flush);

    public override void DidReceiveUserInfo(WCSession session, NSDictionary<NSString, NSObject> userInfo)
    {
        if (userInfo.ObjectForKey(new NSString(ActionKey)) is not NSString json)
        {
            return;
        }

        GlanceAction? action;
        try
        {
            action = JsonSerializer.Deserialize(json.ToString(), GlanceJson.Default.GlanceAction);
        }
        catch (JsonException)
        {
            return;
        }

        if (action is not null)
        {
            host.Enqueue(action);
        }
    }
}
