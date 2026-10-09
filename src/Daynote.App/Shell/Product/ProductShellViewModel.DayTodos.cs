using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.Core.Agenda;
using Daynote.Core.Time;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The day panel's to-do section (docs/TODOS.md §11, design §04).
/// </summary>
/// <remarks>
/// Its own file because the panel stopped being a filter over the 할 일 tab's rows and became a
/// projection out of the to-do store, which is a concern of its own — the same reason the @
/// command's editor half lives in <c>EditorCardView.Capture.cs</c>.
/// </remarks>
public sealed partial class ProductShellViewModel
{
    /// <summary>The selected day's to-dos, for the right panel: the full list filtered to one date.</summary>
    public ObservableCollection<TodoItemViewModel> DayTodos { get; } = [];

    /// <summary>
    /// What was finished on this day. A separate list because the design collapses it behind a
    /// count: the day is a record of itself, so finished rows belong here rather than nowhere,
    /// but they are not what somebody opens the panel to read.
    /// </summary>
    public ObservableCollection<TodoItemViewModel> DayTodosDone { get; } = [];

    [ObservableProperty]
    private string _dayDoneCountText = string.Empty;

    [ObservableProperty]
    private bool _hasDayDone;

    private void RefreshDayTodos()
    {
        if (_dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(RefreshDayTodos);
            return;
        }

        // Projected from everything loaded rather than filtered out of the 할 일 tab's rows: that
        // list carries only the *next* outstanding occurrence of a rule, so filtering it by date
        // would leave a weekly to-do missing from every day but one.
        ClockSnapshot snapshot = _clock.Read();
        DateTime now = snapshot.UtcInstant.ToOffset(snapshot.LocalUtcOffset).DateTime;
        AgendaDayView day = AgendaDay.For(LocalDates.ToDateOnly(SelectedDate), Todo.All);

        DayTodos.Clear();
        foreach (AgendaDayRow row in day.Open)
        {
            DayTodos.Add(Todo.Row(row, now, TodoRowScope.Day));
        }

        DayTodosDone.Clear();
        foreach (AgendaDayRow row in day.Done)
        {
            DayTodosDone.Add(Todo.Row(row, now, TodoRowScope.Day));
        }

        DayTodoCountText = string.Format(
            CultureInfo.CurrentCulture, AppStrings.DeskDayTodoRemainingFormat, DayTodos.Count);
        DayDoneCountText = string.Format(
            CultureInfo.CurrentCulture, AppStrings.DeskDayTodoDoneFormat, DayTodosDone.Count);
        HasDayDone = DayTodosDone.Count > 0;
        IsDayTodoEmpty = DayTodos.Count == 0 && DayTodosDone.Count == 0;
    }

}
