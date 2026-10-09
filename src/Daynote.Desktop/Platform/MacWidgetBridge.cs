using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Daynote.App.Composition;
using Daynote.App.Glance;
using Daynote.App.Localization;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Core.Time;
using Daynote.Desktop.ViewModels;

namespace Daynote.Desktop.Platform;

/// <summary>
/// The Mac app's side of its desktop widgets (native/mac, docs/APPLE_EXTENSIONS.md "macOS"): the
/// phone's glance contract — <see cref="GlanceSnapshotBuilder"/> into <see cref="GlanceFolder"/>,
/// drained by <see cref="GlanceActionApplier"/> — plus the WidgetKit reload and the links a widget
/// opens a day with.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only in a bundle that can have widgets.</b> They exist only when Build-MacApp.sh embedded
/// and team-signed the extension; it then writes the group's name into Info.plist under
/// <c>DaynoteAppGroup</c>. An ad-hoc build has no team and so no group — and on macOS 15 touching
/// another app's group folder without one raises a privacy prompt — so without that key this does
/// nothing at all.
/// </para>
/// <para>
/// <b>Riding on the panel's refresh.</b> Every edit, tick, sync and capture already ends in
/// <see cref="Daynote.App.Shell.Product.TodoPanelViewModel.RefreshAsync"/>, and its
/// <c>Refreshed</c> event comes after every item has been read, so no edit path can forget the
/// widgets. The snapshot is published only when its content changed: each WidgetKit reload spends
/// some of the extension's daily budget.
/// </para>
/// </remarks>
public sealed partial class MacWidgetBridge : IDisposable
{
    /// <summary>The folder inside the group container, named as the phone's host names it.</summary>
    public const string GlanceFolderName = "Glance";

    private const string Library = "libDaynoteWidgetBridge.dylib";

    private readonly DesktopShellViewModel _shell;
    private readonly IAgendaRepository _agenda;
    private readonly INoteRepository _notes;
    private readonly IClock _clock;
    private readonly GlanceFolder _folder;
    private readonly FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _midnight;
    private string? _published;
    private bool _draining;

    private MacWidgetBridge(
        DesktopShellViewModel shell,
        IAgendaRepository agenda,
        INoteRepository notes,
        IClock clock,
        string container)
    {
        _shell = shell;
        _agenda = agenda;
        _notes = notes;
        _clock = clock;
        _folder = new GlanceFolder(Path.Combine(container, GlanceFolderName));

        // A burst of refreshes — typing, a sync pulling twenty items — becomes one write.
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = PublishAsync();
        };

        // "Today" moves at midnight whether or not anything was edited.
        _midnight = new DispatcherTimer();
        _midnight.Tick += (_, _) =>
        {
            ArmMidnight();
            _ = _shell.Todo.RefreshAsync();
        };
        ArmMidnight();

        _shell.Todo.Refreshed += OnChanged;
        LocalizationService.Instance.LanguageChanged += OnChanged;

        try
        {
            Directory.CreateDirectory(_folder.ActionsPath);
            _watcher = new FileSystemWatcher(_folder.ActionsPath, "*.json") { EnableRaisingEvents = true };
            // Renamed, because both sides write to a dot-name and move it into place.
            _watcher.Renamed += (_, _) => Dispatcher.UIThread.Post(() => _ = DrainAsync());
            _watcher.Created += (_, _) => Dispatcher.UIThread.Post(() => _ = DrainAsync());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Actions are still drained on activation; only the immediacy is lost.
            System.Diagnostics.Trace.TraceWarning($"Widget actions not watched: {exception.Message}");
        }
    }

    /// <summary>
    /// The bridge, when this bundle carries widgets and the group folder is reachable; otherwise
    /// null — always, on Windows.
    /// </summary>
    public static MacWidgetBridge? TryAttach(
        Application application,
        DesktopShellViewModel shell,
        IAgendaRepository agenda,
        INoteRepository notes,
        IClock clock,
        Action showWindow)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(showWindow);
        if (!OperatingSystem.IsMacOS() || GroupFromInfoPlist() is not { } group || Container(group) is not { } container)
        {
            return null;
        }

        var bridge = new MacWidgetBridge(shell, agenda, notes, clock, container);

        // A click on a widget arrives as daynote://day?date=… (CFBundleURLTypes in Info.plist).
        if (application.TryGetFeature<IActivatableLifetime>() is { } activatable)
        {
            activatable.Activated += (_, e) =>
            {
                if (e is ProtocolActivatedEventArgs { Kind: ActivationKind.OpenUri } protocol)
                {
                    showWindow();
                    _ = shell.SelectDateAsync(DayFromLink(protocol.Uri) ?? LocalDates.Today(clock));
                }
            };
        }

        _ = bridge.DrainAsync();
        return bridge;
    }

    /// <summary>The window came forward: pick up any check the watcher missed while the Mac slept.</summary>
    public void NotifyActivated() => _ = DrainAsync();

    /// <summary>
    /// The day a widget link names — <c>daynote://day?date=yyyy-MM-dd</c>, or a note's
    /// <c>daynote://note?date=…&amp;id=…</c>, which the desktop opens on its day — or null.
    /// </summary>
    public static LocalDate? DayFromLink(Uri? uri)
    {
        if (uri is not { Scheme: "daynote", Host: "day" or "note" })
        {
            return null;
        }

        foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            if (pair.StartsWith("date=", StringComparison.Ordinal)
                && LocalDate.Parse(Uri.UnescapeDataString(pair[5..])) is { IsSuccess: true } date)
            {
                return date.Value;
            }
        }

        return null;
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private async Task PublishAsync(bool force = false)
    {
        try
        {
            IReadOnlyList<AgendaList> lists = await _agenda.GetListsAsync().ConfigureAwait(true);
            IReadOnlyList<NoteSummary> notes = await _notes.GetAllNotesAsync().ConfigureAwait(true);
            ClockSnapshot clock = _clock.Read();
            GlanceSnapshot snapshot = GlanceSnapshotBuilder.Build(
                _shell.Todo.All,
                lists,
                notes,
                clock.UtcInstant.ToOffset(clock.LocalUtcOffset).DateTime,
                clock.UtcInstant,
                AgendaZone.Local(),
                LocalizationService.Instance.Language,
                _shell.Account is { IsLocked: true });

            // Compared without the stamp, or nothing would ever be the same.
            string content = GlanceSnapshotBuilder.Serialize(snapshot with { GeneratedUtc = string.Empty });
            if (!force && string.Equals(content, _published, StringComparison.Ordinal))
            {
                return;
            }

            _folder.WriteSnapshot(GlanceSnapshotBuilder.Serialize(snapshot));
            _published = content;
            Native.Reload();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            // Nothing waits on a widget; the next change writes the file again.
            System.Diagnostics.Trace.TraceWarning($"Widgets not refreshed: {exception.Message}");
        }
    }

    /// <summary>
    /// Carries out every queued check, oldest first, then rewrites the snapshot from the store,
    /// which replaces the widget's own guess at what its tap did.
    /// </summary>
    private async Task DrainAsync()
    {
        // The watcher fires per file; one drain at a time, and the UI thread is the only caller.
        if (_draining)
        {
            return;
        }

        _draining = true;
        try
        {
            var applier = new GlanceActionApplier(_agenda, AppendNoteLineAsync);
            bool any = false;
            foreach ((string path, GlanceAction? action) in _folder.ReadActions())
            {
                any = true;
                if (action is not null)
                {
                    try
                    {
                        await applier.ApplyAsync(action).ConfigureAwait(true);
                    }
                    catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
                    {
                        // Lost rather than the whole queue stuck behind it, as on the phone.
                        System.Diagnostics.Trace.TraceError($"Applying {action.Type} {action.Id} failed: {exception}");
                    }
                }

                GlanceFolder.Delete(path);
            }

            if (any)
            {
                await _shell.Todo.RefreshAsync().ConfigureAwait(true);
                await PublishAsync(force: true).ConfigureAwait(true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning($"Widget actions not drained: {exception.Message}");
        }
        finally
        {
            _draining = false;
        }
    }

    /// <summary>
    /// "노트에 한 줄", should a capture reach the Mac's queue. The editor is saved first and the day
    /// read again after, so the open note and the line under it cannot become two revisions.
    /// </summary>
    private async Task<bool> AppendNoteLineAsync(string line, DateOnly date, CancellationToken cancellationToken)
    {
        if (!(await _shell.Notes.FlushAsync(FlushReason.NoteChange, cancellationToken).ConfigureAwait(true)).CanProceed)
        {
            return false;
        }

        bool appended = await GlanceNoteLine.AppendAsync(_notes, line, date, cancellationToken).ConfigureAwait(true);
        if (appended && LocalDates.ToDateOnly(_shell.SelectedDate) == date)
        {
            await _shell.Notes.LoadAsync(_shell.SelectedDate, cancellationToken).ConfigureAwait(true);
        }

        return appended;
    }

    private void ArmMidnight()
    {
        ClockSnapshot snapshot = _clock.Read();
        DateTime local = snapshot.UtcInstant.ToOffset(snapshot.LocalUtcOffset).DateTime;
        _midnight.Stop();
        _midnight.Interval = local.Date.AddDays(1).AddSeconds(5) - local;
        _midnight.Start();
    }

    /// <summary>
    /// <c>DaynoteAppGroup</c> from Contents/Info.plist — present only when the build embedded the
    /// widgets and signed them for a team.
    /// </summary>
    private static string? GroupFromInfoPlist()
    {
        string contents = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        string plist = Path.Combine(contents, "Info.plist");
        if (!File.Exists(plist)
            || !File.Exists(Path.Combine(AppContext.BaseDirectory, Library))
            || !Directory.Exists(Path.Combine(contents, "PlugIns", "DaynoteWidgets.appex")))
        {
            return null;
        }

        Match match = GroupKey().Match(File.ReadAllText(plist));
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? Container(string group)
    {
        try
        {
            var buffer = new byte[4096];
            int length = Native.Container(group, buffer, buffer.Length);
            return length > 0 ? Encoding.UTF8.GetString(buffer, 0, length) : null;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            System.Diagnostics.Trace.TraceWarning($"Widget bridge not loaded: {exception.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _shell.Todo.Refreshed -= OnChanged;
        LocalizationService.Instance.LanguageChanged -= OnChanged;
        _debounce.Stop();
        _midnight.Stop();
        _watcher?.Dispose();
    }

    [GeneratedRegex(@"<key>DaynoteAppGroup</key>\s*<string>([^<]+)</string>")]
    private static partial Regex GroupKey();

    private static class Native
    {
        [DllImport(Library, EntryPoint = "daynote_widgets_container")]
        internal static extern int Container([MarshalAs(UnmanagedType.LPUTF8Str)] string group, [Out] byte[] buffer, int capacity);

        [DllImport(Library, EntryPoint = "daynote_widgets_reload")]
        internal static extern void Reload();
    }
}
