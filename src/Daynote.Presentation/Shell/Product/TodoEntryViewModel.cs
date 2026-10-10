using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.App.Notes;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Core.Time;

namespace Daynote.App.Shell.Product;

/// <summary>Which of the sheet's pickers is open under its row. One at a time, or the sheet would outgrow the screen.</summary>
public enum TodoEntryPicker
{
    None,
    Date,
    Time,
    End,
    Repeat,
}

/// <summary>
/// 반복: the repeats the sheet offers, each one an RRULE <see cref="AgendaRecurrence"/> expands.
/// </summary>
public enum TodoRepeat
{
    None,
    Daily,

    /// <summary>Monday to Friday.</summary>
    Weekdays,

    /// <summary>On the date's weekday, which is what a plain FREQ=WEEKLY means and what @ 매주 writes.</summary>
    Weekly,

    /// <summary>On the date's day of the month; a month without it is skipped.</summary>
    Monthly,

    /// <summary>On the date's month and day.</summary>
    Yearly,
}

/// <summary>
/// The draft behind the to-do sheet: what it is called, which kind, its day, its time, how it
/// repeats, a few lines about it and its list, entered in fields of their own. A to-do is never
/// made from a note's body; the two are separate.
/// </summary>
/// <remarks>
/// <para>
/// The item it makes is <see cref="AgendaCapture.Compose"/>'s, with the sheet's day and time as the
/// reading: DUE or DTSTART, HasDueTime and the event's hour follow the same rules as an @ phrase,
/// so reminders, widgets, sync and the day panel cannot tell the two apart. Only what @ has no
/// words for is set on top — a list, an event's own end, a description and a to-do with no date.
/// A repeat goes in as the reading's rule, so a repeating item is anchored exactly as @ 매일 anchors
/// one: a to-do carries the anchor in DTSTART as well as DUE (<see cref="AgendaItem.Anchor"/>).
/// </para>
/// <para>
/// The day it opens on is the one being looked at: the day screen's day, or today from the 할 일
/// tab. An event always has a start; a to-do may have neither date nor time.
/// </para>
/// </remarks>
public sealed partial class TodoEntryViewModel : ObservableObject
{
    private readonly IClock _clock;

    /// <summary>The day the sheet starts on, which an event falls back to.</summary>
    private DateOnly _startDate;

    /// <summary>An event's length, kept when its start moves. One hour until its end is picked.</summary>
    private TimeSpan _eventLength = AgendaPhraseParser.DefaultEventLength;

    /// <summary>
    /// True while the time is the one switching to 일정 filled in, not one the user chose: switching
    /// back to 할 일 takes it away again, since a to-do has no time unless it is given one.
    /// </summary>
    private bool _timeFilledIn;

    private bool _dark;

    public TodoEntryViewModel(IClock clock, INoteRepository repository)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Calendar = new CalendarMonthViewModel(clock, repository, PickDateAsync);
        RepeatOptions =
        [
            .. new[] { TodoRepeat.None, TodoRepeat.Daily, TodoRepeat.Weekdays, TodoRepeat.Weekly, TodoRepeat.Monthly, TodoRepeat.Yearly }
                .Select(static repeat => new TodoRepeatOption(repeat)),
        ];
    }

    /// <summary>The month grid under the date row: the month sheet's own, picking for the draft instead of the day.</summary>
    public CalendarMonthViewModel Calendar { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTask), nameof(IsEvent), nameof(TitlePlaceholder))]
    private AgendaKind _kind = AgendaKind.Task;

    public bool IsTask => Kind == AgendaKind.Task;

    public bool IsEvent => Kind == AgendaKind.Event;

    /// <summary>제목. One line; the item's title.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    private string _title = string.Empty;

    /// <summary>추가 waits for a title: an item with no name is a row nobody can identify.</summary>
    public bool CanAdd => !string.IsNullOrWhiteSpace(Title);

    /// <summary>내용. A few optional lines kept with the item, never in its title.</summary>
    [ObservableProperty]
    private string _description = string.Empty;

    public string TitlePlaceholder => IsEvent ? AppStrings.TodoEntryEventPlaceholder : AppStrings.TodoEntryTaskPlaceholder;

    /// <summary>The day, or null for a to-do with none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDate), nameof(DateText), nameof(CanPickTime), nameof(CanRepeat), nameof(RepeatText))]
    private DateOnly? _date;

    public bool HasDate => Date is not null;

    public string DateText => Date is { } day
        ? day.ToString(AppStrings.TodoEntryDateFormat, LocalizationService.Instance.Culture)
        : AppStrings.TodoEntryNoDate;

    /// <summary>A to-do's time, or an event's start. Null is 시간 없음, which only a to-do can be.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTime), nameof(TimeText), nameof(EndText))]
    private TimeOnly? _time;

    public bool HasTime => Time is not null;

    public string TimeText => Time is { } time ? Clock(time) : AppStrings.TodoEntryNoTime;

    /// <summary>An event's end: its start and its length, with +1 when that runs past midnight.</summary>
    public string EndText
    {
        get
        {
            if (Time is not { } start)
            {
                return string.Empty;
            }

            TimeSpan end = start.ToTimeSpan() + _eventLength;
            return Clock(TimeOnly.FromTimeSpan(TimeSpan.FromTicks(end.Ticks % TimeSpan.TicksPerDay))) +
                (end >= TimeSpan.FromDays(1) ? " +1" : string.Empty);
        }
    }

    /// <summary>A time needs a day to be on. An undated to-do's time row is greyed until it has one.</summary>
    public bool CanPickTime => HasDate;

    /// <summary>How it repeats. Only with a date: a rule needs a day to count from.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Repeats), nameof(RepeatText))]
    private TodoRepeat _repeat;

    public bool Repeats => Repeat != TodoRepeat.None;

    /// <summary>An undated to-do cannot repeat; its 반복 row is greyed until it has a day.</summary>
    public bool CanRepeat => HasDate;

    public IReadOnlyList<TodoRepeatOption> RepeatOptions { get; }

    /// <summary>What the 반복 row says: the repeat spelled out against the chosen day, "매주 금요일".</summary>
    public string RepeatText
    {
        get
        {
            if (Date is not { } day)
            {
                return AppStrings.TodoEntryRepeatNeedsDate;
            }

            CultureInfo culture = LocalizationService.Instance.Culture;
            return Repeat switch
            {
                TodoRepeat.Weekly => string.Format(culture, AppStrings.TodoEntryRepeatWeeklyOn,
                    culture.DateTimeFormat.GetDayName(day.DayOfWeek)),
                TodoRepeat.Monthly => string.Format(culture, AppStrings.TodoEntryRepeatMonthlyOn, day.Day),
                TodoRepeat.Yearly => string.Format(culture, AppStrings.TodoEntryRepeatYearlyOn,
                    day.ToString(AppStrings.TodoEntryRepeatYearlyFormat, culture)),
                _ => LocalizationService.Instance["TodoEntryRepeat" + Repeat],
            };
        }
    }

    /// <summary>The rule 추가 writes, in the forms <see cref="AgendaRecurrence"/> expands and @ 매일 / 매주 writes.</summary>
    public string? Rrule => Repeat switch
    {
        TodoRepeat.Daily => "FREQ=DAILY",
        TodoRepeat.Weekdays => "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR",
        TodoRepeat.Weekly => "FREQ=WEEKLY",
        TodoRepeat.Monthly => "FREQ=MONTHLY",
        TodoRepeat.Yearly => "FREQ=YEARLY",
        _ => null,
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDatePickerOpen), nameof(IsTimePickerOpen), nameof(IsRepeatPickerOpen))]
    private TodoEntryPicker _picker;

    public bool IsDatePickerOpen => Picker == TodoEntryPicker.Date;

    public bool IsRepeatPickerOpen => Picker == TodoEntryPicker.Repeat;

    /// <summary>The hour and minute grid, for the to-do's time, the event's start or the event's end.</summary>
    public bool IsTimePickerOpen => Picker is TodoEntryPicker.Time or TodoEntryPicker.End;

    public IReadOnlyList<TimeOption> HourOptions { get; } =
        [.. Enumerable.Range(0, 24).Select(hour => new TimeOption(hour, hour.ToString("00", CultureInfo.InvariantCulture)))];

    /// <summary>Ten-minute steps: nobody writes 14:37, and six pills fit one row.</summary>
    public IReadOnlyList<TimeOption> MinuteOptions { get; } =
        [.. Enumerable.Range(0, 6).Select(step => new TimeOption(step * 10, (step * 10).ToString("00", CultureInfo.InvariantCulture)))];

    /// <summary>The list it is filed in. The built-in one unless another is chosen (§7: never required).</summary>
    [ObservableProperty]
    private Guid _listId = AgendaList.DefaultId;

    public ObservableCollection<TodoListOption> ListOptions { get; } = [];

    /// <summary>Only when there is a choice to make: with the built-in list alone the row says nothing.</summary>
    public bool ShowLists => ListOptions.Count > 1;

    /// <summary>
    /// A fresh draft: a to-do on <paramref name="date"/> with no time, in <paramref name="listId"/>
    /// or, when that is null or gone, the built-in list.
    /// </summary>
    public async Task ResetAsync(DateOnly date, IEnumerable<AgendaListRowViewModel> lists, bool dark, Guid? listId = null)
    {
        ArgumentNullException.ThrowIfNull(lists);
        _startDate = date;
        _dark = dark;
        _eventLength = AgendaPhraseParser.DefaultEventLength;
        _timeFilledIn = false;
        Kind = AgendaKind.Task;
        Title = string.Empty;
        Description = string.Empty;
        Date = date;
        Time = null;
        Repeat = TodoRepeat.None;
        MarkRepeat();
        Picker = TodoEntryPicker.None;
        List<AgendaListRowViewModel> rows = [.. lists];
        ListId = listId is { } chosen && rows.Any(list => list.Id == chosen) ? chosen : AgendaList.DefaultId;

        ListOptions.Clear();
        int position = 0;
        foreach (AgendaListRowViewModel list in rows)
        {
            ListOptions.Add(new TodoListOption(list.Id, list.Name, Dot(position++)) { IsCurrent = list.Id == ListId });
        }

        OnPropertyChanged(nameof(ShowLists));
        await Calendar.ShowSelectedAsync(LocalDates.FromDateOnly(date)).ConfigureAwait(true);
    }

    /// <summary>
    /// 할 일 or 일정. An event needs a day and a start, so switching to one fills them in — the
    /// sheet's day and the next whole hour — and switching back takes away a time it filled in.
    /// </summary>
    [RelayCommand]
    private void SelectKind(AgendaKind kind)
    {
        Kind = kind;
        if (kind == AgendaKind.Event)
        {
            Date ??= _startDate;
            if (Time is null)
            {
                Time = NextWholeHour();
                _timeFilledIn = true;
            }
        }
        else if (_timeFilledIn)
        {
            Time = null;
            _timeFilledIn = false;
        }

        if (Picker == TodoEntryPicker.End && kind == AgendaKind.Task)
        {
            Picker = TodoEntryPicker.None;
        }

        MarkTime();
    }

    /// <summary>A tap on a row opens its picker, and a second tap closes it.</summary>
    [RelayCommand]
    private async Task TogglePicker(TodoEntryPicker picker)
    {
        if ((picker is TodoEntryPicker.Time or TodoEntryPicker.End && !CanPickTime) ||
            (picker == TodoEntryPicker.Repeat && !CanRepeat))
        {
            return;
        }

        Picker = Picker == picker ? TodoEntryPicker.None : picker;
        if (Picker == TodoEntryPicker.Date)
        {
            await Calendar.ShowSelectedAsync(LocalDates.FromDateOnly(Date ?? _startDate)).ConfigureAwait(true);
            if (Date is null)
            {
                // Nothing is picked: the grid opens on the sheet's month with no day lit.
                foreach (CalendarDayCellViewModel cell in Calendar.Cells)
                {
                    cell.IsSelected = false;
                }
            }
        }

        MarkTime();
    }

    /// <summary>날짜 없음. A to-do only: an event is a block of a day.</summary>
    [RelayCommand]
    private void ClearDate()
    {
        if (IsEvent)
        {
            return;
        }

        Date = null;
        Time = null;
        _timeFilledIn = false;
        Picker = TodoEntryPicker.None;

        // A rule counts from a day; with none there is nothing for it to repeat on.
        Repeat = TodoRepeat.None;
        MarkRepeat();
    }

    /// <summary>시간 없음. A to-do only: an event cannot be all day from here.</summary>
    [RelayCommand]
    private void ClearTime()
    {
        if (IsEvent)
        {
            return;
        }

        Time = null;
        _timeFilledIn = false;
        Picker = TodoEntryPicker.None;
    }

    /// <summary>An hour pill. The grid stays open for the minute.</summary>
    [RelayCommand]
    private void PickHour(int hour) => SetPicked(hour, Picked().Minute);

    /// <summary>A minute pill, the second of the two taps; the grid closes behind it.</summary>
    [RelayCommand]
    private void PickMinute(int minute)
    {
        SetPicked(Picked().Hour, minute);
        Picker = TodoEntryPicker.None;
    }

    /// <summary>A repeat pill. Refused without a date, which the greyed row already says.</summary>
    [RelayCommand]
    private void PickRepeat(TodoRepeat repeat)
    {
        if (repeat != TodoRepeat.None && !CanRepeat)
        {
            return;
        }

        Repeat = repeat;
        Picker = TodoEntryPicker.None;
        MarkRepeat();
    }

    [RelayCommand]
    private void PickList(Guid id)
    {
        ListId = id;
        foreach (TodoListOption option in ListOptions)
        {
            option.IsCurrent = option.Id == id;
        }
    }

    /// <summary>
    /// The item 추가 writes, from <see cref="AgendaCapture.Compose"/> with the sheet's day and time
    /// as the reading. It points at no note.
    /// </summary>
    public AgendaItem Compose(Guid itemId, DateTimeOffset now)
    {
        DateOnly day = Date ?? _startDate;
        var reading = new AgendaPhrase(
            new WallClock(day.ToDateTime(Time ?? TimeOnly.MinValue)),
            HasTime,
            Date is null ? null : Rrule,
            RolledToTomorrow: false,
            Length: 0);
        var state = new AgendaCaptureState(0, 0, Title.Trim(), string.Empty, reading);

        AgendaItem made = AgendaCapture.Compose(state, Kind, Guid.Empty, itemId, now) with
        {
            SourceNoteId = null,
            ListId = ListId,
            Description = Description.Trim(),
        };
        if (IsEvent && HasTime)
        {
            made = made with { EndsAt = new WallClock(reading.At.Value + _eventLength) };
        }

        if (IsTask && Date is null)
        {
            // Undated: owed, not due. No DUE and so no alert, which would have nothing to ring at.
            made = made with { DueAt = null, HasDueTime = false, AlarmLeadMinutes = AgendaAlert.None };
        }

        return made;
    }

    private Task PickDateAsync(LocalDate date)
    {
        Date = LocalDates.ToDateOnly(date);
        Calendar.SyncSelection(date);
        Picker = TodoEntryPicker.None;
        return Task.CompletedTask;
    }

    /// <summary>What the open grid is changing: the start (or the to-do's time), or the event's end.</summary>
    private TimeOnly Picked()
    {
        TimeOnly start = Time ?? NextWholeHour();
        return Picker == TodoEntryPicker.End ? start.Add(_eventLength) : start;
    }

    private void SetPicked(int hour, int minute)
    {
        var picked = new TimeOnly(hour, minute);
        if (Picker == TodoEntryPicker.End && Time is { } start)
        {
            // An end at or before the start runs into the next day rather than being refused.
            TimeSpan length = picked.ToTimeSpan() - start.ToTimeSpan();
            _eventLength = length > TimeSpan.Zero ? length : length + TimeSpan.FromDays(1);
            OnPropertyChanged(nameof(EndText));
        }
        else
        {
            Time = picked;
            _timeFilledIn = false;
        }

        MarkTime();
    }

    /// <summary>Lights the hour and minute of whichever time the grid is open on.</summary>
    private void MarkTime()
    {
        TimeOnly? shown = Picker switch
        {
            TodoEntryPicker.Time => Time,
            TodoEntryPicker.End when Time is not null => Picked(),
            _ => null,
        };
        foreach (TimeOption option in HourOptions)
        {
            option.IsCurrent = shown is { } time && option.Value == time.Hour;
        }

        foreach (TimeOption option in MinuteOptions)
        {
            option.IsCurrent = shown is { } time && option.Value == time.Minute;
        }
    }

    private void MarkRepeat()
    {
        foreach (TodoRepeatOption option in RepeatOptions)
        {
            option.IsCurrent = option.Repeat == Repeat;
        }
    }

    /// <summary>The next o'clock after now: an event made at 14:20 starts at 15:00.</summary>
    private TimeOnly NextWholeHour()
    {
        ClockSnapshot snapshot = _clock.Read();
        DateTime local = snapshot.UtcInstant.ToOffset(snapshot.LocalUtcOffset).DateTime;
        return new TimeOnly((local.Hour + 1) % 24, 0);
    }

    private string Dot(int position)
    {
        (string light, string dark) = AgendaListPalette.ForPosition(position);
        return _dark ? dark : light;
    }

    private static string Clock(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>One list in the sheet's row: its name, its colour, and whether the draft is filed there.</summary>
public sealed partial class TodoListOption(Guid id, string name, string dot) : ObservableObject
{
    public Guid Id { get; } = id;

    public string Name { get; } = name;

    /// <summary>The list's colour as <c>#rrggbb</c>, for the theme the sheet opened in.</summary>
    public string Dot { get; } = dot;

    [ObservableProperty]
    private bool _isCurrent;
}

/// <summary>One pill in the 반복 row.</summary>
public sealed partial class TodoRepeatOption(TodoRepeat repeat) : ObservableObject
{
    public TodoRepeat Repeat { get; } = repeat;

    public string Label => LocalizationService.Instance["TodoEntryRepeat" + Repeat];

    [ObservableProperty]
    private bool _isCurrent;
}

/// <summary>One hour or minute pill: the to-do sheet's time grid and the phone's reminder time sheet.</summary>
public sealed partial class TimeOption(int value, string label) : ObservableObject
{
    public int Value { get; } = value;

    public string Label { get; } = label;

    /// <summary>Whether this is the draft's hour (or minute).</summary>
    [ObservableProperty]
    private bool _isCurrent;
}
