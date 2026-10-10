using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Shell.Product;
using Daynote.Core.Agenda;
using Daynote.Core.Time;
using Daynote.Motion;

namespace Daynote.Desktop.ViewModels;

/// <summary>
/// A to-do row's 편집 and 삭제 on the desktop: from its right-click menu, or the Delete key on a
/// focused row. The phone's swipe actions, through the same use cases.
/// </summary>
/// <remarks>
/// A delete is not confirmed; it is undone instead, from the "삭제됨 · 실행 취소" line that stays
/// four seconds. A repeating to-do asks first whether this day or every repeat is meant, for a
/// delete and an edit alike. An edit opens the add-to-do card on the item, saving over it.
/// </remarks>
public sealed partial class DesktopShellViewModel
{
    private DeleteAgendaItem? _deleteAgenda;
    private EditAgendaItem? _editAgenda;

    /// <summary>The row the repeat question, or the card in its editing face, is about.</summary>
    private AgendaDayRow? _actionRow;

    private AgendaRepeatScope _editScope;

    private AgendaDeletion? _lastDeletion;

    private CancellationTokenSource? _undoWait;

    private DeleteAgendaItem DeleteAgenda => _deleteAgenda ??= new DeleteAgendaItem(_agenda);

    private EditAgendaItem EditAgenda => _editAgenda ??= new EditAgendaItem(_agenda);

    /// <summary>The repeat question: this day only, every repeat, or 취소.</summary>
    [ObservableProperty]
    private bool _isRepeatChoiceOpen;

    /// <summary>True when the question is about a delete, false when about an edit.</summary>
    [ObservableProperty]
    private bool _isRepeatChoiceDelete;

    [ObservableProperty]
    private string _repeatChoiceTitle = string.Empty;

    /// <summary>"삭제됨 · 실행 취소" is up.</summary>
    [ObservableProperty]
    private bool _isUndoShown;

    /// <summary>How long the undo line waits before it closes; a seam, so a test does not wait on the wall clock.</summary>
    public Func<TimeSpan, CancellationToken, Task> UndoDelay { get; set; } = Task.Delay;

    private Task DeleteTodoAsync(AgendaDayRow row)
    {
        if (row.IsOccurrence)
        {
            AskRepeat(row, delete: true);
            return Task.CompletedTask;
        }

        return DeleteNowAsync(row, AgendaRepeatScope.Occurrence);
    }

    private Task EditTodoAsync(AgendaDayRow row)
    {
        if (row.IsOccurrence)
        {
            AskRepeat(row, delete: false);
            return Task.CompletedTask;
        }

        return OpenEditTodoAsync(row, AgendaRepeatScope.Occurrence);
    }

    private void AskRepeat(AgendaDayRow row, bool delete)
    {
        _actionRow = row;
        IsRepeatChoiceDelete = delete;
        RepeatChoiceTitle = row.Item.Title;
        IsRepeatChoiceOpen = true;
    }

    [RelayCommand]
    private Task ChooseThisOccurrence() => ChooseAsync(AgendaRepeatScope.Occurrence);

    [RelayCommand]
    private Task ChooseEveryRepeat() => ChooseAsync(AgendaRepeatScope.Series);

    [RelayCommand]
    private void CloseRepeatChoice() => IsRepeatChoiceOpen = false;

    private Task ChooseAsync(AgendaRepeatScope scope)
    {
        IsRepeatChoiceOpen = false;
        if (_actionRow is not { } row)
        {
            return Task.CompletedTask;
        }

        return IsRepeatChoiceDelete ? DeleteNowAsync(row, scope) : OpenEditTodoAsync(row, scope);
    }

    private async Task DeleteNowAsync(AgendaDayRow row, AgendaRepeatScope scope)
    {
        // A tick still held on the row would write itself onto something that is gone.
        Ticks.Cancel(TodoItemViewModel.KeyOf(row));
        AgendaDeletion deletion = await DeleteAgenda.DeleteAsync(row, scope).ConfigureAwait(true);
        _syncScheduler?.NotifySaved();
        await Todo.RefreshAsync().ConfigureAwait(true);

        _undoWait?.Cancel();
        _undoWait?.Dispose();
        var wait = new CancellationTokenSource();
        _undoWait = wait;
        _lastDeletion = deletion;
        IsUndoShown = true;
        _ = CloseUndoAfterAsync(wait);
    }

    private async Task CloseUndoAfterAsync(CancellationTokenSource wait)
    {
        try
        {
            await UndoDelay(Choreography.NoticeHold, wait.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ReferenceEquals(_undoWait, wait))
        {
            IsUndoShown = false;
            _lastDeletion = null;
        }
    }

    /// <summary>실행 취소: puts back what the last delete took.</summary>
    [RelayCommand]
    private async Task UndoDelete()
    {
        _undoWait?.Cancel();
        IsUndoShown = false;
        if (_lastDeletion is not { } deletion)
        {
            return;
        }

        _lastDeletion = null;
        await DeleteAgenda.RestoreAsync(deletion).ConfigureAwait(true);
        _syncScheduler?.NotifySaved();
        await Todo.RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>The add-to-do card on the item. Every repeat is edited from the rule itself.</summary>
    private async Task OpenEditTodoAsync(AgendaDayRow row, AgendaRepeatScope scope)
    {
        if (scope == AgendaRepeatScope.Series && !row.Item.IsSeries && row.Item.SeriesId is { } seriesId)
        {
            if (await _agenda.GetAsync(seriesId).ConfigureAwait(true) is not { } series)
            {
                return;
            }

            row = row with { Item = series };
        }

        IsPaletteOpen = false;
        _actionRow = row;
        _editScope = scope;
        await TodoEntry.LoadAsync(row, Todo.Lists, IsDark, occurrenceOnly: scope == AgendaRepeatScope.Occurrence)
            .ConfigureAwait(true);
        IsAddTodoOpen = true;
    }

    /// <summary>저장: the card over the item it was opened on — same id, same note, a new stamp.</summary>
    private async Task SaveEditedTodoAsync()
    {
        if (_actionRow is not { } row)
        {
            return;
        }

        ClockSnapshot snapshot = _clock.Read();
        AgendaItem draft = TodoEntry.Compose(Guid.NewGuid(), snapshot.UtcInstant);
        IsAddTodoOpen = false;
        Ticks.Cancel(TodoItemViewModel.KeyOf(row));
        await EditAgenda.SaveAsync(row, draft, _editScope).ConfigureAwait(true);
        _syncScheduler?.NotifySaved();
        await Todo.RefreshAsync().ConfigureAwait(true);
    }
}
