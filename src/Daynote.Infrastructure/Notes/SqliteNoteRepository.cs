using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Daynote.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Notes;

public sealed class SqliteNoteRepository : INoteRepository
{
    private readonly SqliteDatabase _database;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly INoteWriteInterceptor? _interceptor;

    public SqliteNoteRepository(
        SqliteDatabase database,
        Func<DateTimeOffset>? utcNow = null,
        INoteWriteInterceptor? interceptor = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _interceptor = interceptor;
    }

    public ValueTask<NoteSet> GetDayWorkspaceAsync(
        LocalDate localDate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = _database.OpenReadConnection();
        return ValueTask.FromResult(SqliteNoteStatements.LoadWorkspace(connection, null, localDate));
    }

    public ValueTask<DayWorkspace> GetDayWorkspaceStateAsync(
        LocalDate localDate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = _database.OpenReadConnection();
        return ValueTask.FromResult(SqliteNoteStatements.LoadWorkspaceState(connection, null, localDate));
    }

    public async ValueTask<DayWorkspace> CreateNoteAsync(
        LocalDate localDate,
        NoteId projectionId,
        NoteId newNoteId,
        CancellationToken cancellationToken = default)
    {
        EnsureId(newNoteId);
        try
        {
            return await _database.WriteAsync(
                (connection, transaction, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    _interceptor?.BeforeWrite(NoteWriteOperation.Create);
                    List<StoredNote> rows = SqliteNoteStatements.ReadRows(connection, transaction, localDate);
                    string utc = FormatUtc(_utcNow());

                    // On an empty day a VALID projection id first materializes the virtual "Note 1" as a real
                    // row, then appends the requested note (Note 1 + Note 2 — the two-note primitive used by
                    // seeding/tests). An INVALID projection id means the caller wants a SINGLE real "Note 1"
                    // (the app's + button on an empty day), so no projection row is inserted and the new note
                    // takes order 0 and the title "Note 1".
                    bool materializeProjection = rows.Count == 0 && projectionId.IsValid;
                    if (materializeProjection)
                    {
                        if (projectionId == newNoteId) throw new ArgumentException("Note IDs must differ.", nameof(newNoteId));
                        SqliteNoteStatements.Insert(connection, transaction, projectionId, localDate, "Note 1", string.Empty, 0, utc);
                        SqliteNoteStatements.UpsertSearch(connection, transaction, projectionId, localDate, "Note 1", string.Empty);
                    }

                    int order = materializeProjection ? 1 : rows.Count;
                    SqliteNoteStatements.Insert(connection, transaction, newNoteId, localDate, $"Note {order + 1}", string.Empty, order, utc);
                    SqliteNoteStatements.UpsertSearch(connection, transaction, newNoteId, localDate, $"Note {order + 1}", string.Empty);
                    return SqliteNoteStatements.LoadWorkspaceState(connection, transaction, localDate);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
            throw new RecoverableNoteException(NoteFailureCode.StorageUnavailable);
        }
    }

    public async ValueTask<DayWorkspace> ReorderNotesAsync(
        LocalDate localDate,
        IReadOnlyList<NoteId> orderedIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);
        try
        {
            return await _database.WriteAsync(
                (connection, transaction, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    _interceptor?.BeforeWrite(NoteWriteOperation.Reorder);
                    List<StoredNote> rows = SqliteNoteStatements.ReadRows(connection, transaction, localDate);
                    SqliteNoteStatements.ValidateOrder(rows, orderedIds);
                    SqliteNoteStatements.ApplyOrder(connection, transaction, localDate, rows, orderedIds, FormatUtc(_utcNow()));
                    return SqliteNoteStatements.LoadWorkspaceState(connection, transaction, localDate);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
            throw new RecoverableNoteException(NoteFailureCode.StorageUnavailable);
        }
    }

    public async ValueTask<DayWorkspace> DeleteNoteAsync(
        LocalDate localDate,
        NoteId noteId,
        CancellationToken cancellationToken = default)
    {
        EnsureId(noteId);
        try
        {
            return await _database.WriteAsync(
                (connection, transaction, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    _interceptor?.BeforeWrite(NoteWriteOperation.Delete);
                    List<StoredNote> before = SqliteNoteStatements.ReadRows(connection, transaction, localDate);
                    if (!before.Any(row => row.Id == noteId)) throw new ArgumentException("The note does not exist.", nameof(noteId));
                    SqliteNoteStatements.Delete(connection, transaction, noteId, FormatUtc(_utcNow()));
                    StoredNote[] remaining = before.Where(row => row.Id != noteId).ToArray();
                    SqliteNoteStatements.ApplyOrder(
                        connection,
                        transaction,
                        localDate,
                        remaining,
                        remaining.Select(static row => row.Id).ToArray(),
                        FormatUtc(_utcNow()));
                    return SqliteNoteStatements.LoadWorkspaceState(connection, transaction, localDate);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
            throw new RecoverableNoteException(NoteFailureCode.StorageUnavailable);
        }
    }

    public async ValueTask<NoteSaveReceipt> SaveNoteAsync(
        NoteSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Validate();
        if (request.IsNew && request.Body.Length == 0 &&
            string.Equals(request.Title, "Note 1", StringComparison.Ordinal))
        {
            return new NoteSaveReceipt(0, IsPersisted: false);
        }

        try
        {
            return await _database.WriteAsync(
                (connection, transaction, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    _interceptor?.BeforeWrite(NoteWriteOperation.Save);
                    string utc = FormatUtc(_utcNow());
                    int revision = request.IsNew
                        ? SqliteNoteStatements.InsertFirstEdit(connection, transaction, request, utc)
                        : SqliteNoteStatements.UpdateCas(connection, transaction, request, utc);
                    SqliteNoteStatements.SetCustomTitle(connection, transaction, request.Id, request.HasCustomTitle, utc);
                    _interceptor?.AfterSourceWrite(NoteWriteOperation.Save);
                    SqliteNoteStatements.UpsertSearch(connection, transaction, request.Id, request.LocalDate, request.Title, request.Body);
                    return new NoteSaveReceipt(revision);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
            throw new RecoverableNoteException(NoteFailureCode.StorageUnavailable);
        }
    }

    public async ValueTask<DayWorkspace> ToggleFavoriteAsync(
        LocalDate localDate,
        NoteId noteId,
        CancellationToken cancellationToken = default)
    {
        EnsureId(noteId);
        try
        {
            return await _database.WriteAsync(
                (connection, transaction, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    _interceptor?.BeforeWrite(NoteWriteOperation.Save);
                    if (!SqliteNoteStatements.ReadRows(connection, transaction, localDate).Any(row => row.Id == noteId))
                    {
                        throw new ArgumentException("The note does not exist.", nameof(noteId));
                    }

                    SqliteNoteStatements.ToggleFavorite(connection, transaction, noteId, FormatUtc(_utcNow()));
                    return SqliteNoteStatements.LoadWorkspaceState(connection, transaction, localDate);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
            throw new RecoverableNoteException(NoteFailureCode.StorageUnavailable);
        }
    }

    public async ValueTask<DayWorkspace> SetTagsAsync(
        LocalDate localDate,
        NoteId noteId,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken = default)
    {
        EnsureId(noteId);
        ArgumentNullException.ThrowIfNull(tags);
        try
        {
            return await _database.WriteAsync(
                (connection, transaction, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    _interceptor?.BeforeWrite(NoteWriteOperation.Save);
                    StoredNote note = SqliteNoteStatements.ReadRows(connection, transaction, localDate)
                        .FirstOrDefault(row => row.Id == noteId);
                    if (note.Id != noteId)
                    {
                        throw new ArgumentException("The note does not exist.", nameof(noteId));
                    }

                    SqliteNoteStatements.ReplaceTags(connection, transaction, noteId, tags, FormatUtc(_utcNow()));
                    SqliteNoteStatements.UpsertSearch(connection, transaction, noteId, localDate, note.Title, note.Body);
                    return SqliteNoteStatements.LoadWorkspaceState(connection, transaction, localDate);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
            throw new RecoverableNoteException(NoteFailureCode.StorageUnavailable);
        }
    }

    public ValueTask<IReadOnlyList<DateContentSummary>> GetMonthContentSummaryAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startDate = new DateOnly(year, month, 1);
        var endDate = startDate.AddMonths(1);
        using SqliteConnection connection = _database.OpenReadConnection();
        return ValueTask.FromResult<IReadOnlyList<DateContentSummary>>(
            SqliteNoteStatements.ReadMonthSummary(
                connection,
                startDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                endDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
    }

    public ValueTask<IReadOnlyList<NoteSummary>> GetAllNotesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = _database.OpenReadConnection();
        return ValueTask.FromResult<IReadOnlyList<NoteSummary>>(
            SqliteNoteStatements.ReadAllNotes(connection, null, null));
    }

    public ValueTask<IReadOnlyList<NoteSummary>> GetAllNotesAsync(
        LocalDate from,
        LocalDate to,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = _database.OpenReadConnection();
        return ValueTask.FromResult<IReadOnlyList<NoteSummary>>(
            SqliteNoteStatements.ReadAllNotes(connection, from.ToString(), to.ToString()));
    }

    private static void EnsureId(NoteId id)
    {
        if (!id.IsValid) throw new ArgumentException("A note ID is required.", nameof(id));
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
