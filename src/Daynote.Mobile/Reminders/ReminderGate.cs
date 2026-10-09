using Daynote.Core.Agenda;
using Daynote.Infrastructure.Sync;
using Daynote.Mobile.Platform;

namespace Daynote.Mobile.Reminders;

/// <summary>
/// The last question before a reminder's notification is posted: is it still wanted?
/// </summary>
/// <remarks>
/// <para>
/// An alarm is armed when the app plans, and the app does not always run between then and the
/// moment it fires. A to-do finished from a widget, on another device and pulled while the app was
/// closed, or edited in a way that moved its time leaves an alarm behind that only a later
/// reconciliation would cancel. Asking at the moment of firing closes that gap: the alarm counts
/// only if planning the store as it is now would still produce it.
/// </para>
/// <para>
/// <b>It fails open.</b> A store that cannot be read (no database, an update not yet migrated, an
/// error) lets the reminder through: a reminder for something done is a nuisance, a missing one
/// is a missed appointment.
/// </para>
/// </remarks>
public static class ReminderGate
{
    /// <summary>How far back a reminder missing from <c>reminders.json</c> may have been due.</summary>
    private static readonly TimeSpan Unrecorded = TimeSpan.FromHours(12);

    public static async Task<bool> ShouldNotifyAsync(
        string baseRoot,
        ISecretProtector? protector,
        string id,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        try
        {
            // The planner only plans what is ahead, so it is asked from just before the alarm's own
            // time: an inexact alarm can arrive minutes late and must still recognise itself.
            Reminder? recorded = ReminderStateStore.InFolder(baseRoot).Load().Scheduled
                .FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));
            DateTime from = recorded is { } r ? r.At.AddMinutes(-1) : now - Unrecorded;

            await using BackgroundStore? store = BackgroundStore.Open(baseRoot, protector, out _);
            if (store is null)
            {
                return true;
            }

            if (!await ReminderCoordinator.IsEnabledAsync(store.Settings, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            IReadOnlyList<AgendaItem> items = await store.Agenda.GetAllAsync(cancellationToken).ConfigureAwait(false);
            TimeSpan dateOnlyTime = await ReminderCoordinator.GetDateOnlyTimeAsync(store.Settings, cancellationToken)
                .ConfigureAwait(false);
            return ReminderPlanner
                .Plan(items, new DateTimeOffset(from, TimeZoneInfo.Local.GetUtcOffset(from)), int.MaxValue, dateOnlyTime)
                .Any(planned => string.Equals(planned.Id, id, StringComparison.Ordinal));
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            System.Diagnostics.Trace.TraceError($"Checking a reminder before posting it failed: {exception}");
            return true;
        }
    }
}
