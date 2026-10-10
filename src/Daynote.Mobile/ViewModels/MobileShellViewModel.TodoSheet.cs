using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.Core.Agenda;
using Daynote.Core.Time;

namespace Daynote.Mobile.ViewModels;

/// <summary>
/// The to-do sheet: a to-do or an event made in fields of its own — 제목, 날짜, 시간, 반복, 내용,
/// 목록 — rather than typed into the note's body.
/// </summary>
/// <remarks>
/// The editor's + button opens it, and so does an @ typed at the start of a line, the widgets'
/// "@ 할 일" and the <c>daynote://capture?at=1</c> link. It is the phone's only way to a to-do:
/// the desktop's inline @ reading is not used here. The sheet writes nothing into the note, and
/// the item only points back at it through <see cref="AgendaItem.SourceNoteId"/>.
/// </remarks>
public sealed partial class MobileShellViewModel
{
    /// <summary>The sheet's draft.</summary>
    public TodoEntryViewModel Entry { get; }

    [ObservableProperty]
    private bool _isTodoSheetOpen;

    partial void OnIsTodoSheetOpenChanged(bool value) => OnPropertyChanged(nameof(ShowDock));

    /// <summary>
    /// Opens a fresh draft for the note in the editor, on that note's day. Nothing happens without
    /// a saved note to point back at.
    /// </summary>
    [RelayCommand]
    private async Task OpenTodoSheet()
    {
        if (!IsEditorOpen || Notes.SelectedTab is not { IsProjection: false })
        {
            return;
        }

        await Entry.ResetAsync(LocalDates.ToDateOnly(SelectedDate), Todo.Lists, IsDark).ConfigureAwait(true);
        IsTodoSheetOpen = true;
    }

    [RelayCommand]
    private void CloseTodoSheet() => IsTodoSheetOpen = false;

    /// <summary>
    /// 추가: writes the item, closes the sheet, and lets the note's own collection say it arrived
    /// (phone §02).
    /// </summary>
    [RelayCommand]
    private async Task CommitTodoSheet()
    {
        if (!IsTodoSheetOpen || !Entry.CanAdd || Notes.SelectedTab is not { IsProjection: false, Id: { } noteId })
        {
            return;
        }

        ClockSnapshot snapshot = _clock.Read();
        AgendaItem made = Entry.Compose(noteId.Value, Guid.NewGuid(), snapshot.UtcInstant);
        IsTodoSheetOpen = false;

        await _agenda.SaveAsync(made).ConfigureAwait(true);

        // The note is not touched, so no note save is coming to set a sync off.
        _syncScheduler?.NotifySaved();
        await RefreshTodosAsync().ConfigureAwait(true);
        await FlashJustMadeAsync(made, snapshot.UtcInstant.ToOffset(snapshot.LocalUtcOffset).DateTime)
            .ConfigureAwait(true);
    }
}
