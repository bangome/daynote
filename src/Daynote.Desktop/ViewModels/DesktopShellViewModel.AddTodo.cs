using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
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

    /// <summary>The day panel's + 할 일: a to-do on the selected date.</summary>
    [RelayCommand]
    private Task AddDayTodo() => OpenAddTodoAsync(SelectedDate, listId: null);

    /// <summary>The 할 일 view's + 할 일: a to-do on today, in the list being looked at.</summary>
    [RelayCommand]
    private Task AddListTodo() => OpenAddTodoAsync(LocalDates.Today(_clock), Todo.SelectedListId);

    private async Task OpenAddTodoAsync(LocalDate date, Guid? listId)
    {
        IsPaletteOpen = false;
        await TodoEntry.ResetAsync(LocalDates.ToDateOnly(date), Todo.Lists, IsDark, listId).ConfigureAwait(true);
        IsAddTodoOpen = true;
    }

    [RelayCommand]
    private void CloseAddTodo() => IsAddTodoOpen = false;

    /// <summary>추가: writes the item and closes the card. The day panel and the list show it arriving.</summary>
    [RelayCommand]
    private async Task CommitAddTodo()
    {
        if (!IsAddTodoOpen || !TodoEntry.CanAdd)
        {
            return;
        }

        ClockSnapshot snapshot = _clock.Read();
        AgendaItem made = TodoEntry.Compose(Guid.NewGuid(), snapshot.UtcInstant);
        IsAddTodoOpen = false;

        await _agenda.SaveAsync(made).ConfigureAwait(true);

        // No note is saved along with it, so nothing else is going to set a sync off.
        _syncScheduler?.NotifySaved();
        await Todo.RefreshAsync().ConfigureAwait(true);
    }
}
