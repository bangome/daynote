using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.App.Shell.Product;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Core.Time;

namespace Daynote.Desktop.ViewModels;

/// <summary>
/// The add-to-do card: the phone's to-do sheet on the desktop, over the window. The same draft
/// (<see cref="TodoEntryViewModel"/>) and the same fields — 할 일 / 일정, 제목, 날짜, 시간, 반복,
/// 내용, 목록.
/// </summary>
/// <remarks>
/// A note's body never makes a to-do (docs/TODOS.md, 2026-10-10), so this is the desktop's way to
/// one: + 할 일 in the day panel opens it on the selected date, and the 할 일 view's opens it on
/// today in the list being looked at. What it makes points at no note.
/// </remarks>
public sealed partial class DesktopShellViewModel
{
    private TodoEntryViewModel? _todoEntry;

    /// <summary>The card's draft. Built on first use, since it reads the month off the store.</summary>
    public TodoEntryViewModel TodoEntry => _todoEntry ??= new TodoEntryViewModel(_clock, _repository);

    [ObservableProperty]
    private bool _isAddTodoOpen;

    /// <summary>The card's top right while making one: the day it opened on, "10월 13일 (화)".</summary>
    [ObservableProperty]
    private string _addTodoSource = string.Empty;

    /// <summary>The day panel's + 할 일: a to-do on the selected date.</summary>
    [RelayCommand]
    private Task AddDayTodo() => OpenAddTodoAsync(SelectedDate, listId: null, AgendaKind.Task);

    /// <summary>The 할 일 view's + 할 일: a to-do on today, in the list being looked at.</summary>
    [RelayCommand]
    private Task AddListTodo() => OpenAddTodoAsync(LocalDates.Today(_clock), Todo.SelectedListId, AgendaKind.Task);

    private async Task OpenAddTodoAsync(LocalDate date, Guid? listId, AgendaKind kind = AgendaKind.Task)
    {
        IsPaletteOpen = false;
        await TodoEntry.ResetAsync(LocalDates.ToDateOnly(date), Todo.Lists, IsDark, listId).ConfigureAwait(true);
        AddTodoSource = LocalDates.ToDateOnly(date).ToString(AppStrings.TodoEntryDateFormat, LocalizationService.Instance.Culture);
        if (kind == AgendaKind.Event)
        {
            TodoEntry.SelectKindCommand.Execute(AgendaKind.Event);
        }

        IsAddTodoOpen = true;
    }

    [RelayCommand]
    private void CloseAddTodo() => IsAddTodoOpen = false;

    /// <summary>
    /// 추가: writes the item and closes the card. The day panel and the list show it arriving. In
    /// its editing face the button is 저장 and writes over the item (DesktopShellViewModel.TodoActions).
    /// </summary>
    [RelayCommand]
    private async Task CommitAddTodo()
    {
        if (!IsAddTodoOpen || !TodoEntry.CanAdd)
        {
            return;
        }

        if (TodoEntry.IsEditing)
        {
            await SaveEditedTodoAsync().ConfigureAwait(true);
            return;
        }

        ClockSnapshot snapshot = _clock.Read();
        AgendaItem made = TodoEntry.Compose(Guid.NewGuid(), snapshot.UtcInstant);
        IsAddTodoOpen = false;
        await SaveMadeAsync(made).ConfigureAwait(true);
    }

    /// <summary>삭제, in the card's editing face: the item it was opened on, in the scope it was opened in.</summary>
    [RelayCommand]
    private async Task DeleteEditedTodo()
    {
        if (!IsAddTodoOpen || !TodoEntry.IsEditing || _actionRow is not { } row)
        {
            return;
        }

        IsAddTodoOpen = false;
        await DeleteNowAsync(row, _editScope).ConfigureAwait(true);
    }
}
