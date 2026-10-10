using System.Globalization;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Agenda;
using Daynote.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Portable.Tests.Agenda;

/// <summary>
/// What a delete and its undo leave for sync, against a real database (docs/TODOS.md §9).
/// </summary>
/// <remarks>
/// Sync orders a tombstone against an edit by time alone, here and on the Worker. These pin that a
/// delete leaves a tombstone, and that an undo is queued as a write newer than it — the restore
/// has to win on a device that already pushed the delete.
/// </remarks>
[TestClass]
public sealed class DeleteAgendaItemTests
{
    [TestMethod]
    public async Task Deleting_a_one_off_leaves_a_tombstone_and_undo_is_queued_newer_than_it()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        AgendaItem item = OneOff();
        await fixture.Agenda.SaveAsync(item);

        AgendaDeletion deletion = await fixture.Delete.DeleteAsync(new AgendaDayRow(item, null, item.DueAt));

        Assert.IsNull(await fixture.Agenda.GetAsync(item.Id));
        DateTimeOffset deleted = fixture.Tombstone(item.Id) ?? throw new AssertFailedException("No tombstone: the delete never syncs.");
        Assert.IsNull(fixture.Queued(item.Id), "A deleted item is still queued as a write.");

        await Task.Delay(5);
        await fixture.Delete.RestoreAsync(deletion);

        AgendaItem back = await fixture.Agenda.GetAsync(item.Id) ?? throw new AssertFailedException("Undo did not put it back.");
        Assert.AreEqual(item.Title, back.Title);
        Assert.IsNull(fixture.Tombstone(item.Id), "The tombstone outlived the undo and would be pushed.");
        Assert.IsGreaterThan(deleted, back.UpdatedUtc, "The restore is older than its tombstone and loses the merge.");
        Assert.AreEqual(back.UpdatedUtc, fixture.Queued(item.Id), "The restore is not queued.");
    }

    [TestMethod]
    public async Task Deleting_a_note_leaves_the_to_do_captured_from_it_untouched()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        LocalDate date = LocalDate.Parse("2026-10-07").Value;
        NoteId note = NoteId.Create(Guid.NewGuid()).Value;
        await fixture.Notes.SaveNoteAsync(new NoteSaveRequest(note, date, "회의", "-[ ] 자료 공유", 0, IsNew: true, HasCustomTitle: true));
        AgendaItem item = OneOff() with { SourceNoteId = note.Value };
        await fixture.Agenda.SaveAsync(item);

        await fixture.Notes.DeleteNoteAsync(date, note);

        // The body is just text and the to-do is its own thing: deleting one never takes the other.
        AgendaItem kept = await fixture.Agenda.GetAsync(item.Id) ?? throw new AssertFailedException("Deleting the note deleted its to-do.");
        Assert.AreEqual(item.Title, kept.Title);
        Assert.AreEqual(item.Status, kept.Status);
        Assert.IsNull(fixture.Tombstone(item.Id), "Deleting the note tombstoned its to-do.");
    }

    [TestMethod]
    public async Task Deleting_every_repeat_tombstones_the_series_and_each_override()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        AgendaItem series = Series();
        await fixture.Agenda.SaveAsync(series);
        var day = new DateOnly(2026, 10, 7);
        AgendaItem ticked = await new ToggleAgendaItem(fixture.Agenda).ToggleAsync(
            AgendaDay.For(day, await fixture.Agenda.GetAllAsync()).Open.Single());

        AgendaDeletion deletion = await fixture.Delete.DeleteAsync(
            AgendaDay.For(day.AddDays(1), await fixture.Agenda.GetAllAsync()).Open.Single(),
            AgendaRepeatScope.Series);

        Assert.IsEmpty(await fixture.Agenda.GetAllAsync());
        Assert.IsNotNull(fixture.Tombstone(series.Id));
        Assert.IsNotNull(fixture.Tombstone(ticked.Id), "The override has no tombstone and stays on the other device.");

        await Task.Delay(5);
        await fixture.Delete.RestoreAsync(deletion);

        Assert.HasCount(2, await fixture.Agenda.GetAllAsync());
        Assert.IsNull(fixture.Tombstone(series.Id));
        Assert.IsNull(fixture.Tombstone(ticked.Id));
    }

    [TestMethod]
    public async Task Deleting_one_occurrence_queues_the_rule_with_its_exdate()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        AgendaItem series = Series();
        await fixture.Agenda.SaveAsync(series);
        AgendaDayRow row = AgendaDay.For(new DateOnly(2026, 10, 7), [series]).Open.Single();

        await fixture.Delete.DeleteAsync(row, AgendaRepeatScope.Occurrence);

        AgendaItem rule = (await fixture.Agenda.GetAsync(series.Id))!;
        Assert.AreSequenceEqual(new[] { row.RecurrenceId!.Value }, rule.ExceptionDates.ToArray());
        Assert.AreEqual(rule.UpdatedUtc, fixture.Queued(series.Id), "The EXDATE is not queued.");
        Assert.IsNull(fixture.Tombstone(series.Id));
    }

    private static AgendaItem OneOff() => Item() with
    {
        DueAt = new WallClock(new DateTime(2026, 10, 7, 14, 0, 0)),
        HasDueTime = true,
    };

    private static AgendaItem Series() => Item() with
    {
        Rrule = "FREQ=DAILY",
        StartsAt = new WallClock(new DateTime(2026, 10, 1, 7, 0, 0)),
        DueAt = new WallClock(new DateTime(2026, 10, 1, 7, 0, 0)),
        HasDueTime = true,
    };

    private static AgendaItem Item() => new(
        Guid.NewGuid(),
        AgendaList.DefaultId,
        AgendaKind.Task,
        "스트레칭",
        string.Empty,
        "Asia/Seoul",
        StartsAt: null,
        EndsAt: null,
        DueAt: null,
        HasDueTime: false,
        Rrule: null,
        SeriesId: null,
        RecurrenceId: null,
        AgendaStatus.NeedsAction,
        CompletedUtc: null,
        Priority: 0,
        TimelineVisibility.Auto,
        SourceNoteId: null,
        ExceptionDates: [],
        AgendaAlert.Default,
        DateTimeOffset.UtcNow.AddDays(-1),
        DateTimeOffset.UtcNow.AddDays(-1));

    /// <summary>The real clock throughout: a tombstone reads SQLite's, and the two have to be comparable.</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TempDirectory directory;
        private readonly SqliteDatabase database;

        private Fixture(TempDirectory directory, SqliteDatabase database)
        {
            this.directory = directory;
            this.database = database;
            Agenda = new SqliteAgendaRepository(database, static () => DateTimeOffset.UtcNow);
            Delete = new DeleteAgendaItem(Agenda);
            Notes = new SqliteNoteRepository(database, static () => DateTimeOffset.UtcNow);
        }

        internal SqliteNoteRepository Notes { get; }

        internal SqliteAgendaRepository Agenda { get; }

        internal DeleteAgendaItem Delete { get; }

        internal static ValueTask<Fixture> CreateAsync()
        {
            var directory = new TempDirectory();
            var database = new SqliteDatabase(new SqliteDatabaseOptions(System.IO.Path.Combine(directory.Path, "daynote.db")));
            database.Initialize();
            return ValueTask.FromResult(new Fixture(directory, database));
        }

        internal DateTimeOffset? Tombstone(Guid id) =>
            Read("SELECT deleted_utc FROM sync_tombstones WHERE entity = 'agenda_item' AND entity_id = $id;", id);

        internal DateTimeOffset? Queued(Guid id) =>
            Read("SELECT queued_utc FROM sync_outbox WHERE entity = 'agenda_item' AND entity_id = $id;", id);

        private DateTimeOffset? Read(string sql, Guid id)
        {
            using SqliteConnection connection = database.OpenReadConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$id", id.ToString());
            return command.ExecuteScalar() is string text
                ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture)
                : null;
        }

        public async ValueTask DisposeAsync()
        {
            await database.DisposeAsync();
            directory.Dispose();
        }
    }
}
