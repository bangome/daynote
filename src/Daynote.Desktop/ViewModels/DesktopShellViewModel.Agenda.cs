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
using Daynote.Motion;

namespace Daynote.Desktop.ViewModels;

/// <summary>
/// The v2 design's to-do furniture around the add card (Daynote Desktop B v2): the day panel's
/// 일정 section and its collapsed 완료, the quick-add lines under the day's to-dos and the 할 일
/// view, the sidebar's lists, and the "M/D에 추가됨 · 보기" notice that says where an item went
/// when it is not on the day being looked at.
/// </summary>
public sealed partial class DesktopShellViewModel
{
    private CancellationTokenSource? _noticeWait;

    private LocalDate? _noticeDate;

    private const string DayEventsClosedKey = "product.panel-events-closed";
    private const string DayTodosClosedKey = "product.panel-todos-closed";
    private const string DayFilesClosedKey = "product.panel-files-closed";

    /// <summary>The day panel's 일정, 할 일 and 파일 each fold under their heading (remembered).</summary>
    [ObservableProperty]
    private bool _isDayEventsOpen = true;

    [ObservableProperty]
    private bool _isDayTodosOpen = true;

    [ObservableProperty]
    private bool _isDayFilesOpen = true;

    /// <summary>The panel's sub-heading for the selected day: "오늘", or "10/14 (수)".</summary>
    [ObservableProperty]
    private string _dayGroupLabel = string.Empty;

    /// <summary>The next seven days' one-off events, under 일정 (design v2 "다가오는 7일").</summary>
    public System.Collections.ObjectModel.ObservableCollection<PanelAgendaRow> UpcomingEvents { get; } = [];

    [ObservableProperty]
    private string _upcomingHeading = string.Empty;

    [ObservableProperty]
    private bool _hasUpcoming;

    /// <summary>Every other one-off to-do still owed, under the day's (design v2 "남은 할 일 전체").</summary>
    public System.Collections.ObjectModel.ObservableCollection<PanelAgendaRow> OtherTodos { get; } = [];

    [ObservableProperty]
    private string _otherTodosHeading = string.Empty;

    [ObservableProperty]
    private bool _hasOtherTodos;

    /// <summary>The 할 일 section's line: "오늘 할 일 추가" on today, "할 일 추가" elsewhere.</summary>
    public string DayQuickPlaceholder => IsTodaySelected ? AppStrings.DeskQuickAddToday : AppStrings.DeskQuickAddDay;

    [RelayCommand]
    private void ToggleDayEvents() => SetPanelSection(DayEventsClosedKey, IsDayEventsOpen = !IsDayEventsOpen);

    [RelayCommand]
    private void ToggleDayTodosSection() => SetPanelSection(DayTodosClosedKey, IsDayTodosOpen = !IsDayTodosOpen);

    [RelayCommand]
    private void ToggleDayFiles() => SetPanelSection(DayFilesClosedKey, IsDayFilesOpen = !IsDayFilesOpen);

    private void SetPanelSection(string key, bool open)
    {
        if (!_loading)
        {
            _ = _settings.SetBoolAsync(key, !open);
        }
    }

    private async Task LoadPanelSectionsAsync(CancellationToken cancellationToken)
    {
        IsDayEventsOpen = !await _settings.GetBoolAsync(DayEventsClosedKey, false, cancellationToken).ConfigureAwait(true);
        IsDayTodosOpen = !await _settings.GetBoolAsync(DayTodosClosedKey, false, cancellationToken).ConfigureAwait(true);
        IsDayFilesOpen = !await _settings.GetBoolAsync(DayFilesClosedKey, false, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// What the day panel shows beyond the day itself: the next week's one-off events, and every
    /// other one-off to-do still owed, soonest first, undated last.
    /// </summary>
    private void RefreshPanelExtras(DateOnly day, DateTime now)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        DayGroupLabel = IsTodaySelected
            ? AppStrings.Today
            : day.ToString(AppStrings.DeskPanelDayFormat, LocalizationService.Instance.Culture);
        OnPropertyChanged(nameof(DayQuickPlaceholder));

        UpcomingEvents.Clear();
        for (int offset = 1; offset <= 7; offset++)
        {
            DateOnly date = day.AddDays(offset);
            foreach (AgendaDayRow row in AgendaDay.For(date, Todo.All).Open
                         .Where(static row => row.Item.Kind == AgendaKind.Event && !row.IsOccurrence))
            {
                string weekday = date.ToString("ddd", LocalizationService.Instance.Culture);
                string when = row.Item.EndsAt is null
                    ? AppStrings.DeskEventAllDay
                    : row.At?.Value.ToString(AppStrings.TodoRowTimeFormat, culture) ?? string.Empty;
                UpcomingEvents.Add(new PanelAgendaRow(
                    Todo.Row(row, now, TodoRowScope.Day),
                    date.ToString(AppStrings.DeskNoticeDateFormat, culture),
                    string.Create(culture, $"{weekday} {when}"),
                    isLate: false));
            }
        }

        UpcomingHeading = Format(AppStrings.DeskPanelUpcomingFormat, UpcomingEvents.Count);
        HasUpcoming = UpcomingEvents.Count > 0;

        OtherTodos.Clear();
        foreach (AgendaItem item in Todo.All
                     .Where(item => item.Kind == AgendaKind.Task && !item.IsSeries && !item.IsOverride
                         && item.Status == AgendaStatus.NeedsAction
                         && (item.Anchor is not { } anchor || DateOnly.FromDateTime(anchor.Value) != day))
                     .OrderBy(static item => item.Anchor?.Value ?? DateTime.MaxValue))
        {
            WallClock? at = item.Anchor is { } anchor && item.HasClockTime ? anchor : null;
            var row = new AgendaDayRow(item, null, at);
            string sub;
            bool late = false;
            if (item.Anchor is { } falls)
            {
                DateTime due = at?.Value ?? falls.Value.Date.AddDays(1).AddTicks(-1);
                late = due < now;
                string date = string.Create(culture,
                    $"{falls.Value.ToString(AppStrings.DeskNoticeDateFormat, culture)} {falls.Value.ToString("ddd", LocalizationService.Instance.Culture)}");
                sub = at is { } clock ? string.Create(culture, $"{date} {clock.Value.ToString(AppStrings.TodoRowTimeFormat, culture)}") : date;
                if (falls.Value.Date < now.Date)
                {
                    sub = Format(AppStrings.DeskPanelPastFormat, sub);
                }
            }
            else
            {
                sub = AppStrings.TodoEntryNoDate;
            }

            OtherTodos.Add(new PanelAgendaRow(Todo.Row(row, now, TodoRowScope.AllDates), string.Empty, sub, late));
        }

        OtherTodosHeading = Format(AppStrings.DeskPanelOtherTodosFormat, OtherTodos.Count);
        HasOtherTodos = OtherTodos.Count > 0;
    }

    /// <summary>The selected day's events, all-day ones first, then by start (design v2, 이 날 패널 일정).</summary>
    public ObservableCollection<TodoItemViewModel> DayEvents { get; } = [];

    [ObservableProperty]
    private string _dayEventCountText = "0";

    [ObservableProperty]
    private bool _isDayEventEmpty = true;

    /// <summary>완료 N, opened: the day's finished to-dos under their count.</summary>
    [ObservableProperty]
    private bool _isDayDoneOpen;

    /// <summary>The day panel's "할 일 추가" line.</summary>
    [ObservableProperty]
    private string _dayQuickText = string.Empty;

    /// <summary>The 할 일 view's "오늘 할 일 추가" line.</summary>
    [ObservableProperty]
    private string _listQuickText = string.Empty;

    /// <summary>"10/16에 추가됨 · 보기" is up: in the day panel, or in the editor's footer when the panel is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNoticeInPanel), nameof(IsNoticeInFooter))]
    private bool _isNoticeShown;

    [ObservableProperty]
    private string _noticeText = string.Empty;

    public bool IsNoticeInPanel => IsNoticeShown && !RightCollapsed;

    public bool IsNoticeInFooter => IsNoticeShown && RightCollapsed;

    /// <summary>How long the notice waits before it goes; a seam, so a test does not wait on the wall clock.</summary>
    public Func<TimeSpan, CancellationToken, Task> NoticeDelay { get; set; } = Task.Delay;

    /// <summary>The list the 할 일 view is narrowed to, or null for all of them.</summary>
    public AgendaListRowViewModel? SelectedAgendaList =>
        Todo.SelectedListId is { } id ? Todo.Lists.FirstOrDefault(list => list.Id == id) : null;

    /// <summary>리스트 삭제, in the 할 일 view's heading: any list but the built-in one, while it is the one shown.</summary>
    public bool CanDeleteSelectedList => IsListMode && TabIsTodo && SelectedAgendaList is { CanDelete: true };

    /// <summary>Which list a quick-add line files into: the one the 할 일 view is narrowed to, or the built-in one.</summary>
    private Guid QuickListId => Todo.SelectedListId ?? AgendaList.DefaultId;

    /// <summary>The day panel's 일정 +: an event on the selected date.</summary>
    [RelayCommand]
    private Task AddDayEvent() => OpenAddTodoAsync(SelectedDate, listId: null, AgendaKind.Event);

    [RelayCommand]
    private void ToggleDayDone() => IsDayDoneOpen = !IsDayDoneOpen;

    /// <summary>Enter in the day panel's line: a to-do with that title on the selected day.</summary>
    [RelayCommand]
    private async Task QuickAddDay()
    {
        string title = DayQuickText.Trim();
        if (title.Length == 0)
        {
            return;
        }

        DayQuickText = string.Empty;
        await SaveMadeAsync(TodoEntryViewModel.ComposeQuick(
            title, LocalDates.ToDateOnly(SelectedDate), QuickListId, Guid.NewGuid(), _clock.Read().UtcInstant)).ConfigureAwait(true);
    }

    /// <summary>Enter in the 할 일 view's line: a to-do for today, in the list being looked at.</summary>
    [RelayCommand]
    private async Task QuickAddList()
    {
        string title = ListQuickText.Trim();
        if (title.Length == 0)
        {
            return;
        }

        ListQuickText = string.Empty;
        await SaveMadeAsync(TodoEntryViewModel.ComposeQuick(
            title, LocalDates.ToDateOnly(LocalDates.Today(_clock)), QuickListId, Guid.NewGuid(), _clock.Read().UtcInstant)).ConfigureAwait(true);
    }

    /// <summary>
    /// Writes something just made, refreshes what shows it, and says where it went when that is
    /// not in front of the user: another day than the panel's ("10/16에 추가됨"), or a panel that
    /// is closed ("할 일 추가됨 · 10/16"). The row arriving in the panel says it otherwise.
    /// </summary>
    private async Task SaveMadeAsync(AgendaItem made)
    {
        await _agenda.SaveAsync(made).ConfigureAwait(true);

        // No note is saved along with it, so nothing else is going to set a sync off.
        _syncScheduler?.NotifySaved();
        await Todo.RefreshAsync().ConfigureAwait(true);
        Announce(made);
    }

    private void Announce(AgendaItem made)
    {
        if (made.Anchor is not { } anchor)
        {
            return;
        }

        bool onSelectedDay = AgendaDay.For(LocalDates.ToDateOnly(SelectedDate), [made]).Open.Count > 0;
        LocalDate date = LocalDates.FromDateOnly(DateOnly.FromDateTime(anchor.Value));
        string day = anchor.Value.ToString(AppStrings.DeskNoticeDateFormat, CultureInfo.CurrentCulture);
        string? text = !onSelectedDay
            ? Format(AppStrings.DeskNoticeAddedToFormat, day)
            : RightCollapsed && !IsListMode
                ? Format(made.Kind == AgendaKind.Event ? AppStrings.DeskNoticeEventAddedFormat : AppStrings.DeskNoticeTodoAddedFormat, day)
                : null;
        if (text is null || (IsListMode && TabIsTodo))
        {
            // The 할 일 view lists every date, so the row is already in front of the user.
            return;
        }

        _noticeWait?.Cancel();
        _noticeWait?.Dispose();
        var wait = new CancellationTokenSource();
        _noticeWait = wait;
        _noticeDate = date;
        NoticeText = text;
        IsNoticeShown = true;
        _ = CloseNoticeAfterAsync(wait);
    }

    private async Task CloseNoticeAfterAsync(CancellationTokenSource wait)
    {
        try
        {
            await NoticeDelay(Choreography.NoticeHold + Choreography.NoticeHold / 2, wait.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ReferenceEquals(_noticeWait, wait))
        {
            IsNoticeShown = false;
        }
    }

    /// <summary>보기: the day it went to, with the panel open on it.</summary>
    [RelayCommand]
    private async Task ViewNotice()
    {
        _noticeWait?.Cancel();
        IsNoticeShown = false;
        if (_noticeDate is not { } date)
        {
            return;
        }

        RightCollapsed = false;
        await SelectDateAsync(date).ConfigureAwait(true);
    }

    /// <summary>The sidebar's 할 일: every list, on every date.</summary>
    [RelayCommand]
    private async Task ShowAllTodos()
    {
        await ShowListCommand.ExecuteAsync(RightTab.Todo).ConfigureAwait(true);
        if (IsListMode && Todo.SelectedListId is not null)
        {
            await Todo.SelectListAsync(Todo.SelectedListId).ConfigureAwait(true);
        }

        RaiseListState();
    }

    /// <summary>A list in the sidebar: the 할 일 view, narrowed to it. Picking it again keeps it, as a nav row does.</summary>
    [RelayCommand]
    private async Task ShowAgendaList(AgendaListRowViewModel? list)
    {
        if (list is null)
        {
            return;
        }

        await ShowListCommand.ExecuteAsync(RightTab.Todo).ConfigureAwait(true);
        if (IsListMode && Todo.SelectedListId != list.Id)
        {
            await Todo.SelectListAsync(list.Id).ConfigureAwait(true);
        }

        RaiseListState();
    }

    /// <summary>리스트 삭제: its to-dos move to the built-in list, and the view widens to all of them.</summary>
    [RelayCommand]
    private async Task DeleteSelectedList()
    {
        if (SelectedAgendaList is not { CanDelete: true } list)
        {
            return;
        }

        await Todo.DeleteListAsync(list.Id).ConfigureAwait(true);
        _syncScheduler?.NotifySaved();
        RaiseListState();
    }

    private void RaiseListState()
    {
        foreach (AgendaListRowViewModel list in Todo.Lists)
        {
            list.IsCurrent = IsListMode && TabIsTodo && list.IsSelected;
        }

        OnPropertyChanged(nameof(IsNavTodo));
        OnPropertyChanged(nameof(AreSidebarListsShown));
        OnPropertyChanged(nameof(SidebarListsTip));
        OnPropertyChanged(nameof(SelectedAgendaList));
        OnPropertyChanged(nameof(CanDeleteSelectedList));
        OnPropertyChanged(nameof(ListTitle));
        OnPropertyChanged(nameof(ListSubtitle));
        OnPropertyChanged(nameof(ViewTitle));
        OnPropertyChanged(nameof(ViewSubtitle));
    }
}

/// <summary>
/// A row in the day panel that is not on the day: an upcoming event with its date and weekday, or
/// a to-do still owed elsewhere with its when ("지남 · 10/12 월 15:00").
/// </summary>
public sealed class PanelAgendaRow(TodoItemViewModel row, string lead, string sub, bool isLate)
{
    public TodoItemViewModel Row { get; } = row;

    /// <summary>An upcoming event's date, "10/14", in the panel's left column.</summary>
    public string Lead { get; } = lead;

    public string Sub { get; } = sub;

    public bool IsLate { get; } = isLate;
}
