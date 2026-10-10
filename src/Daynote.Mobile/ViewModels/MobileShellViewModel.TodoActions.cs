using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Shell.Product;
using Daynote.Core.Agenda;
using Daynote.Motion;

namespace Daynote.Mobile.ViewModels;

/// <summary>
/// A to-do row's swipe actions: 편집 and 삭제, the question a repeating one asks first, and the
/// "삭제됨 · 실행 취소" line a delete leaves behind for four seconds.
/// </summary>
/// <remarks>
/// <para>
/// A delete is not confirmed beforehand. It is undone afterwards instead, which costs the user
/// nothing when they meant it and one tap when they did not. Undo puts back exactly what was
/// removed (<see cref="DeleteAgendaItem.RestoreAsync"/>).
/// </para>
/// <para>
/// An occurrence of a rule asks which of its days is meant, for a delete and for an edit alike:
/// "이 항목만" or every repeat. The one-off never asks.
/// </para>
/// </remarks>
public sealed partial class MobileShellViewModel
{
    private DeleteAgendaItem? _deleteAgenda;
    private EditAgendaItem? _editAgenda;

    /// <summary>The row the repeat question, or the sheet in its editing face, is about.</summary>
    private AgendaDayRow? _actionRow;

    /// <summary>Which of the rule's days the sheet is editing: the occurrence, or all of them.</summary>
    private AgendaRepeatScope _editScope;

    /// <summary>What the last delete took, until its undo line closes.</summary>
    private AgendaDeletion? _lastDeletion;

    private CancellationTokenSource? _undoWait;

    private DeleteAgendaItem DeleteAgenda => _deleteAgenda ??= new DeleteAgendaItem(_agenda);

    private EditAgendaItem EditAgenda => _editAgenda ??= new EditAgendaItem(_agenda);

    /// <summary>The repeat question: "이 항목만" / every repeat / 취소.</summary>
    [ObservableProperty]
    private bool _isRepeatChoiceOpen;

    /// <summary>True when the question is about a delete, false when about an edit.</summary>
    [ObservableProperty]
    private bool _isRepeatChoiceDelete;

    /// <summary>The question's heading: the to-do's title.</summary>
    [ObservableProperty]
    private string _repeatChoiceTitle = string.Empty;

    /// <summary>"삭제됨 · 실행 취소" is up.</summary>
    [ObservableProperty]
    private bool _isUndoShown;

    /// <summary>How long the undo line waits before it closes; a seam, so a test does not wait on the wall clock.</summary>
    public Func<TimeSpan, CancellationToken, Task> UndoDelay { get; set; } = Task.Delay;

    partial void OnIsRepeatChoiceOpenChanged(bool value) => OnPropertyChanged(nameof(ShowDock));

    /// <summary>삭제: a one-off goes at once; an occurrence asks which days first.</summary>
    private Task DeleteTodoAsync(AgendaDayRow row)
    {
        if (row.IsOccurrence)
        {
            AskRepeat(row, delete: true);
            return Task.CompletedTask;
        }

        return DeleteNowAsync(row, AgendaRepeatScope.Occurrence);
    }

    /// <summary>편집: a one-off opens the sheet on it; an occurrence asks which days first.</summary>
    private Task EditTodoAsync(AgendaDayRow row)
    {
        if (row.IsOccurrence)
        {
            AskRepeat(row, delete: false);
            return Task.CompletedTask;
        }

        return OpenEditSheetAsync(row, AgendaRepeatScope.Occurrence);
    }

    private void AskRepeat(AgendaDayRow row, bool delete)
    {
        _actionRow = row;
        IsRepeatChoiceDelete = delete;
        RepeatChoiceTitle = row.Item.Title;
        IsRepeatChoiceOpen = true;
    }

    /// <summary>"이 항목만".</summary>
    [RelayCommand]
    private Task ChooseThisOccurrence() => ChooseAsync(AgendaRepeatScope.Occurrence);

    /// <summary>"이후 모든 반복" / "모든 반복".</summary>
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

        return IsRepeatChoiceDelete ? DeleteNowAsync(row, scope) : OpenEditSheetAsync(row, scope);
    }

    private async Task DeleteNowAsync(AgendaDayRow row, AgendaRepeatScope scope)
    {
        // A tick still held on the row would write itself onto something that is gone.
        Ticks.Cancel(TodoItemViewModel.KeyOf(row));
        AgendaDeletion deletion = await DeleteAgenda.DeleteAsync(row, scope).ConfigureAwait(true);

        // No note changed, so nothing else is going to set a sync off.
        _syncScheduler?.NotifySaved();
        await RefreshTodosAsync().ConfigureAwait(true);
        ShowUndo(deletion);
    }

    /// <summary>The line opens, or opens again for a newer delete, and closes itself after four seconds.</summary>
    private void ShowUndo(AgendaDeletion deletion)
    {
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
        await RefreshTodosAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// The sheet in its editing face, filled from the row. Every repeat is edited from the rule
    /// itself, so the sheet shows the rule's title and its 반복 rather than one day's override.
    /// </summary>
    private async Task OpenEditSheetAsync(AgendaDayRow row, AgendaRepeatScope scope)
    {
        if (scope == AgendaRepeatScope.Series && !row.Item.IsSeries && row.Item.SeriesId is { } seriesId)
        {
            if (await _agenda.GetAsync(seriesId).ConfigureAwait(true) is not { } series)
            {
                return;
            }

            row = row with { Item = series };
        }

        _actionRow = row;
        _editScope = scope;
        await Entry.LoadAsync(row, Todo.Lists, IsDark, occurrenceOnly: scope == AgendaRepeatScope.Occurrence)
            .ConfigureAwait(true);
        IsTodoSheetOpen = true;
    }

    /// <summary>저장: the draft over the item it was opened on — same id, same note, a new stamp.</summary>
    private async Task SaveEditedTodoAsync(AgendaItem draft)
    {
        if (_actionRow is not { } row)
        {
            return;
        }

        Ticks.Cancel(TodoItemViewModel.KeyOf(row));
        await EditAgenda.SaveAsync(row, draft, _editScope).ConfigureAwait(true);
        _syncScheduler?.NotifySaved();
        await RefreshTodosAsync().ConfigureAwait(true);
    }
}
