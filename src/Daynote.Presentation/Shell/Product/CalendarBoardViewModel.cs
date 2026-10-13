using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Core.Files;

namespace Daynote.App.Shell.Product;

/// <summary>What one line in a month-view day is.</summary>
public enum CalendarBoardKind
{
    Event,
    Todo,
    File,
}

/// <summary>
/// The 캘린더 view (Desktop B v2 <c>viewIsCal</c>): the month as a grid of days, each with its events,
/// to-dos and files, filtered by kind, and a card of the whole day when the pointer rests on one.
/// </summary>
/// <remarks>
/// Built from what the shell already holds — the month's note counts from the sidebar calendar, the
/// agenda the panels read, the files library — so the view has no reads of its own and agrees with
/// the panels about which items fall where. Which items a day shows follows the design: events,
/// and to-dos whose <see cref="AgendaItem.ShowsInTimeline"/> says so; a repeating event only on
/// today, or a weekday stand-up would fill every cell.
/// </remarks>
public sealed partial class CalendarBoardViewModel : ObservableObject
{
    private readonly Func<LocalDate, Task> _selectDay;
    private readonly Func<LocalDate, Task> _openDay;
    private readonly Func<AgendaDayRow, Task> _openItem;
    private readonly Func<DayFile, Task> _openFile;
    private Input? _input;

    public CalendarBoardViewModel(
        Func<LocalDate, Task> selectDay,
        Func<LocalDate, Task> openDay,
        Func<AgendaDayRow, Task> openItem,
        Func<DayFile, Task> openFile)
    {
        _selectDay = selectDay ?? throw new ArgumentNullException(nameof(selectDay));
        _openDay = openDay ?? throw new ArgumentNullException(nameof(openDay));
        _openItem = openItem ?? throw new ArgumentNullException(nameof(openItem));
        _openFile = openFile ?? throw new ArgumentNullException(nameof(openFile));
        Filters =
        [
            new CalendarBoardFilter(CalendarBoardKind.Event, Rebuild),
            new CalendarBoardFilter(CalendarBoardKind.Todo, Rebuild),
            new CalendarBoardFilter(CalendarBoardKind.File, Rebuild),
        ];
    }

    public ObservableCollection<CalendarBoardDay> Cells { get; } = [];

    /// <summary>일정 · 할 일 · 파일, each a pill with the month's count that shows or hides its kind.</summary>
    public IReadOnlyList<CalendarBoardFilter> Filters { get; }

    /// <summary>The day under the pointer, whose card is up; null when none is.</summary>
    [ObservableProperty]
    private CalendarBoardDay? _hover;

    /// <summary>Everything a month's grid is drawn from.</summary>
    /// <param name="Days">The sidebar calendar's cells for the month: dates, today, note counts.</param>
    /// <param name="ListColor">A list's ring colour in the current theme.</param>
    public sealed record Input(
        IReadOnlyList<CalendarDayCellViewModel> Days,
        LocalDate Selected,
        IReadOnlyList<AgendaItem> Items,
        IReadOnlyList<DayFile> Files,
        Func<Guid, string> ListColor,
        DateTime Now);

    public void Build(Input input)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        Rebuild();
    }

    [RelayCommand]
    private Task OpenHoverDay() => Hover is { } day ? _openDay(day.Date) : Task.CompletedTask;

    private void Rebuild()
    {
        if (_input is not { } input)
        {
            return;
        }

        DateOnly today = DateOnly.FromDateTime(input.Now);
        var counts = new Dictionary<CalendarBoardKind, int> { [CalendarBoardKind.Event] = 0, [CalendarBoardKind.Todo] = 0, [CalendarBoardKind.File] = 0 };
        ILookup<LocalDate, DayFile> filesByDay = input.Files.ToLookup(static file => file.LocalDate);

        Hover = null;
        Cells.Clear();
        foreach (CalendarDayCellViewModel cell in input.Days)
        {
            DateOnly date = LocalDates.ToDateOnly(cell.Date);
            if (!cell.IsInMonth)
            {
                Cells.Add(new CalendarBoardDay(cell.Date, inMonth: false, isToday: false, isSelected: false, cell.IsSunday, 0, [], [], [], [], _selectDay, _openDay));
                continue;
            }

            AgendaDayView view = AgendaDay.For(date, input.Items);
            AgendaDayRow[] rows = [.. view.Open.Concat(view.Done)
                .Where(row => !(row.IsOccurrence && row.Item.Kind == AgendaKind.Event && date != today))
                .OrderBy(static row => row.Item.Kind == AgendaKind.Event && row.Item.EndsAt is null ? DateTime.MinValue : row.At?.Value ?? DateTime.MaxValue)];
            CalendarBoardItem[] events = [.. rows.Where(static row => row.Item.Kind == AgendaKind.Event).Select(row => Item(row, date, today, input))];
            CalendarBoardItem[] todos = [.. rows.Where(static row => row.Item.Kind == AgendaKind.Task).Select(row => Item(row, date, today, input))];
            CalendarBoardItem[] files = [.. filesByDay[cell.Date].Select(File)];

            // The grid and its counts follow the timeline's rule for to-dos; the card shows the whole day.
            CalendarBoardItem[] shownTodos = [.. todos.Where(static todo => todo.ShowsInTimeline)];
            counts[CalendarBoardKind.Event] += events.Length;
            counts[CalendarBoardKind.Todo] += shownTodos.Length;
            counts[CalendarBoardKind.File] += files.Length;

            var lines = new List<CalendarBoardItem>();
            if (IsOn(CalendarBoardKind.Event))
            {
                lines.AddRange(events);
            }

            if (IsOn(CalendarBoardKind.Todo))
            {
                lines.AddRange(shownTodos);
            }

            if (IsOn(CalendarBoardKind.File))
            {
                lines.AddRange(files);
            }

            Cells.Add(new CalendarBoardDay(
                cell.Date, inMonth: true, cell.IsToday, cell.Date == input.Selected, cell.IsSunday, cell.NoteCount,
                lines, events, todos, files, _selectDay, _openDay));
        }

        foreach (CalendarBoardFilter filter in Filters)
        {
            filter.CountText = counts[filter.Kind].ToString(CultureInfo.CurrentCulture);
        }
    }

    private bool IsOn(CalendarBoardKind kind) => Filters.First(filter => filter.Kind == kind).IsOn;

    private CalendarBoardItem Item(AgendaDayRow row, DateOnly date, DateOnly today, Input input)
    {
        bool isEvent = row.Item.Kind == AgendaKind.Event;
        bool allDay = isEvent && row.Item.EndsAt is null;
        string time = !allDay && row.At is { } at ? at.Value.ToString(AppStrings.TodoRowTimeFormat, CultureInfo.CurrentCulture) : string.Empty;
        string meta;
        if (isEvent)
        {
            DateTime? end = row.At is { } start && row.Item.StartsAt is { } first && row.Item.EndsAt is { } last
                ? start.Value + (last.Value - first.Value)
                : null;
            meta = allDay
                ? AppStrings.DeskEventAllDay
                : end is { } stop ? string.Create(CultureInfo.CurrentCulture, $"{time}–{stop.ToString(AppStrings.TodoRowTimeFormat, CultureInfo.CurrentCulture)}") : time;
        }
        else
        {
            meta = row.IsOccurrence ? ("↻ " + time).TrimEnd() : time;
        }

        return new CalendarBoardItem(
            isEvent ? CalendarBoardKind.Event : CalendarBoardKind.Todo,
            row.Item.Title,
            time,
            meta,
            input.ListColor(row.Item.ListId),
            row.IsDone,
            allDay,
            isPast: date < today,
            showsInTimeline: row.Item.ShowsInTimeline,
            ext: string.Empty,
            () => _openItem(row));
    }

    private CalendarBoardItem File(DayFile file)
    {
        string ext = Path.GetExtension(file.DisplayName).TrimStart('.').ToUpperInvariant();
        return new CalendarBoardItem(
            CalendarBoardKind.File, file.DisplayName, string.Empty, FileItemViewModel.FormatSize(file.ByteLength), string.Empty,
            isDone: false, isAllDay: false, isPast: false, showsInTimeline: true, ext[..Math.Min(4, ext.Length)], () => _openFile(file));
    }
}

/// <summary>One pill of the month view's filters: a kind, how many there are this month, on or off.</summary>
public sealed partial class CalendarBoardFilter : ObservableObject
{
    private readonly Action _changed;

    public CalendarBoardFilter(CalendarBoardKind kind, Action changed)
    {
        Kind = kind;
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
    }

    public CalendarBoardKind Kind { get; }

    public string Label => Kind switch
    {
        CalendarBoardKind.Event => AppStrings.DeskDayEventTitle,
        CalendarBoardKind.Todo => AppStrings.DeskDayTodoHeading,
        _ => AppStrings.DeskNavFiles,
    };

    [ObservableProperty]
    private string _countText = "0";

    [ObservableProperty]
    private bool _isOn = true;

    [RelayCommand]
    private void Toggle()
    {
        IsOn = !IsOn;
        _changed();
    }
}

/// <summary>One day of the month view: its number, "노트 N", its lines, and what its card lists.</summary>
public sealed partial class CalendarBoardDay : ObservableObject
{
    private readonly Func<LocalDate, Task> _select;
    private readonly Func<LocalDate, Task> _open;

    public CalendarBoardDay(
        LocalDate date,
        bool inMonth,
        bool isToday,
        bool isSelected,
        bool isSunday,
        int noteCount,
        IReadOnlyList<CalendarBoardItem> lines,
        IReadOnlyList<CalendarBoardItem> events,
        IReadOnlyList<CalendarBoardItem> todos,
        IReadOnlyList<CalendarBoardItem> files,
        Func<LocalDate, Task> select,
        Func<LocalDate, Task> open)
    {
        Date = date;
        IsInMonth = inMonth;
        IsToday = isToday;
        IsSelected = isSelected;
        IsSunday = isSunday;
        NoteCount = noteCount;
        Events = events;
        Todos = todos;
        Files = files;
        int more = Math.Max(0, lines.Count - Cap);
        Lines = more > 0 ? [.. lines.Take(Cap - 1)] : lines;
        MoreText = more > 0 ? string.Format(CultureInfo.CurrentCulture, AppStrings.DeskCalendarMoreFormat, more + 1) : string.Empty;
        _select = select;
        _open = open;
    }

    /// <summary>How many lines a day shows before "+N개 더".</summary>
    private const int Cap = 4;

    public LocalDate Date { get; }

    public string DayText => Date.Day.ToString(CultureInfo.CurrentCulture);

    public bool IsInMonth { get; }

    public bool IsToday { get; }

    public bool IsSelected { get; }

    public bool IsSunday { get; }

    public int NoteCount { get; }

    public bool HasNotes => IsInMonth && NoteCount > 0;

    /// <summary>"노트 3" at the top right of the day.</summary>
    public string NoteText => string.Format(CultureInfo.CurrentCulture, AppStrings.DeskTimelineNotesFormat, NoteCount);

    public IReadOnlyList<CalendarBoardItem> Lines { get; }

    public bool HasMore => MoreText.Length > 0;

    /// <summary>"+3개 더" when the day has more than fit.</summary>
    public string MoreText { get; }

    public IReadOnlyList<CalendarBoardItem> Events { get; }

    public IReadOnlyList<CalendarBoardItem> Todos { get; }

    public IReadOnlyList<CalendarBoardItem> Files { get; }

    public bool HasEvents => Events.Count > 0;

    public bool HasTodos => Todos.Count > 0;

    public bool HasFiles => Files.Count > 0;

    public bool IsEmpty => !HasEvents && !HasTodos && !HasFiles;

    /// <summary>The card's heading, "10월 13일 (화)".</summary>
    public string Heading =>
        LocalDates.ToDateOnly(Date).ToString(AppStrings.DeskTabMenuDateFormat, LocalizationService.Instance.Culture);

    public string EventCountText => Events.Count.ToString(CultureInfo.CurrentCulture);

    public string TodoCountText => Todos.Count.ToString(CultureInfo.CurrentCulture);

    public string FileCountText => Files.Count.ToString(CultureInfo.CurrentCulture);

    /// <summary>A click on the day selects it and the view stays.</summary>
    [RelayCommand]
    private Task Select() => IsInMonth ? _select(Date) : Task.CompletedTask;

    /// <summary>A click on its number opens the day in the editor.</summary>
    [RelayCommand]
    private Task Open() => IsInMonth ? _open(Date) : Task.CompletedTask;
}

/// <summary>One line in a day: an event, a to-do or a file, and what a click on it does.</summary>
public sealed partial class CalendarBoardItem
{
    private readonly Func<Task> _open;

    public CalendarBoardItem(
        CalendarBoardKind kind,
        string title,
        string time,
        string meta,
        string listColor,
        bool isDone,
        bool isAllDay,
        bool isPast,
        bool showsInTimeline,
        string ext,
        Func<Task> open)
    {
        Kind = kind;
        Title = title;
        Time = time;
        Meta = meta;
        ListColor = listColor;
        IsDone = isDone;
        IsAllDay = isAllDay;
        IsPast = isPast;
        ShowsInTimeline = showsInTimeline;
        Ext = ext;
        _open = open;
    }

    public CalendarBoardKind Kind { get; }

    public bool IsEvent => Kind == CalendarBoardKind.Event;

    public bool IsTodo => Kind == CalendarBoardKind.Todo;

    public bool IsFile => Kind == CalendarBoardKind.File;

    public string Title { get; }

    public string Time { get; }

    public bool HasTime => Time.Length > 0;

    /// <summary>The card's right column: "09:00–09:30", "종일", "↻ 08:00", a file's size.</summary>
    public string Meta { get; }

    public string ListColor { get; }

    public bool IsDone { get; }

    public bool IsAllDay { get; }

    /// <summary>An event on a day gone by, drawn faded.</summary>
    public bool IsPast { get; }

    public bool ShowsInTimeline { get; }

    public string Ext { get; }

    [RelayCommand]
    private Task Open() => _open();
}
