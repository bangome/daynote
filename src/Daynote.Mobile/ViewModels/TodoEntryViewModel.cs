using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.App.Notes;
using Daynote.App.Shell.Product;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Core.Time;

namespace Daynote.Mobile.ViewModels;

/// <summary>Which of the sheet's pickers is open under its row. One at a time, or the sheet would outgrow the screen.</summary>
public enum TodoEntryPicker
{
    None,
    Date,
    Time,
    End,
}

/// <summary>
/// The draft behind the to-do sheet: what it is called, which kind, its day, its time and its list,
/// entered in fields of their own rather than typed into the note.
/// </summary>
/// <remarks>
/// <para>
/// The item it makes is <see cref="AgendaCapture.Compose"/>'s, with the sheet's day and time as the
/// reading: DUE or DTSTART, HasDueTime and the event's hour follow the same rules as an @ phrase,
/// so reminders, widgets, sync and the day panel cannot tell the two apart. Only what @ has no
/// words for is set on top — a list, an event's own end, and a to-do with no date.
/// </para>
/// <para>
/// The note's own day is the default, as the date helper's was: someone writing up Monday means
/// Monday. An event always has a start; a to-do may have neither date nor time.
/// </para>
/// </remarks>
public sealed partial class TodoEntryViewModel : ObservableObject
{
    private readonly IClock _clock;

    /// <summary>The note's day, which the sheet starts on and an event falls back to.</summary>
    private DateOnly _noteDate;

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
    }

    /// <summary>The month grid under the date row: the month sheet's own, picking for the draft instead of the day.</summary>
    public CalendarMonthViewModel Calendar { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTask), nameof(IsEvent), nameof(TitlePlaceholder))]
    private AgendaKind _kind = AgendaKind.Task;

    public bool IsTask => Kind == AgendaKind.Task;

    public bool IsEvent => Kind == AgendaKind.Event;

    /// <summary>내용. One line; the item's title.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    private string _text = string.Empty;

    /// <summary>추가 waits for a title: an item with no name is a row nobody can identify.</summary>
    public bool CanAdd => !string.IsNullOrWhiteSpace(Text);

    public string TitlePlaceholder => MobileStrings.Get(IsEvent ? "MobileTodoEventPlaceholder" : "MobileTodoTaskPlaceholder");

    /// <summary>The day, or null for a to-do with none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDate), nameof(DateText), nameof(CanPickTime))]
    private DateOnly? _date;

    public bool HasDate => Date is not null;

    public string DateText => Date is { } day
        ? day.ToString(MobileStrings.Get("MobileTodoDateFormat"), LocalizationService.Instance.Culture)
        : MobileStrings.Get("MobileTodoNoDate");

    /// <summary>A to-do's time, or an event's start. Null is 시간 없음, which only a to-do can be.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTime), nameof(TimeText), nameof(EndText))]
    private TimeOnly? _time;

    public bool HasTime => Time is not null;

    public string TimeText => Time is { } time ? Clock(time) : MobileStrings.Get("MobileTodoNoTime");

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDatePickerOpen), nameof(IsTimePickerOpen))]
    private TodoEntryPicker _picker;

    public bool IsDatePickerOpen => Picker == TodoEntryPicker.Date;

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
    /// A fresh draft for the note on <paramref name="noteDate"/>: a to-do, on that day, with no
    /// time, in the built-in list.
    /// </summary>
    public async Task ResetAsync(DateOnly noteDate, IEnumerable<AgendaListRowViewModel> lists, bool dark)
    {
        ArgumentNullException.ThrowIfNull(lists);
        _noteDate = noteDate;
        _dark = dark;
        _eventLength = AgendaPhraseParser.DefaultEventLength;
        _timeFilledIn = false;
        Kind = AgendaKind.Task;
        Text = string.Empty;
        Date = noteDate;
        Time = null;
        Picker = TodoEntryPicker.None;
        ListId = AgendaList.DefaultId;

        ListOptions.Clear();
        int position = 0;
        foreach (AgendaListRowViewModel list in lists)
        {
            ListOptions.Add(new TodoListOption(list.Id, list.Name, Dot(position++)) { IsCurrent = list.Id == ListId });
        }

        OnPropertyChanged(nameof(ShowLists));
        await Calendar.ShowSelectedAsync(LocalDates.FromDateOnly(noteDate)).ConfigureAwait(true);
    }

    /// <summary>
    /// 할 일 or 일정. An event needs a day and a start, so switching to one fills them in — the note's
    /// day and the next whole hour — and switching back takes away a time it filled in.
    /// </summary>
    [RelayCommand]
    private void SelectKind(AgendaKind kind)
    {
        Kind = kind;
        if (kind == AgendaKind.Event)
        {
            Date ??= _noteDate;
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
        if (picker is TodoEntryPicker.Time or TodoEntryPicker.End && !CanPickTime)
        {
            return;
        }

        Picker = Picker == picker ? TodoEntryPicker.None : picker;
        if (Picker == TodoEntryPicker.Date)
        {
            await Calendar.ShowSelectedAsync(LocalDates.FromDateOnly(Date ?? _noteDate)).ConfigureAwait(true);
            if (Date is null)
            {
                // Nothing is picked: the grid opens on the note's month with no day lit.
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
    /// as the reading. Nothing is read out of or written into the note.
    /// </summary>
    public AgendaItem Compose(Guid noteId, Guid itemId, DateTimeOffset now)
    {
        DateOnly day = Date ?? _noteDate;
        var reading = new AgendaPhrase(
            new WallClock(day.ToDateTime(Time ?? TimeOnly.MinValue)),
            HasTime,
            Rrule: null,
            RolledToTomorrow: false,
            Length: 0);
        var state = new AgendaCaptureState(0, 0, Text.Trim(), string.Empty, reading);

        AgendaItem made = AgendaCapture.Compose(state, Kind, noteId, itemId, now) with { ListId = ListId };
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

    /// <summary>The next o'clock after now: an event made at 14:20 starts at 15:00.</summary>
    private TimeOnly NextWholeHour()
    {
        ClockSnapshot snapshot = _clock.Read();
        DateTime local = snapshot.UtcInstant.ToOffset(snapshot.LocalUtcOffset).DateTime;
        return new TimeOnly((local.Hour + 1) % 24, 0);
    }

    private IBrush Dot(int position)
    {
        (string light, string dark) = AgendaListPalette.ForPosition(position);
        return Brush.Parse(_dark ? dark : light);
    }

    private static string Clock(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>One list in the sheet's row: its name, its colour, and whether the draft is filed there.</summary>
public sealed partial class TodoListOption(Guid id, string name, IBrush dot) : ObservableObject
{
    public Guid Id { get; } = id;

    public string Name { get; } = name;

    public IBrush Dot { get; } = dot;

    [ObservableProperty]
    private bool _isCurrent;
}
