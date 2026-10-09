using Daynote.App.Composition;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;

namespace Daynote.App.Glance;

/// <summary>
/// The watch's "노트에 한 줄": a sentence put at the end of a day's note, as said (Apple Watch
/// design §03 and §99).
/// </summary>
/// <remarks>
/// The day's first note, because it is the one the day opens on and the one the phone's "+ 새
/// 노트" lands in; a day with none gets one, which §99 asks for. Written through the repository
/// rather than an editor, so the caller has to save whatever the editor holds first and load the
/// day again afterwards — otherwise the next autosave would carry a revision this has moved past.
/// </remarks>
public static class GlanceNoteLine
{
    public static async Task<bool> AppendAsync(
        INoteRepository repository,
        string line,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        LocalDate day = LocalDates.FromDateOnly(date);
        DayWorkspace workspace = await repository.GetDayWorkspaceStateAsync(day, cancellationToken).ConfigureAwait(true);
        Note? first = workspace.Notes.IsProjectionOnly ? null : workspace.Notes.Notes.FirstOrDefault();

        if (first is not { Id: { } id })
        {
            id = NoteId.Create(Guid.NewGuid()).Value;
            workspace = await repository.CreateNoteAsync(day, default, id, cancellationToken).ConfigureAwait(true);
            await repository.SaveNoteAsync(
                new NoteSaveRequest(id, day, string.Empty, line.Trim(), workspace.RevisionOf(id), IsNew: false, HasCustomTitle: false),
                cancellationToken).ConfigureAwait(true);
            return true;
        }

        string body = first.Body.TrimEnd();
        await repository.SaveNoteAsync(
            new NoteSaveRequest(
                id,
                day,
                first.HasCustomTitle ? first.Title : string.Empty,
                body.Length == 0 ? line.Trim() : body + "\n" + line.Trim(),
                workspace.RevisionOf(id),
                IsNew: false,
                first.HasCustomTitle),
            cancellationToken).ConfigureAwait(true);
        return true;
    }
}
