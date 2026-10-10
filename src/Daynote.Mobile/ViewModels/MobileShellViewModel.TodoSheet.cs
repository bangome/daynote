using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Shell.Product;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Core.Time;

namespace Daynote.Mobile.ViewModels;

/// <summary>
/// The to-do sheet: a to-do or an event made in fields of its own — 제목, 날짜, 시간, 반복, 내용,
/// 목록.
/// </summary>
/// <remarks>
/// To-dos and notes are separate (docs/TODOS.md, 2026-10-10): the sheet is opened from the to-do
/// surfaces — the day's 할 일, the 할 일 tab, the tablet's day panel and the widgets — and never
/// from a note. What it makes points at no note.
/// </remarks>
public sealed partial class MobileShellViewModel
{
    /// <summary>The sheet's draft.</summary>
    public TodoEntryViewModel Entry { get; }

    [ObservableProperty]
    private bool _isTodoSheetOpen;

    /// <summary>
    /// "10/8에 추가됨" while something just made went to a day other than the one on screen, for the
    /// tablet panel's line (motion spec M4). Null the rest of the time.
    /// </summary>
    [ObservableProperty]
    private string? _madeElsewhereText;

    /// <summary>The day the item behind <see cref="MadeElsewhereText"/> went to, for 보기.</summary>
    private LocalDate _madeElsewhereDate;

    partial void OnIsTodoSheetOpenChanged(bool value) => OnPropertyChanged(nameof(ShowDock));

    /// <summary>이 날의 할 일's +: a to-do on the day on screen.</summary>
    [RelayCommand]
    private Task AddDayTodo() => OpenTodoSheetAsync(SelectedDate, listId: null);

    /// <summary>The 할 일 tab's +: a to-do on today, in the list being looked at.</summary>
    [RelayCommand]
    private Task AddListTodo() => OpenTodoSheetAsync(LocalDates.Today(_clock), Todo.SelectedListId);

    /// <summary>A fresh draft on <paramref name="date"/>, filed in <paramref name="listId"/> or the built-in list.</summary>
    private async Task OpenTodoSheetAsync(LocalDate date, Guid? listId)
    {
        await Entry.ResetAsync(LocalDates.ToDateOnly(date), Todo.Lists, IsDark, listId).ConfigureAwait(true);
        IsTodoSheetOpen = true;
    }

    [RelayCommand]
    private void CloseTodoSheet() => IsTodoSheetOpen = false;

    /// <summary>
    /// 추가: writes the item and closes the sheet. The lists it lands in show it arriving. In its
    /// editing face the button is 저장 and writes over the item it was opened on.
    /// </summary>
    [RelayCommand]
    private async Task CommitTodoSheet()
    {
        if (!IsTodoSheetOpen || !Entry.CanAdd)
        {
            return;
        }

        ClockSnapshot snapshot = _clock.Read();
        AgendaItem made = Entry.Compose(Guid.NewGuid(), snapshot.UtcInstant);
        IsTodoSheetOpen = false;

        if (Entry.IsEditing)
        {
            await SaveEditedTodoAsync(made).ConfigureAwait(true);
            return;
        }

        await _agenda.SaveAsync(made).ConfigureAwait(true);

        // No note is saved along with it, so nothing else is going to set a sync off.
        _syncScheduler?.NotifySaved();
        await RefreshTodosAsync().ConfigureAwait(true);

        // An undated to-do went to no day at all, so there is nowhere else to point to.
        if (made.Anchor is { } anchor && LocalDates.FromDateOnly(DateOnly.FromDateTime(anchor.Value)) != SelectedDate)
        {
            _madeElsewhereDate = LocalDates.FromDateOnly(DateOnly.FromDateTime(anchor.Value));
            MadeElsewhereText = null;
            MadeElsewhereText = MobileStrings.Format(
                "MobileTodoAddedTo",
                DateOnly.FromDateTime(anchor.Value).ToString(
                    MobileStrings.Get("MobileTodoAddedDateFormat"),
                    Daynote.App.Localization.LocalizationService.Instance.Culture));
        }
    }

    /// <summary>보기: the day it went to, which is the one place it can be seen in context.</summary>
    [RelayCommand]
    private async Task ViewMadeElsewhere()
    {
        MadeElsewhereText = null;
        await CloseEditorAsync().ConfigureAwait(true);
        await SelectDateAsync(_madeElsewhereDate).ConfigureAwait(true);
    }
}
