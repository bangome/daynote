using CommunityToolkit.Mvvm.Input;
using Daynote.Core.Notes;

namespace Daynote.App.Notes;

public enum MarkdownCommand
{
    Bold,
    Italic,
    InlineCode,
    BulletedList,
    NumberedList,
}

public sealed partial class NoteWorkspaceViewModel
{
    partial void OnEditorTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsEditorEmpty));
        if (_suppressEditorSync || SelectedTab is not { } tab)
        {
            return;
        }

        tab.Body = value;
        tab.RefreshPreview();
        tab.SaveState = NoteSaveState.Dirty;
        SaveStatus = SaveStatusKind.Dirty;
        _autosave.MarkDirty(BuildRequest(tab));
    }

    /// <summary>Applies a Markdown toolbar command to the current selection and returns the new selection.</summary>
    public MarkdownEdit ApplyFormat(MarkdownCommand command, int selectionStart, int selectionLength)
    {
        MarkdownEdit edit = command switch
        {
            MarkdownCommand.Bold => MarkdownSyntax.ToggleBold(EditorText, selectionStart, selectionLength),
            MarkdownCommand.Italic => MarkdownSyntax.ToggleItalic(EditorText, selectionStart, selectionLength),
            MarkdownCommand.InlineCode => MarkdownSyntax.ToggleInlineCode(EditorText, selectionStart, selectionLength),
            MarkdownCommand.BulletedList => MarkdownSyntax.ToggleBulletedList(EditorText, selectionStart, selectionLength),
            MarkdownCommand.NumberedList => MarkdownSyntax.ToggleNumberedList(EditorText, selectionStart, selectionLength),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };

        EditorText = edit.Text;
        return edit;
    }

    /// <summary>Flushes pending autosave. On failure the dirty text is retained and the caller aborts.</summary>
    public async Task<FlushResult> FlushAsync(FlushReason reason, CancellationToken cancellationToken = default)
    {
        IsGuarded = true;
        try
        {
            bool hadDirty = _autosave.IsDirty;
            FlushResult result = await _autosave.FlushAsync(reason, cancellationToken).ConfigureAwait(true);
            if (result.CanProceed)
            {
                OnFlushSucceeded(hadDirty);
            }
            else if (result.Error is { } error)
            {
                ApplySaveError(error);
            }

            return result;
        }
        finally
        {
            IsGuarded = false;
        }
    }

    [RelayCommand]
    private Task SelectNote(NoteTabViewModel? tab) => SelectNoteAsync(tab);

    [RelayCommand]
    private async Task RetryAsync()
    {
        FlushResult result = await FlushAsync(FlushReason.NoteChange).ConfigureAwait(true);
        if (result.CanProceed)
        {
            ClearSaveError();
        }
    }

    private void OnFlushSucceeded(bool hadDirty)
    {
        _projectionOnly = _projectionOnly && !hadDirty;
        if (SelectedTab is { } tab && tab.SaveState != NoteSaveState.Clean)
        {
            tab.SaveState = NoteSaveState.Clean;
        }

        ClearSaveError();
        SaveStatus = hadDirty ? SaveStatusKind.Saved : SaveStatus == SaveStatusKind.Error ? SaveStatusKind.None : SaveStatus;
    }

    private void OnRecoverableError(RecoverableNoteError error) => Post(() => ApplySaveError(error));

    private void ApplySaveError(RecoverableNoteError error)
    {
        SaveStatus = SaveStatusKind.Error;
        SaveErrorMessage = error.Message;
        if (SelectedTab is { } tab)
        {
            tab.SaveState = NoteSaveState.Error;
        }

        OnPropertyChanged(nameof(HasSaveError));
    }

    private void ClearSaveError()
    {
        if (SaveErrorMessage is not null || SaveStatus == SaveStatusKind.Error)
        {
            SaveErrorMessage = null;
            if (SaveStatus == SaveStatusKind.Error)
            {
                SaveStatus = SaveStatusKind.None;
            }

            OnPropertyChanged(nameof(HasSaveError));
        }
    }

    private NoteSaveRequest BuildRequest(NoteTabViewModel tab) => new(
        tab.Id,
        SelectedDate,
        tab.Title,
        tab.Body,
        tab.Revision,
        tab.IsProjection,
        tab.HasCustomTitle);
}
