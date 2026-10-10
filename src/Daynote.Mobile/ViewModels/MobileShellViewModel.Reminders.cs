using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Localization;
using Daynote.App.Shell.Product;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Mobile.Reminders;

namespace Daynote.Mobile.ViewModels;

/// <summary>
/// To-do reminders: the settings row, the moments that reconcile what is scheduled, and the tap on
/// a notification that brings its note back.
/// </summary>
public sealed partial class MobileShellViewModel
{
    /// <summary>Keeps the device's reminders in step with the notes, or null on a head without notifications. Set by composition.</summary>
    public ReminderCoordinator? Reminders
    {
        get => _reminders;
        init
        {
            _reminders = value;
            if (value is not null)
            {
                value.PermissionChanged += (_, _) => RefreshReminderRow();
            }
        }
    }

    private readonly ReminderCoordinator? _reminders;

    public bool HasReminders => Reminders is not null;

    /// <summary>The switch; on until turned off.</summary>
    [ObservableProperty]
    private bool _remindersEnabled = true;

    /// <summary>The OS does not allow Daynote's notifications, so the switch alone would do nothing.</summary>
    public bool IsReminderPermissionDenied => Reminders is { Permission: ReminderPermission.Denied };

    /// <summary>What the row says under its title: what the switch does, or why it cannot.</summary>
    public string ReminderStatusText => IsReminderPermissionDenied ? AppStrings.ReminderSettingsDenied : AppStrings.ReminderSettingsHint;

    /// <summary>When a to-do with a date and no time reminds; 09:00 until changed.</summary>
    [ObservableProperty]
    private TimeSpan _reminderTime = ReminderPlanner.DefaultDateOnlyTime;

    /// <summary>"09:00", as the row shows it.</summary>
    public string ReminderTimeText => FormatTime(ReminderTime);

    partial void OnReminderTimeChanged(TimeSpan value) => OnPropertyChanged(nameof(ReminderTimeText));

    /// <summary>The default time row, which has nothing to apply to while reminders are off.</summary>
    public bool ShowReminderTimeRow => HasReminders && RemindersEnabled;

    /// <summary>
    /// "정확한 시각에 알림": only on Android 12+ while exact alarms are not allowed, and only while
    /// reminders are on. Gone once allowed, rather than a row that says all is well.
    /// </summary>
    public bool ShowPreciseRemindersRow =>
        RemindersEnabled && Reminders is { ExactAlarms: ExactAlarmState.NotAllowed };

    /// <summary>Opens the system page for exact alarms. Only ever from the row, never by itself.</summary>
    [RelayCommand]
    private void OpenExactAlarmSettings() => Reminders?.OpenExactAlarmSettings();

    partial void OnRemindersEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowReminderTimeRow));
        OnPropertyChanged(nameof(ShowPreciseRemindersRow));
        if (!_loading && Reminders is { } reminders)
        {
            _ = RunQuietlyAsync(() => reminders.SetEnabledAsync(value));
        }
    }

    [RelayCommand]
    private void OpenNotificationSettings() => Reminders?.OpenSystemSettings();

    private void RefreshReminderRow()
    {
        OnPropertyChanged(nameof(IsReminderPermissionDenied));
        OnPropertyChanged(nameof(ReminderStatusText));
        OnPropertyChanged(nameof(ShowPreciseRemindersRow));
    }

    private async Task LoadReminderSettingAsync(CancellationToken cancellationToken)
    {
        if (Reminders is not null)
        {
            RemindersEnabled = await ReminderCoordinator.IsEnabledAsync(_settings, cancellationToken).ConfigureAwait(true);
            ReminderTime = await ReminderCoordinator.GetDateOnlyTimeAsync(_settings, cancellationToken).ConfigureAwait(true);
        }
    }

    // ── The default reminder time sheet ──────────────────────────────────────────────────────────
    // A bottom sheet like the month sheet: twenty-four hour pills and four minute pills, every one a
    // full-size target, and nothing applied until 완료. A spinning wheel would be the platform's own
    // idiom, but Avalonia has no touch-native one, and a grid of pills is two taps for any time.

    [ObservableProperty]
    private bool _isReminderTimeSheetOpen;

    /// <summary>The hour being chosen in the sheet, apart from <see cref="ReminderTime"/> until 완료.</summary>
    [ObservableProperty]
    private int _draftReminderHour;

    [ObservableProperty]
    private int _draftReminderMinute;

    /// <summary>The draft, large at the top of the sheet.</summary>
    public string DraftReminderTimeText => FormatTime(new TimeSpan(DraftReminderHour, DraftReminderMinute, 0));

    public IReadOnlyList<TimeOption> ReminderHourOptions { get; } =
        [.. Enumerable.Range(0, 24).Select(hour => new TimeOption(hour, hour.ToString("00", System.Globalization.CultureInfo.InvariantCulture)))];

    public IReadOnlyList<TimeOption> ReminderMinuteOptions { get; } =
        [.. new[] { 0, 15, 30, 45 }.Select(minute => new TimeOption(minute, minute.ToString("00", System.Globalization.CultureInfo.InvariantCulture)))];

    partial void OnIsReminderTimeSheetOpenChanged(bool value) => OnPropertyChanged(nameof(ShowDock));

    partial void OnDraftReminderHourChanged(int value) => MarkDraftTime();

    partial void OnDraftReminderMinuteChanged(int value) => MarkDraftTime();

    private void MarkDraftTime()
    {
        foreach (TimeOption option in ReminderHourOptions)
        {
            option.IsCurrent = option.Value == DraftReminderHour;
        }

        foreach (TimeOption option in ReminderMinuteOptions)
        {
            option.IsCurrent = option.Value == DraftReminderMinute;
        }

        OnPropertyChanged(nameof(DraftReminderTimeText));
    }

    [RelayCommand]
    private void OpenReminderTimeSheet()
    {
        DraftReminderHour = ReminderTime.Hours;

        // A time stored some other way may sit between the quarter hours; the sheet starts on the
        // quarter below it.
        DraftReminderMinute = ReminderTime.Minutes / 15 * 15;
        MarkDraftTime();
        IsReminderTimeSheetOpen = true;
    }

    [RelayCommand]
    private void CloseReminderTimeSheet() => IsReminderTimeSheetOpen = false;

    [RelayCommand]
    private void PickReminderHour(int hour) => DraftReminderHour = hour;

    [RelayCommand]
    private void PickReminderMinute(int minute) => DraftReminderMinute = minute;

    /// <summary>완료: keeps the time and reschedules every date-only reminder at once.</summary>
    [RelayCommand]
    private async Task CommitReminderTime()
    {
        IsReminderTimeSheetOpen = false;
        var time = new TimeSpan(DraftReminderHour, DraftReminderMinute, 0);
        if (time == ReminderTime || Reminders is not { } reminders)
        {
            return;
        }

        ReminderTime = time;
        await reminders.SetDateOnlyTimeAsync(time).ConfigureAwait(true);
    }

    private static string FormatTime(TimeSpan time) =>
        time.ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Reconciles in the background; <paramref name="notes"/> when every note was just read anyway.</summary>
    private void RefreshReminders(IReadOnlyList<Daynote.Core.Agenda.AgendaItem>? items = null)
    {
        if (Reminders is { } reminders && !_disposed)
        {
            _ = RunQuietlyAsync(() => reminders.RefreshAsync(items));
        }
    }

    /// <summary>
    /// A reminder was tapped: show its day and open its note, over whatever was on screen.
    /// </summary>
    public async Task OpenReminderAsync(LocalDate date, Guid noteId)
    {
        // The open note is saved before anything moves, as every other way off the editor does.
        // A save that fails keeps the editor up with its retry, and the tap goes no further.
        await CloseEditorAsync().ConfigureAwait(true);
        if (IsEditorOpen)
        {
            return;
        }

        GoToPage(MobilePage.Day);
        if (await SelectDateAsync(date).ConfigureAwait(true))
        {
            await OpenByIdAsync(noteId).ConfigureAwait(true);
        }
    }
}
