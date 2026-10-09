using Avalonia.Threading;
using Daynote.App.Localization;
using Daynote.Core.Agenda;
using Daynote.Infrastructure.Sync;
using Daynote.Mobile.Platform;
using Daynote.Mobile.Reminders;

namespace Daynote.Mobile.Widgets;

/// <summary>What a tap on a widget's ring did.</summary>
/// <param name="Changed">False when the row was gone, already in the state asked for, or locked.</param>
/// <param name="CancelledReminders">
/// Reminder ids taken out of <c>reminders.json</c> because the row was just finished with the app
/// not running; the head cancels their alarms.
/// </param>
public sealed record WidgetTick(bool Changed, IReadOnlyList<string> CancelledReminders)
{
    public static WidgetTick None { get; } = new(false, []);
}

/// <summary>
/// What the home-screen widgets read and write, through <see cref="BackgroundStore"/>: the active
/// profile's own store, with no network, no UI and no migration.
/// </summary>
/// <remarks>
/// <b>A tick goes through the same store the app ticks through</b>, so the outbox triggers queue it
/// for sync exactly as an in-app tick is queued. With the app running the shell does it, which also
/// redraws the app, reconciles the reminders and asks for a sync soon. Without it, the finished
/// row's reminders are taken out here, since nothing else would before they fire.
/// </remarks>
public static class WidgetData
{
    /// <summary>The snapshot for the active profile, as of <paramref name="now"/>.</summary>
    public static async Task<WidgetSnapshot> ReadAsync(
        string baseRoot,
        ISecretProtector? protector,
        DateTime now,
        IReadOnlyCollection<WidgetSettling> settling,
        CancellationToken cancellationToken = default)
    {
        await using BackgroundStore? store = BackgroundStore.Open(baseRoot, protector, out bool outdated);
        if (outdated)
        {
            return WidgetSnapshot.Outdated(now, AppLanguages.FromSystem());
        }

        if (store is null)
        {
            // No database yet: the app has never run on this profile.
            return WidgetSnapshot.Build(now, [], [], AppLanguages.FromSystem());
        }

        AppLanguage language = store.IsLive
            ? LocalizationService.Instance.Language
            : await LanguageStartup.ResolveAsync(store.Settings, cancellationToken).ConfigureAwait(false);

        if (await store.IsLockedAsync(cancellationToken).ConfigureAwait(false))
        {
            return WidgetSnapshot.Locked(now, language);
        }

        IReadOnlyList<AgendaItem> items = await store.Agenda.GetAllAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<AgendaList> lists = await store.Agenda.GetListsAsync(cancellationToken).ConfigureAwait(false);
        return WidgetSnapshot.Build(now, items, lists, language, settling);
    }

    /// <summary>
    /// Sets the row <paramref name="key"/> stands for on <paramref name="day"/> to done, or back to
    /// open.
    /// </summary>
    /// <param name="complete">
    /// The state the tapped widget showed the opposite of. A set rather than a flip: a widget drawn
    /// before the row was finished elsewhere must not reopen it.
    /// </param>
    public static async Task<WidgetTick> SetDoneAsync(
        string baseRoot,
        ISecretProtector? protector,
        WidgetRowKey key,
        DateOnly day,
        bool complete,
        CancellationToken cancellationToken = default)
    {
        await using BackgroundStore? store = BackgroundStore.Open(baseRoot, protector, out _);
        if (store is null || await store.IsLockedAsync(cancellationToken).ConfigureAwait(false))
        {
            return WidgetTick.None;
        }

        IReadOnlyList<AgendaItem> items = await store.Agenda.GetAllAsync(cancellationToken).ConfigureAwait(false);
        AgendaDayView view = AgendaDay.For(day, items);
        AgendaDayRow? found = view.Open.Concat(view.Done)
            .Cast<AgendaDayRow?>()
            .FirstOrDefault(row => row is { } r && WidgetRowKey.Of(r) == key);
        if (found is not { } row || row.Item.Kind != AgendaKind.Task || row.IsDone == complete)
        {
            return WidgetTick.None;
        }

        if (store.Shell is { } shell)
        {
            await Dispatcher.UIThread.InvokeAsync(() => shell.ToggleFromWidgetAsync(row));
            return new WidgetTick(true, []);
        }

        await new ToggleAgendaItem(store.Agenda).ToggleAsync(row, cancellationToken).ConfigureAwait(false);
        return new WidgetTick(true, complete ? Unschedule(baseRoot, row) : []);
    }

    /// <summary>
    /// Takes a finished row's reminders out of <c>reminders.json</c> and returns their ids.
    /// </summary>
    /// <remarks>
    /// Only on the way to done. Reopening a row with the app not running leaves its reminder to
    /// the app's next reconciliation, which is the same wait any other change made outside the app
    /// has; a reminder that fires for something already finished is the one that cannot wait.
    /// </remarks>
    internal static IReadOnlyList<string> Unschedule(string baseRoot, AgendaDayRow row)
    {
        var store = ReminderStateStore.InFolder(baseRoot);
        ReminderState state = store.Load();
        var ids = new HashSet<string>(ReminderPlanner.IdsFor(row), StringComparer.Ordinal);
        string[] gone = [.. state.Scheduled.Where(r => ids.Contains(r.Id)).Select(static r => r.Id)];
        if (gone.Length > 0)
        {
            store.Save(state with { Scheduled = [.. state.Scheduled.Where(r => !ids.Contains(r.Id))] });
        }

        return gone;
    }
}
