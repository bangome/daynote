using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.App.Shell.Product;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Core.Files;
using Daynote.Core.Notes;
using Daynote.Core.Time;

namespace Daynote.Desktop.ViewModels;

/// <summary>
/// The v2 month view (캘린더) and the agenda dots on the sidebar's month and the week strip.
/// </summary>
/// <remarks>
/// The view shares the sidebar calendar's month: its arrows are the same commands, and its grid is
/// rebuilt from the same cells, so the two never show different months.
/// </remarks>
public sealed partial class DesktopShellViewModel
{
    private const string SidebarListsKey = "product.sidebar-lists";

    private CalendarBoardViewModel? _calendarBoard;

    /// <summary>The 캘린더 view is filling the middle.</summary>
    [ObservableProperty]
    private bool _isCalendarMode;

    /// <summary>The lists under 할 일 were opened with the chevron (and stay so across launches).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AreSidebarListsShown), nameof(SidebarListsTip))]
    private bool _isSidebarListsOpen;

    public CalendarBoardViewModel CalendarBoard => _calendarBoard ??= new CalendarBoardViewModel(
        SelectBoardDayAsync, OpenBoardDayAsync, EditTodoAsync, OpenBoardFileAsync);

    public bool IsNavCalendar => IsCalendarMode;

    /// <summary>
    /// The lists show under 할 일 while the chevron says so, and always while one of them narrows the
    /// view — a selected row that could not be seen would be a filter nobody can take off.
    /// </summary>
    public bool AreSidebarListsShown =>
        IsSidebarListsOpen || (IsListMode && TabIsTodo && Todo.SelectedListId is not null);

    public string SidebarListsTip => AreSidebarListsShown ? AppStrings.DeskListsCollapse : AppStrings.DeskListsExpand;

    /// <summary>The header's 캘린더 toggle: into the month view, or back to the editor.</summary>
    [RelayCommand]
    private async Task ToggleCalendar()
    {
        if (IsCalendarMode)
        {
            IsCalendarMode = false;
            return;
        }

        await ShowCalendar().ConfigureAwait(true);
    }

    /// <summary>The sidebar's 캘린더: the month of the selected day.</summary>
    [RelayCommand]
    private async Task ShowCalendar()
    {
        IsPaletteOpen = false;
        if (IsEditorMode)
        {
            FlushResult flush = await Notes.FlushAsync(FlushReason.DateChange).ConfigureAwait(true);
            if (!flush.CanProceed)
            {
                return;
            }
        }

        IsTimelineMode = false;
        IsListMode = false;
        IsCalendarMode = true;
        await RefreshCalendarBoardAsync().ConfigureAwait(true);
    }

    /// <summary>오늘, beside the month view's arrows.</summary>
    [RelayCommand]
    private async Task CalendarToday()
    {
        LocalDate today = LocalDates.Today(_clock);
        await SelectDateAsync(today).ConfigureAwait(true);
        await Calendar.ShowSelectedAsync(today).ConfigureAwait(true);
    }

    /// <summary>The chevron beside 할 일. Folding the lists away while one filters the view widens it.</summary>
    [RelayCommand]
    private async Task ToggleSidebarLists()
    {
        bool showing = AreSidebarListsShown;
        IsSidebarListsOpen = !showing;
        if (!_loading)
        {
            _ = _settings.SetBoolAsync(SidebarListsKey, IsSidebarListsOpen);
        }

        if (showing && Todo.SelectedListId is not null && IsListMode && TabIsTodo)
        {
            await Todo.SelectListAsync(Todo.SelectedListId).ConfigureAwait(true);
        }

        RaiseListState();
    }

    partial void OnIsCalendarModeChanged(bool value) => RaiseViewState();

    /// <summary>Wires the dots and the month view to what they are drawn from. Called once from the constructor.</summary>
    private void AttachCalendar()
    {
        Calendar.AgendaDays = AgendaDaySet;
        Week.AgendaDays = AgendaDaySet;
        Todo.Refreshed += (_, _) =>
        {
            Calendar.RefreshAgendaDays();
            Week.RefreshAgendaDays();
            if (IsCalendarMode)
            {
                _ = RefreshCalendarBoardAsync();
            }
        };
        Calendar.Cells.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ViewSubtitle));
            if (IsCalendarMode)
            {
                ScheduleCalendarBoard();
            }
        };
    }

    private async Task LoadSidebarListsAsync(CancellationToken cancellationToken) =>
        IsSidebarListsOpen = await _settings.GetBoolAsync(SidebarListsKey, false, cancellationToken).ConfigureAwait(true);

    /// <summary>
    /// The dates a dated one-off to-do or event falls on (design v2 <c>taskBy</c>): a repeating one
    /// would put a dot under every day and say nothing.
    /// </summary>
    private IReadOnlySet<LocalDate> AgendaDaySet() =>
        Todo.All
            .Where(static item => !item.IsSeries && item.Status != AgendaStatus.Cancelled && item.Anchor is not null)
            .Select(static item => LocalDates.FromDateOnly(DateOnly.FromDateTime(item.Anchor!.Value.Value)))
            .ToHashSet();

    private bool _calendarBoardPending;

    /// <summary>The month's cells arrive one by one; the grid is rebuilt once they have.</summary>
    private void ScheduleCalendarBoard()
    {
        if (_calendarBoardPending)
        {
            return;
        }

        _calendarBoardPending = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _calendarBoardPending = false;
            _ = RefreshCalendarBoardAsync();
        });
    }

    private async Task RefreshCalendarBoardAsync()
    {
        IReadOnlyList<DayFile> files = await Files.ListAllAsync().ConfigureAwait(true);
        ClockSnapshot snapshot = _clock.Read();
        CalendarBoard.Build(new CalendarBoardViewModel.Input(
            [.. Calendar.Cells],
            SelectedDate,
            Todo.All,
            files,
            Todo.ListColor,
            snapshot.UtcInstant.ToOffset(snapshot.LocalUtcOffset).DateTime));
    }

    /// <summary>A click in a day: it becomes the selected day, and the month stays.</summary>
    private async Task SelectBoardDayAsync(LocalDate date)
    {
        await SelectDateAsync(date).ConfigureAwait(true);
        await RefreshCalendarBoardAsync().ConfigureAwait(true);
    }

    /// <summary>A day's number, or the card's heading: the day in the editor.</summary>
    private async Task OpenBoardDayAsync(LocalDate date)
    {
        CalendarBoard.Hover = null;
        IsCalendarMode = false;
        await SelectDateAsync(date).ConfigureAwait(true);
    }

    /// <summary>A file in a day: the files list, searched for it.</summary>
    private async Task OpenBoardFileAsync(DayFile file)
    {
        CalendarBoard.Hover = null;
        await ShowListCommand.ExecuteAsync(RightTab.Files).ConfigureAwait(true);
        Files.LibraryQuery = file.DisplayName;
    }

    /// <summary>A date heading in the 파일 view: that day, in the editor.</summary>
    private async Task OpenFileDayAsync(LocalDate date)
    {
        IsListMode = false;
        IsCalendarMode = false;
        await SelectDateAsync(date).ConfigureAwait(true);
    }

    private string CalendarSubtitle => string.Format(
        CultureInfo.CurrentCulture, AppStrings.DeskCalendarViewSubFormat, Calendar.MonthLabel);
}
