using Daynote.App.Composition;
using Daynote.App.Notes;
using Daynote.Core.Domain;
using Daynote.Core.Notes;

namespace Daynote.Desktop.ViewModels;

/// <summary>
/// What the menu bar popover asks of the window behind it (menu bar design §01).
/// </summary>
public sealed partial class DesktopShellViewModel
{
    /// <summary>
    /// Adds a line typed into the popover to today's note, through the same store and revision
    /// check as the editor, so it syncs like any other edit.
    /// </summary>
    /// <remarks>
    /// The editor may hold today's note with unsaved typing. It is flushed first — writing under it
    /// would have one of the two saves refused as a conflict — and re-read afterwards, so the line
    /// is on screen if the note is open and the editor's revision is the new one.
    /// </remarks>
    public async Task<MenuBarAppendResult> AppendLineToTodayAsync(
        AppendNoteLine append,
        string line,
        bool newNote,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(append);

        FlushResult flush = await Notes.FlushAsync(FlushReason.NoteChange, cancellationToken).ConfigureAwait(true);
        if (!flush.CanProceed)
        {
            return MenuBarAppendResult.Failed;
        }

        try
        {
            await append.ExecuteAsync(LocalDates.Today(_clock), line, newNote, cancellationToken).ConfigureAwait(true);
        }
        catch (RecoverableNoteException)
        {
            return MenuBarAppendResult.Failed;
        }

        SyncReloadResult reload = await Notes.ReloadAfterSyncAsync(cancellationToken).ConfigureAwait(true);
        await RefreshAfterSyncAsync(reload).ConfigureAwait(true);
        return newNote ? MenuBarAppendResult.NewNote : MenuBarAppendResult.Appended;
    }

    /// <summary>The popover's 보기 and "Daynote 열기": the window on that day, out of the timeline.</summary>
    public async Task ShowDateFromMenuBarAsync(LocalDate date)
    {
        IsSettingsOpen = false;
        await SelectDateAsync(date).ConfigureAwait(true);
    }

    /// <summary>
    /// The popover's 자세히…: the add card on today, with what was typed as its title, so kind,
    /// date, time, repeat, description and list are all there.
    /// </summary>
    public async Task OpenAddTodoFromMenuBarAsync(string title)
    {
        IsSettingsOpen = false;
        await OpenAddTodoAsync(LocalDates.Today(_clock), listId: null).ConfigureAwait(true);
        TodoEntry.Title = title;
    }

    /// <summary>The popover's 설정.</summary>
    public void OpenSettingsFromMenuBar()
    {
        SettingsViewModel?.SelectSectionCommand.Execute(SettingsSection.General);
        IsSettingsOpen = true;
    }
}
