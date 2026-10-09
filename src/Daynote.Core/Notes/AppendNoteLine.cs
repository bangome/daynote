using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;

namespace Daynote.Core.Notes;

/// <summary>
/// Adds one line to the end of a day's last note, or to a note of its own (menu bar design §01,
/// "노트에 한 줄").
/// </summary>
/// <remarks>
/// The rule the menu bar and the watch share: a line goes on the end of the day's last note, and a
/// day with no note gets one made for it. Last rather than first because the last note is the one
/// most recently started, which is where an afterthought belongs.
/// <para>
/// It writes through the repository's revision check like every other save, so a caller holding
/// that note open in an editor must flush the editor first and re-read it afterwards; otherwise one
/// of the two writes is refused as a conflict rather than silently overwriting the other.
/// </para>
/// </remarks>
public sealed class AppendNoteLine(INoteRepository repository, Func<NoteId> nextId)
{
    private readonly INoteRepository repository =
        repository ?? throw new ArgumentNullException(nameof(repository));

    private readonly Func<NoteId> nextId = nextId ?? throw new ArgumentNullException(nameof(nextId));

    /// <summary>Writes <paramref name="line"/> and returns the note it landed in.</summary>
    /// <param name="newNote">True to start a note of its own even when the day already has some.</param>
    public async ValueTask<NoteId> ExecuteAsync(
        LocalDate localDate,
        string line,
        bool newNote = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(line);
        string text = line.Trim();
        if (text.Length == 0)
        {
            throw new ArgumentException("There is nothing to add.", nameof(line));
        }

        DayWorkspace workspace = await repository
            .GetDayWorkspaceStateAsync(localDate, cancellationToken).ConfigureAwait(false);

        Note target;
        if (newNote || workspace.Notes.IsProjectionOnly)
        {
            // An invalid projection id asks for exactly one new note, appended after any there are.
            workspace = await repository
                .CreateNoteAsync(localDate, default, nextId(), cancellationToken).ConfigureAwait(false);
            target = workspace.Notes.Notes[^1];
        }
        else
        {
            target = workspace.Notes.Notes[^1];
        }

        NoteId id = target.Id!.Value;
        await repository.SaveNoteAsync(
            new NoteSaveRequest(
                id,
                localDate,
                target.Title,
                Join(target.Body, text),
                workspace.RevisionOf(id),
                IsNew: false,
                target.HasCustomTitle),
            cancellationToken).ConfigureAwait(false);
        return id;
    }

    /// <summary>The body with the line on a line of its own at the end.</summary>
    private static string Join(string body, string line) =>
        body.Length == 0 ? line
        : body.EndsWith('\n') ? body + line
        : body + "\n" + line;
}
