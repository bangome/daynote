using System.Globalization;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Infrastructure.Agenda;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Persistence.Migrations;

/// <summary>
/// The one-time walk that turns every <c>-[ ]</c> line in a note body into a to-do
/// (docs/TODOS.md §8).
/// </summary>
/// <remarks>
/// <b>Not registered yet.</b> <see cref="MigrationRunner.FromEmbeddedResources"/> does not include
/// it, so no database runs it. §12 is explicit: until desktop and phone switch their readers in the
/// same release, a device that migrated would hold entities while its phone still parsed the same
/// lines out of the body, and every task would exist — and remind — twice. Step 3 adds it to the
/// set; that is the whole change.
/// <para>
/// <b>The body is left exactly as written.</b> Deleting the lines would be the app rewriting the
/// user's own prose, and §8 refuses that. Afterwards a <c>-[ ]</c> in a body is text that looks
/// like a checkbox, and nothing reads it as a to-do.
/// </para>
/// <para>
/// <b>Ids are derived from the note and the line</b>, never random — see
/// <see cref="TodoBodyScan"/>. Two devices run this independently and offline, and random ids
/// would hand the user every task twice after the next sync.
/// </para>
/// <para>
/// <b>Timestamps come from the note, not the clock.</b> The note's <c>updated_utc</c> is both
/// deterministic across devices — so the two migrations do not flap against last-write-wins — and
/// the most honest evidence there is of when the line was last touched.
/// </para>
/// </remarks>
public static class TodoCaptureMigration
{
    public const int Version = 7;

    public const string Name = "todo_capture";

    /// <summary>
    /// The step, ready to be added to the runner's set. It is deliberately not in
    /// <see cref="MigrationRunner.FromEmbeddedResources"/>; the tests construct a runner that
    /// includes it.
    /// </summary>
    public static SqliteMigration Migration => new(Version, Name, Apply, backupFirst: true);

    internal static void Apply(SqliteConnection connection, SqliteTransaction transaction)
    {
        string zone = AgendaZone.Local();

        foreach (SourceNote note in ReadNotes(connection, transaction))
        {
            foreach (ScannedTodo todo in TodoBodyScan.Scan(note.Id, note.Date, note.Body))
            {
                // A row already under this id is one the user has since edited, or the same line
                // arriving from another device that migrated first. Either way it is newer than
                // what this walk would write, and overwriting it would undo real work.
                if (SqliteAgendaStatements.ReadUpdatedUtc(
                        connection, transaction, "agenda_items", todo.Id) is not null)
                {
                    continue;
                }

                SqliteAgendaStatements.Save(connection, transaction, ToItem(note, todo, zone));
            }
        }
    }

    private static AgendaItem ToItem(SourceNote note, ScannedTodo todo, string zone) => new(
        todo.Id,
        AgendaList.DefaultId,
        AgendaKind.Task,
        todo.Text,
        string.Empty,
        zone,
        // The note's own date, not today. These are historical lines, and giving every one of them
        // a start of today would empty years of notes onto this morning's panel. A due stamp still
        // wins where there is one: the day panel reads COALESCE(due_at, starts_at).
        StartsAt: new WallClock(new DateTime(note.Date.Year, note.Date.Month, note.Date.Day, 0, 0, 0)),
        EndsAt: null,
        DueAt: todo.DueAt,
        HasDueTime: todo.HasDueTime,
        Rrule: null,
        SeriesId: null,
        RecurrenceId: null,
        todo.Completed ? AgendaStatus.Completed : AgendaStatus.NeedsAction,
        // Nothing recorded when it was ticked. The note's last edit is the closest thing to a fact
        // available, and it is at least never in the future.
        CompletedUtc: todo.Completed ? note.UpdatedUtc : null,
        Priority: 0,
        TimelineVisibility.Auto,
        // What `source_note_id` is for: a one-way jump back to where the line was written. Allowed
        // to dangle, so deleting the note later does not take the task with it.
        SourceNoteId: note.Id,
        ExceptionDates: [],
        // Exactly what this line did as text: a checkbox with a `(M/D)` stamp reminded, one
        // without never did. Writing the default alert onto every migrated row instead would
        // start pinging people about undated checkboxes they have had sitting in today's note for
        // months — the migration is not the place to change what the app does.
        AlarmLeadMinutes: todo.DueAt is null ? AgendaAlert.None : AgendaAlert.Default,
        note.UpdatedUtc,
        note.UpdatedUtc);

    private static List<SourceNote> ReadNotes(SqliteConnection connection, SqliteTransaction transaction)
    {
        // Read whole rather than streamed: the walk writes to agenda_items on the same connection,
        // and holding a reader open across those writes is what deadlocks a single-connection
        // SQLite transaction. A note body is small and the count is in the thousands at worst.
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id, local_date, body, updated_utc FROM notes ORDER BY id;";

        var notes = new List<SourceNote>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            DomainResult<LocalDate> date = LocalDate.Parse(reader.GetString(1));
            if (!Guid.TryParse(reader.GetString(0), out Guid id) || !date.IsSuccess)
            {
                continue;
            }

            notes.Add(new SourceNote(
                id,
                date.Value,
                reader.GetString(2),
                DateTimeOffset.Parse(
                    reader.GetString(3),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind)));
        }

        return notes;
    }

    private readonly record struct SourceNote(
        Guid Id,
        LocalDate Date,
        string Body,
        DateTimeOffset UpdatedUtc);
}
