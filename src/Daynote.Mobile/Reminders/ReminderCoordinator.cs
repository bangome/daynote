using Daynote.App.Localization;
using Daynote.Core.Notes;
using Daynote.Core.Settings;
using Daynote.Core.Time;

namespace Daynote.Mobile.Reminders;

/// <summary>
/// Keeps the device's scheduled reminders in step with the notes: reads the to-dos, plans, diffs
/// against <see cref="ReminderStateStore"/> and hands the difference to the platform.
/// </summary>
/// <remarks>
/// <para>
/// The shell calls <see cref="RefreshAsync"/> whenever the notes may have changed — after the to-do
/// lists are rebuilt (which every edit, sync pull and language switch goes through), after a save,
/// on resume and when the setting changes. Runs never overlap: a call during a run is folded into
/// one more run afterwards, so a burst of saves costs two passes, not one each. All calls are
/// expected on the UI thread.
/// </para>
/// <para>
/// Permission is asked for the first time there is something to schedule, and only once; a denial
/// is respected, and nothing is scheduled while notifications are not allowed. Turning the setting
/// off plans nothing, which cancels everything scheduled.
/// </para>
/// </remarks>
public sealed class ReminderCoordinator
{
    /// <summary>The settings-store key; anything but "off" (including no row) means on.</summary>
    public const string EnabledKey = "mobile.reminders.enabled";

    /// <summary>The time a date-only to-do reminds at, as "HH:mm"; no row means <see cref="ReminderPlanner.DefaultDateOnlyTime"/>.</summary>
    public const string DateOnlyTimeKey = "mobile.reminders.time";

    private readonly IReminderScheduler _scheduler;
    private readonly INoteRepository _repository;
    private readonly ISettingsStore _settings;
    private readonly IClock _clock;
    private readonly ReminderStateStore _store;
    private Task? _run;
    private bool _again;
    private IReadOnlyList<NoteSummary>? _latestNotes;
    private bool _closed;
    private ExactAlarmState? _lastExact;

    public ReminderCoordinator(
        IReminderScheduler scheduler, INoteRepository repository, ISettingsStore settings, IClock clock, ReminderStateStore store)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>The permission as of the last run, for the settings row.</summary>
    public ReminderPermission Permission { get; private set; } = ReminderPermission.NotDetermined;

    /// <summary>Raised when <see cref="Permission"/> changes.</summary>
    public event EventHandler? PermissionChanged;

    public void OpenSystemSettings() => _scheduler.OpenSystemSettings();

    /// <summary>Whether reminders fire on the minute; see <see cref="IReminderScheduler.ExactAlarms"/>.</summary>
    public ExactAlarmState ExactAlarms => _scheduler.ExactAlarms;

    public void OpenExactAlarmSettings() => _scheduler.OpenExactAlarmSettings();

    public static async Task<bool> IsEnabledAsync(ISettingsStore settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? value = await settings.GetAsync(EnabledKey, cancellationToken).ConfigureAwait(true);
        return !string.Equals(value, "off", StringComparison.Ordinal);
    }

    public static async Task<TimeSpan> GetDateOnlyTimeAsync(ISettingsStore settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? value = await settings.GetAsync(DateOnlyTimeKey, cancellationToken).ConfigureAwait(true);
        return TimeSpan.TryParseExact(value, @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture, out TimeSpan time)
            && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1)
                ? time
                : ReminderPlanner.DefaultDateOnlyTime;
    }

    /// <summary>Persists the date-only time and reschedules every date-only reminder at once.</summary>
    public async Task SetDateOnlyTimeAsync(TimeSpan time)
    {
        if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(time));
        }

        await _settings.SetAsync(DateOnlyTimeKey, time.ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Persists the switch and reconciles at once: off cancels every scheduled reminder.</summary>
    public async Task SetEnabledAsync(bool enabled)
    {
        await _settings.SetAsync(EnabledKey, enabled ? "on" : "off").ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Brings the scheduled reminders up to date.
    /// </summary>
    /// <param name="notes">
    /// Every note, when the caller has just read them anyway (the to-do refresh does); null reads
    /// them from the repository.
    /// </param>
    public Task RefreshAsync(IReadOnlyList<NoteSummary>? notes = null)
    {
        _latestNotes = notes;
        if (_run is { IsCompleted: false } running)
        {
            _again = true;
            return running;
        }

        _run = RunLoopAsync();
        return _run;
    }

    /// <summary>
    /// Cancels everything this device scheduled and stops: the profile is about to be closed, and
    /// its to-dos must not remind under the next one (sign-in, sign-out, a switch).
    /// </summary>
    public async Task ClearAsync()
    {
        _closed = true;
        if (_run is { } running)
        {
            await running.ConfigureAwait(true);
        }

        try
        {
            ReminderState state = _store.Load();
            if (state.Scheduled.Count > 0)
            {
                await _scheduler.ApplyAsync(new ReminderChanges(
                    [], [.. state.Scheduled.Select(r => r.Id)], state.ChannelName, state.ChannelDescription)).ConfigureAwait(true);
            }

            _store.Save(state with { Scheduled = [] });
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            System.Diagnostics.Trace.TraceError($"Clearing reminders failed: {exception}");
        }
    }

    private async Task RunLoopAsync()
    {
        do
        {
            _again = false;
            IReadOnlyList<NoteSummary>? notes = _latestNotes;
            _latestNotes = null;
            try
            {
                await RunOnceAsync(notes).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Nothing waits on a reminder pass; the next one starts from the state file again.
                System.Diagnostics.Trace.TraceError($"Reminder refresh failed: {exception}");
            }
        }
        while (_again && !_closed);
    }

    private async Task RunOnceAsync(IReadOnlyList<NoteSummary>? notes)
    {
        if (_closed)
        {
            return;
        }

        ReminderState state = _store.Load();
        ReminderPermission permission = await _scheduler.GetPermissionAsync().ConfigureAwait(true);
        IReadOnlyList<Reminder> desired = [];

        if (await IsEnabledAsync(_settings).ConfigureAwait(true))
        {
            notes ??= await _repository.GetAllNotesAsync().ConfigureAwait(true);
            ClockSnapshot snapshot = _clock.Read();
            DateTimeOffset now = snapshot.UtcInstant.ToOffset(snapshot.LocalUtcOffset);
            TimeSpan dateOnlyTime = await GetDateOnlyTimeAsync(_settings).ConfigureAwait(true);
            desired = ReminderPlanner.Plan(notes, now, _scheduler.Capacity, dateOnlyTime);

            // The first time there is something to remind about, and never again.
            if (desired.Count > 0 && permission == ReminderPermission.NotDetermined && !state.PermissionAsked)
            {
                state = state with { PermissionAsked = true };
                _store.Save(state);
                permission = await _scheduler.RequestPermissionAsync().ConfigureAwait(true);
                if (permission == ReminderPermission.NotDetermined)
                {
                    // The prompt could not be shown (no activity on screen); it has not been asked.
                    state = state with { PermissionAsked = false };
                    _store.Save(state);
                }
            }
        }

        // Android cannot tell "denied" from "never asked"; having asked, it is a denial.
        if (permission == ReminderPermission.NotDetermined && state.PermissionAsked)
        {
            permission = ReminderPermission.Denied;
        }

        SetPermission(permission);
        if (permission != ReminderPermission.Granted || _closed)
        {
            desired = [];
        }

        string channel = AppStrings.ReminderChannelName;
        string description = AppStrings.ReminderChannelDescription;
        ReminderChanges changes = ReminderPlanner.Diff(state.Scheduled, desired, channel, description);

        // Exact alarms were just allowed: every alarm armed inexact is armed again, now exact. The
        // platform's own broadcast does this too; this covers a build or OS where it never arrives.
        ExactAlarmState exact = _scheduler.ExactAlarms;
        if (_lastExact == ExactAlarmState.NotAllowed && exact == ExactAlarmState.Allowed)
        {
            changes = changes with { Schedule = desired };
        }

        _lastExact = exact;
        if (!changes.IsEmpty || !string.Equals(channel, state.ChannelName, StringComparison.Ordinal))
        {
            await _scheduler.ApplyAsync(changes).ConfigureAwait(true);
        }

        _store.Save(state with { ChannelName = channel, ChannelDescription = description, Scheduled = desired });
    }

    private void SetPermission(ReminderPermission permission)
    {
        if (Permission != permission)
        {
            Permission = permission;
            PermissionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
