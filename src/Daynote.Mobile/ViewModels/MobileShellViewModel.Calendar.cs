using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.App.Shell.Product;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Core.Time;

namespace Daynote.Mobile.ViewModels;

/// <summary>
/// Mobile B v2's home screen and calendar: the day's 일정, then its 할 일, then its 노트, each
/// folding under its heading; the dots under days with a dated to-do or event; and the month sheet
/// pushed up into the month's calendar with the selected day's agenda under it.
/// </summary>
public sealed partial class MobileShellViewModel
{
    private const string HomeEventsClosedKey = "mobile.home-events-closed";
    private const string HomeTodosClosedKey = "mobile.home-todos-closed";
    private const string HomeNotesClosedKey = "mobile.home-notes-closed";

    private CalendarBoardViewModel? _monthBoard;

    /// <summary>Notes on each day of the week strip, as read; the agenda's days are added to them.</summary>
    private HashSet<LocalDate> _weekNoteDays = [];

    [ObservableProperty]
    private bool _isHomeEventsOpen = true;

    [ObservableProperty]
    private bool _isHomeTodosOpen = true;

    [ObservableProperty]
    private bool _isHomeNotesOpen = true;

    /// <summary>The selected day's events, then the next seven days' one-off ones.</summary>
    public ObservableCollection<HomeAgendaGroup> EventGroups { get; } = [];

    /// <summary>The selected day's to-dos (with the add line), then every other one-off one still owed.</summary>
    public ObservableCollection<HomeAgendaGroup> TodoGroupsHome { get; } = [];

    [ObservableProperty]
    private string _homeEventCountText = "0";

    [ObservableProperty]
    private string _homeTodoCountText = "0";

    [ObservableProperty]
    private bool _isHomeEventsEmpty = true;

    /// <summary>The 할 일 group's own line: a to-do with that title, on the day.</summary>
    [ObservableProperty]
    private string _homeQuickText = string.Empty;

    /// <summary>The month sheet pushed up into the month's calendar (the design's <c>sheetFull</c>).</summary>
    [ObservableProperty]
    private bool _isMonthBoardOpen;

    /// <summary>The sheet's push up, while it is a half sheet; null once it is the calendar.</summary>
    public System.Windows.Input.ICommand? MonthSheetExpand => IsMonthBoardOpen ? null : ExpandMonthBoardCommand;

    /// <summary>The sheet's pull down, while it is the calendar: back to the half sheet rather than away.</summary>
    public System.Windows.Input.ICommand? MonthSheetCollapse => IsMonthBoardOpen ? CollapseMonthBoardCommand : null;

    /// <summary>A week's height in the month's calendar: 70 points in a six-week month, 82 otherwise.</summary>
    public double MonthBoardRowHeight => MonthBoard.Cells.Count > 35 ? 70 : 82;

    partial void OnIsMonthBoardOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(MonthSheetExpand));
        OnPropertyChanged(nameof(MonthSheetCollapse));
    }

    /// <summary>The month's calendar, the same one the desktop draws, in its phone shape.</summary>
    public CalendarBoardViewModel MonthBoard => _monthBoard ??= new CalendarBoardViewModel(
        SelectBoardDayAsync, OpenBoardDayAsync, EditTodoAsync, static _ => Task.CompletedTask) { IsCompact = true };

    /// <summary>The selected day's events and to-dos, under the month's calendar.</summary>
    public ObservableCollection<HomeAgendaRow> MonthAgenda { get; } = [];

    [ObservableProperty]
    private string _monthAgendaTitle = string.Empty;

    [ObservableProperty]
    private string _monthAgendaSubtitle = string.Empty;

    [ObservableProperty]
    private bool _isMonthAgendaEmpty = true;

    [RelayCommand]
    private void ToggleHomeEvents() => SaveHomeSection(HomeEventsClosedKey, IsHomeEventsOpen = !IsHomeEventsOpen);

    [RelayCommand]
    private void ToggleHomeTodos() => SaveHomeSection(HomeTodosClosedKey, IsHomeTodosOpen = !IsHomeTodosOpen);

    [RelayCommand]
    private void ToggleHomeNotes() => SaveHomeSection(HomeNotesClosedKey, IsHomeNotesOpen = !IsHomeNotesOpen);

    /// <summary>Enter in the 할 일 group: a to-do with that title on the selected day.</summary>
    [RelayCommand]
    private async Task HomeQuickAdd()
    {
        string title = HomeQuickText.Trim();
        if (title.Length == 0)
        {
            return;
        }

        HomeQuickText = string.Empty;
        AgendaItem made = TodoEntryViewModel.ComposeQuick(
            title, LocalDates.ToDateOnly(SelectedDate), AgendaList.DefaultId, Guid.NewGuid(), _clock.Read().UtcInstant);
        await _agenda.SaveAsync(made).ConfigureAwait(true);
        _syncScheduler?.NotifySaved();
        await RefreshTodosAsync().ConfigureAwait(true);
    }

    /// <summary>The grab handle's tap: the month sheet up into the calendar, or back down.</summary>
    [RelayCommand]
    private Task ToggleMonthBoard() => IsMonthBoardOpen ? CollapseMonthBoard() : ExpandMonthBoard();

    /// <summary>Pushed up: the month's calendar over the whole screen.</summary>
    [RelayCommand]
    private async Task ExpandMonthBoard()
    {
        IsMonthBoardOpen = true;
        await RefreshMonthBoardAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private Task CollapseMonthBoard()
    {
        IsMonthBoardOpen = false;
        return Task.CompletedTask;
    }

    /// <summary>닫기: the calendar and its sheet both go.</summary>
    [RelayCommand]
    private void CloseMonthBoard()
    {
        IsMonthBoardOpen = false;
        IsMonthPickerOpen = false;
    }

    [RelayCommand]
    private async Task MonthBoardPrevious()
    {
        await Calendar.PreviousMonthCommand.ExecuteAsync(null).ConfigureAwait(true);
        Calendar.SyncSelection(SelectedDate);
        await RefreshMonthBoardAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task MonthBoardNext()
    {
        await Calendar.NextMonthCommand.ExecuteAsync(null).ConfigureAwait(true);
        Calendar.SyncSelection(SelectedDate);
        await RefreshMonthBoardAsync().ConfigureAwait(true);
    }

    /// <summary>이 날 보기: the home screen, on the day the calendar has selected.</summary>
    [RelayCommand]
    private void OpenMonthAgendaDay()
    {
        CloseMonthBoard();
        GoToPage(MobilePage.Day);
    }

    private void SaveHomeSection(string key, bool open)
    {
        if (!_loading)
        {
            _ = _settings.SetBoolAsync(key, !open);
        }
    }

    private async Task LoadHomeSectionsAsync(CancellationToken cancellationToken)
    {
        IsHomeEventsOpen = !await _settings.GetBoolAsync(HomeEventsClosedKey, false, cancellationToken).ConfigureAwait(true);
        IsHomeTodosOpen = !await _settings.GetBoolAsync(HomeTodosClosedKey, false, cancellationToken).ConfigureAwait(true);
        IsHomeNotesOpen = !await _settings.GetBoolAsync(HomeNotesClosedKey, false, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// The dates a dated one-off to-do or event falls on: the month sheet's and the week strip's
    /// dots count them with the notes (a repeat would dot every day and say nothing).
    /// </summary>
    private IReadOnlySet<LocalDate> AgendaDaySet() =>
        Todo.All
            .Where(static item => !item.IsSeries && item.Status != AgendaStatus.Cancelled && item.Anchor is not null)
            .Select(static item => LocalDates.FromDateOnly(DateOnly.FromDateTime(item.Anchor!.Value.Value)))
            .ToHashSet();

    private void RefreshWeekDots()
    {
        IReadOnlySet<LocalDate> agenda = AgendaDaySet();
        foreach (WeekDayViewModel day in Week)
        {
            day.HasNotes = _weekNoteDays.Contains(day.Date) || agenda.Contains(day.Date);
        }
    }

    /// <summary>
    /// The home screen's 일정 and 할 일 groups for the selected day (Mobile B v2 <c>evGroups</c> /
    /// <c>tdGroups</c>), from rows the panels already built.
    /// </summary>
    private void RefreshHomeAgenda(DateTime now, Func<AgendaDayRow, TodoRowViewModel> row)
    {
        DateOnly day = LocalDates.ToDateOnly(SelectedDate);
        DateOnly today = DateOnly.FromDateTime(now);
        CultureInfo culture = CultureInfo.CurrentCulture;
        string dayLabel = RelativeDayLabel(day, today);
        AgendaDayView view = AgendaDay.For(day, Todo.All);

        // 일정: the day's, then the next seven days' one-off events with the day they fall on.
        EventGroups.Clear();
        HomeAgendaRow[] dayEvents = [.. view.Open.Concat(view.Done)
            .Where(static r => r.Item.Kind == AgendaKind.Event)
            .OrderBy(static r => r.Item.EndsAt is null ? DateTime.MinValue : r.At?.Value ?? DateTime.MaxValue)
            .Select((r, i) => HomeAgendaRow.Event(row(r), sub: string.Empty, first: i == 0))];
        if (dayEvents.Length > 0)
        {
            EventGroups.Add(new HomeAgendaGroup(dayLabel, dayEvents.Length.ToString(culture), primary: true, dayEvents));
        }

        var upcoming = new List<HomeAgendaRow>();
        for (int offset = 1; offset <= 7; offset++)
        {
            DateOnly date = day.AddDays(offset);
            foreach (AgendaDayRow r in AgendaDay.For(date, Todo.All).Open
                         .Where(static r => r.Item.Kind == AgendaKind.Event && !r.IsOccurrence))
            {
                upcoming.Add(HomeAgendaRow.Event(row(r), RelativeDayLabel(date, today), first: upcoming.Count == 0));
            }
        }

        if (upcoming.Count > 0)
        {
            EventGroups.Add(new HomeAgendaGroup(MobileStrings.Get("MobileHomeUpcomingEvents"), upcoming.Count.ToString(culture), primary: false, upcoming));
        }

        HomeEventCountText = (dayEvents.Length + upcoming.Count).ToString(culture);
        IsHomeEventsEmpty = EventGroups.Count == 0;

        // 할 일: the day's, open first, with the add line; then every other one-off to-do still owed.
        AgendaDayRow[] dayTodos = [.. view.Open.Where(static r => r.Item.Kind == AgendaKind.Task),
            .. view.Done.Where(static r => r.Item.Kind == AgendaKind.Task)];
        int remaining = view.Open.Count(static r => r.Item.Kind == AgendaKind.Task);
        string remainingText = MobileStrings.Format("MobileHomeTodoRemainingFormat", remaining);
        HomeAgendaRow[] dayRows = [.. dayTodos.Select((r, i) => HomeAgendaRow.Todo(row(r), r.At is { } at ? at.Value.ToString("HH:mm", culture) : string.Empty, r.IsOverdue(now), first: i == 0))];
        while (TodoGroupsHome.Count > 1)
        {
            TodoGroupsHome.RemoveAt(TodoGroupsHome.Count - 1);
        }

        if (TodoGroupsHome.Count == 1)
        {
            TodoGroupsHome[0].Update(dayLabel, remainingText, dayRows);
        }
        else
        {
            TodoGroupsHome.Add(new HomeAgendaGroup(dayLabel, remainingText, primary: true, dayRows, hasAddLine: true));
        }

        var rest = new List<HomeAgendaRow>();
        foreach (AgendaItem item in Todo.All
                     .Where(item => item.Kind == AgendaKind.Task && !item.IsSeries && !item.IsOverride
                         && item.Status == AgendaStatus.NeedsAction
                         && item.Anchor is { } anchor && DateOnly.FromDateTime(anchor.Value) != day)
                     .OrderBy(static item => item.Anchor!.Value.Value))
        {
            WallClock anchor = item.Anchor!.Value;
            WallClock? at = item.HasClockTime ? anchor : null;
            var r = new AgendaDayRow(item, null, at);
            string due = RelativeDayLabel(DateOnly.FromDateTime(anchor.Value), today);
            if (at is { } clock)
            {
                due = string.Create(culture, $"{due} {clock.Value.ToString("HH:mm", culture)}");
            }

            DateTime late = at?.Value ?? anchor.Value.Date.AddDays(1).AddTicks(-1);
            rest.Add(HomeAgendaRow.Todo(row(r), due, late < now, first: rest.Count == 0));
        }

        if (rest.Count > 0)
        {
            TodoGroupsHome.Add(new HomeAgendaGroup(MobileStrings.Get("MobileHomeOtherTodos"), rest.Count.ToString(culture), primary: false, rest));
        }

        HomeTodoCountText = (remaining + rest.Count).ToString(culture);
        RefreshWeekDots();
        Calendar.RefreshAgendaDays();
        if (IsMonthBoardOpen)
        {
            _ = RefreshMonthBoardAsync();
        }
    }

    /// <summary>"오늘", "내일", "모레", or "10/16 (금)".</summary>
    private static string RelativeDayLabel(DateOnly date, DateOnly today) => (date.DayNumber - today.DayNumber) switch
    {
        0 => MobileStrings.Get("MobileTodoToday"),
        1 => MobileStrings.Get("MobileDayTomorrow"),
        2 => MobileStrings.Get("MobileDayAfterTomorrow"),
        _ => date.ToString(MobileStrings.Get("MobileShortDayFormat"), LocalizationService.Instance.Culture),
    };

    /// <summary>The month's calendar and the selected day's agenda under it.</summary>
    private async Task RefreshMonthBoardAsync()
    {
        ClockSnapshot snapshot = _clock.Read();
        DateTime now = snapshot.UtcInstant.ToOffset(snapshot.LocalUtcOffset).DateTime;
        if (Calendar.Cells.Count == 0)
        {
            await Calendar.LoadAsync().ConfigureAwait(true);
        }

        MonthBoard.Build(new CalendarBoardViewModel.Input([.. Calendar.Cells], SelectedDate, Todo.All, [], Todo.ListColor, now));
        OnPropertyChanged(nameof(MonthBoardRowHeight));

        DateOnly day = LocalDates.ToDateOnly(SelectedDate);
        AgendaDayView view = AgendaDay.For(day, Todo.All);
        AgendaDayRow[] rows = [.. view.Open.Concat(view.Done)
            .OrderBy(static r => r.Item.Kind == AgendaKind.Event ? 0 : 1)
            .ThenBy(static r => r.Item.EndsAt is null && r.Item.Kind == AgendaKind.Event ? DateTime.MinValue : r.At?.Value ?? DateTime.MaxValue)];
        MonthAgenda.Clear();
        foreach ((AgendaDayRow r, int i) in rows.Select((r, i) => (r, i)))
        {
            MonthAgenda.Add(HomeAgendaRow.Agenda(
                new TodoRowViewModel(Todo.Row(r, now, TodoRowScope.Day), r, EditTodoAsync, DeleteTodoAsync), first: i == 0));
        }

        int events = rows.Count(static r => r.Item.Kind == AgendaKind.Event);
        int notes = Notes.ProjectionOnly ? 0 : Notes.Tabs.Count(static tab => !tab.IsProjection);
        MonthAgendaTitle = day.ToString(MobileStrings.Get("MobileMonthAgendaDateFormat"), LocalizationService.Instance.Culture);
        MonthAgendaSubtitle = string.Join(" · ", new[]
        {
            events > 0 ? MobileStrings.Format("MobileMonthAgendaEventsFormat", events) : string.Empty,
            rows.Length - events > 0 ? MobileStrings.Format("MobileMonthAgendaTodosFormat", rows.Length - events) : string.Empty,
            notes > 0 ? MobileStrings.Format("MobileMonthAgendaNotesFormat", notes) : string.Empty,
        }.Where(static part => part.Length > 0));
        IsMonthAgendaEmpty = MonthAgenda.Count == 0;
    }

    /// <summary>A day tapped in the month's calendar: it becomes the day, and the calendar stays.</summary>
    private async Task SelectBoardDayAsync(LocalDate date)
    {
        if (date.Year != Calendar.CursorYear || date.Month != Calendar.CursorMonth)
        {
            Calendar.CursorYear = date.Year;
            Calendar.CursorMonth = date.Month;
            await Calendar.LoadAsync().ConfigureAwait(true);
        }

        await SelectDateAsync(date).ConfigureAwait(true);
        Calendar.SyncSelection(date);
        await RefreshMonthBoardAsync().ConfigureAwait(true);
    }

    private async Task OpenBoardDayAsync(LocalDate date)
    {
        await SelectDateAsync(date).ConfigureAwait(true);
        OpenMonthAgendaDay();
    }
}

/// <summary>One group of the home screen's 일정 or 할 일: "오늘 4", and its card of rows.</summary>
public sealed partial class HomeAgendaGroup : ObservableObject
{
    public HomeAgendaGroup(string label, string countText, bool primary, IEnumerable<HomeAgendaRow> rows, bool hasAddLine = false)
    {
        IsPrimary = primary;
        HasAddLine = hasAddLine;
        Update(label, countText, rows);
    }

    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CountText { get; set; } = string.Empty;

    /// <summary>The selected day's group, labelled in Text; the others in Text2.</summary>
    public bool IsPrimary { get; }

    /// <summary>
    /// Refilled in place for the day's to-dos, so the list showing them stays the same list and a
    /// row made since it was last seen arrives (motion spec M2).
    /// </summary>
    public ObservableCollection<HomeAgendaRow> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>The day's to-dos end with the "할 일 추가" line.</summary>
    public bool HasAddLine { get; }

    internal void Update(string label, string countText, IEnumerable<HomeAgendaRow> rows)
    {
        Label = label;
        CountText = countText;
        Rows.Clear();
        foreach (HomeAgendaRow row in rows)
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(HasRows));
    }
}

/// <summary>
/// A line of the home screen's cards or of the month's agenda: the to-do or event, and what the
/// card says beside it — an event's day ("내일"), a to-do's due ("내일 10:00").
/// </summary>
public sealed class HomeAgendaRow
{
    private HomeAgendaRow(TodoRowViewModel row, string sub, string due, bool isLate, bool first)
    {
        Row = row;
        Sub = sub;
        Due = due;
        IsLate = isLate;
        IsFirst = first;
    }

    public TodoRowViewModel Row { get; }

    public TodoItemViewModel Item => Row.Item;

    /// <summary>An upcoming event's day, under its title.</summary>
    public string Sub { get; }

    public bool HasSub => Sub.Length > 0;

    /// <summary>A to-do's time, or for one on another day its day and time, at the right.</summary>
    public string Due { get; }

    public bool HasDue => Due.Length > 0;

    public bool IsLate { get; }

    /// <summary>The first row of a card draws no rule above it.</summary>
    public bool IsFirst { get; }

    /// <summary>The month agenda's left column: the time, "종일", or "—" for a to-do with none.</summary>
    public string TimeColumn => Item.IsEvent && Item.IsAllDay
        ? MobileStrings.Get("MobileAllDay")
        : Item.TimeLabel.Length > 0 ? Item.TimeLabel : "—";

    /// <summary>An event's start in the card's left column, "09:00" or "종일".</summary>
    public string Start => Item.IsAllDay ? MobileStrings.Get("MobileAllDay") : Item.TimeLabel;

    internal static HomeAgendaRow Event(TodoRowViewModel row, string sub, bool first) => new(row, sub, string.Empty, false, first);

    internal static HomeAgendaRow Todo(TodoRowViewModel row, string due, bool late, bool first) => new(row, string.Empty, due, late, first);

    internal static HomeAgendaRow Agenda(TodoRowViewModel row, bool first) => new(row, string.Empty, string.Empty, false, first);
}
