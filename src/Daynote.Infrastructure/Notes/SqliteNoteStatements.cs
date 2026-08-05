using System.Text;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Notes;

internal readonly record struct StoredNote(
    NoteId Id,
    string Title,
    string Body,
    int SortOrder,
    int Revision,
    bool HasCustomTitle,
    bool IsFavorite,
    IReadOnlyList<string> Tags);

internal static class SqliteNoteStatements
{
    public static List<StoredNote> ReadRows(SqliteConnection connection, SqliteTransaction? transaction, LocalDate date)
    {
        Dictionary<NoteId, List<string>> tagsByNote = ReadTags(connection, transaction, date);
        using SqliteCommand command = Create(connection, transaction,
            "SELECT id,title,body,sort_order,revision,is_favorite,EXISTS(SELECT 1 FROM settings WHERE key='note.custom-title.' || notes.id) FROM notes WHERE local_date=$date ORDER BY sort_order;");
        command.Parameters.AddWithValue("$date", date.ToString());
        using SqliteDataReader reader = command.ExecuteReader();
        var rows = new List<StoredNote>();
        while (reader.Read())
        {
            NoteId id = NoteId.Create(Guid.Parse(reader.GetString(0))).Value;
            rows.Add(new StoredNote(
                id,
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(6) != 0,
                reader.GetInt32(5) != 0,
                tagsByNote.TryGetValue(id, out List<string>? tags) ? tags : []));
        }

        return rows;
    }

    public static NoteSet LoadWorkspace(SqliteConnection connection, SqliteTransaction? transaction, LocalDate date)
    {
        List<StoredNote> rows = ReadRows(connection, transaction, date);
        IEnumerable<Note> notes = rows.Select(row => Note.CreatePersisted(
            row.Id,
            date,
            row.SortOrder,
            row.HasCustomTitle ? row.Title : null,
            row.Body,
            row.IsFavorite,
            row.Tags).Value);
        return NoteSet.Restore(date, notes).Value;
    }

    public static DayWorkspace LoadWorkspaceState(SqliteConnection connection, SqliteTransaction? transaction, LocalDate date)
    {
        List<StoredNote> rows = ReadRows(connection, transaction, date);
        NoteSet notes = NoteSet.Restore(
            date,
            rows.Select(row => Note.CreatePersisted(
                row.Id,
                date,
                row.SortOrder,
                row.HasCustomTitle ? row.Title : null,
                row.Body,
                row.IsFavorite,
                row.Tags).Value)).Value;
        return new DayWorkspace(notes, rows.Select(static row => KeyValuePair.Create(row.Id, row.Revision)));
    }

    private static Dictionary<NoteId, List<string>> ReadTags(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        LocalDate date)
    {
        using SqliteCommand command = Create(connection, transaction,
            "SELECT nt.note_id,nt.tag FROM note_tags nt JOIN notes n ON n.id=nt.note_id WHERE n.local_date=$date ORDER BY nt.note_id,nt.sort_order;");
        command.Parameters.AddWithValue("$date", date.ToString());
        using SqliteDataReader reader = command.ExecuteReader();
        var result = new Dictionary<NoteId, List<string>>();
        while (reader.Read())
        {
            NoteId id = NoteId.Create(Guid.Parse(reader.GetString(0))).Value;
            if (!result.TryGetValue(id, out List<string>? tags))
            {
                tags = [];
                result[id] = tags;
            }

            tags.Add(reader.GetString(1));
        }

        return result;
    }

    private static List<string> ReadTagsFor(SqliteConnection connection, SqliteTransaction? transaction, NoteId id)
    {
        using SqliteCommand command = Create(connection, transaction,
            "SELECT tag FROM note_tags WHERE note_id=$id ORDER BY sort_order;");
        command.Parameters.AddWithValue("$id", id.ToString());
        using SqliteDataReader reader = command.ExecuteReader();
        var tags = new List<string>();
        while (reader.Read())
        {
            tags.Add(reader.GetString(0));
        }

        return tags;
    }

    public static void Insert(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NoteId id,
        LocalDate date,
        string title,
        string body,
        int order,
        string utc)
    {
        using SqliteCommand command = Create(connection, transaction,
            "INSERT INTO notes(id,local_date,title,body,sort_order,revision,created_utc,updated_utc) VALUES($id,$date,$title,$body,$order,0,$utc,$utc);");
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$date", date.ToString());
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$body", body);
        command.Parameters.AddWithValue("$order", order);
        command.Parameters.AddWithValue("$utc", utc);
        command.ExecuteNonQuery();
    }

    public static int InsertFirstEdit(SqliteConnection connection, SqliteTransaction transaction, NoteSaveRequest request, string utc)
    {
        using (SqliteCommand count = Create(connection, transaction, "SELECT COUNT(*) FROM notes WHERE local_date=$date;"))
        {
            count.Parameters.AddWithValue("$date", request.LocalDate.ToString());
            if (Convert.ToInt32(count.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != 0)
                throw new RecoverableNoteException(NoteFailureCode.RevisionConflict);
        }

        Insert(connection, transaction, request.Id, request.LocalDate, request.Title, request.Body, 0, utc);
        return 0;
    }

    public static int UpdateCas(SqliteConnection connection, SqliteTransaction transaction, NoteSaveRequest request, string utc)
    {
        using SqliteCommand command = Create(connection, transaction,
            "UPDATE notes SET title=$title,body=$body,revision=revision+1,updated_utc=$utc WHERE id=$id AND local_date=$date AND revision=$revision;");
        command.Parameters.AddWithValue("$title", request.Title);
        command.Parameters.AddWithValue("$body", request.Body);
        command.Parameters.AddWithValue("$utc", utc);
        command.Parameters.AddWithValue("$id", request.Id.ToString());
        command.Parameters.AddWithValue("$date", request.LocalDate.ToString());
        command.Parameters.AddWithValue("$revision", request.Revision);
        if (command.ExecuteNonQuery() != 1) throw new RecoverableNoteException(NoteFailureCode.RevisionConflict);
        return checked(request.Revision + 1);
    }

    public static void Delete(SqliteConnection connection, SqliteTransaction transaction, NoteId id)
    {
        using SqliteCommand search = Create(connection, transaction, "DELETE FROM search_documents WHERE source_type='note' AND source_id=$id;");
        search.Parameters.AddWithValue("$id", id.ToString());
        search.ExecuteNonQuery();
        SetCustomTitle(connection, transaction, id, hasCustomTitle: false, utc: string.Empty);
        using SqliteCommand note = Create(connection, transaction, "DELETE FROM notes WHERE id=$id;");
        note.Parameters.AddWithValue("$id", id.ToString());
        note.ExecuteNonQuery();
    }

    public static void ValidateOrder(IReadOnlyList<StoredNote> rows, IReadOnlyList<NoteId> orderedIds)
    {
        if (rows.Count == 0 || rows.Count != orderedIds.Count ||
            orderedIds.Distinct().Count() != orderedIds.Count ||
            !rows.Select(static row => row.Id).ToHashSet().SetEquals(orderedIds))
            throw new ArgumentException("The order must contain every note ID exactly once.", nameof(orderedIds));
    }

    public static void ApplyOrder(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LocalDate date,
        IReadOnlyList<StoredNote> rows,
        IReadOnlyList<NoteId> orderedIds,
        string utc)
    {
        if (rows.Count == 0) return;
        using (SqliteCommand offset = Create(connection, transaction, "UPDATE notes SET sort_order=sort_order+1000000 WHERE local_date=$date;"))
        {
            offset.Parameters.AddWithValue("$date", date.ToString());
            offset.ExecuteNonQuery();
        }

        Dictionary<NoteId, StoredNote> byId = rows.ToDictionary(static row => row.Id);
        for (int order = 0; order < orderedIds.Count; order++)
        {
            StoredNote row = byId[orderedIds[order]];
            string title = row.HasCustomTitle ? row.Title : $"Note {order + 1}";
            using SqliteCommand update = Create(connection, transaction,
                "UPDATE notes SET sort_order=$order,title=$title,revision=revision+1,updated_utc=$utc WHERE id=$id;");
            update.Parameters.AddWithValue("$order", order);
            update.Parameters.AddWithValue("$title", title);
            update.Parameters.AddWithValue("$utc", utc);
            update.Parameters.AddWithValue("$id", row.Id.ToString());
            update.ExecuteNonQuery();
            UpsertSearch(connection, transaction, row.Id, date, title, row.Body);
        }
    }

    public static void UpsertSearch(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NoteId id,
        LocalDate date,
        string title,
        string body)
    {
        // Tags are indexed by folding them into body_folded only. The displayed body column stays the
        // pure note text so search snippets never surface tag noise, while both the trigram FTS column
        // and the LIKE substring column (both read from body_folded) still match tag queries. The FTS
        // schema and its triggers are unchanged, preserving the source/index atomicity contract.
        IReadOnlyList<string> tags = ReadTagsFor(connection, transaction, id);
        string normalizedTitle = title.Normalize(NormalizationForm.FormC);
        string normalizedBody = body.Normalize(NormalizationForm.FormC);
        string bodyForFold = tags.Count == 0
            ? normalizedBody
            : normalizedBody + "\n" + string.Join(' ', tags);
        using SqliteCommand command = Create(connection, transaction,
            "INSERT INTO search_documents(source_type,source_id,local_date,title,body,title_folded,body_folded) VALUES('note',$id,$date,$title,$body,$titleFolded,$bodyFolded) ON CONFLICT(source_type,source_id) DO UPDATE SET local_date=excluded.local_date,title=excluded.title,body=excluded.body,title_folded=excluded.title_folded,body_folded=excluded.body_folded;");
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$date", date.ToString());
        command.Parameters.AddWithValue("$title", normalizedTitle);
        command.Parameters.AddWithValue("$body", normalizedBody);
        command.Parameters.AddWithValue("$titleFolded", normalizedTitle.ToUpperInvariant().Normalize(NormalizationForm.FormC));
        command.Parameters.AddWithValue("$bodyFolded", bodyForFold.ToUpperInvariant().Normalize(NormalizationForm.FormC));
        command.ExecuteNonQuery();
    }

    public static bool ToggleFavorite(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NoteId id,
        string utc)
    {
        using SqliteCommand command = Create(connection, transaction,
            "UPDATE notes SET is_favorite=1-is_favorite,updated_utc=$utc WHERE id=$id RETURNING is_favorite;");
        command.Parameters.AddWithValue("$utc", utc);
        command.Parameters.AddWithValue("$id", id.ToString());
        object? result = command.ExecuteScalar();
        if (result is null)
        {
            throw new ArgumentException("The note does not exist.", nameof(id));
        }

        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) != 0;
    }

    public static void ReplaceTags(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NoteId id,
        IReadOnlyList<string> tags,
        string utc)
    {
        using (SqliteCommand touch = Create(connection, transaction,
            "UPDATE notes SET updated_utc=$utc WHERE id=$id;"))
        {
            touch.Parameters.AddWithValue("$utc", utc);
            touch.Parameters.AddWithValue("$id", id.ToString());
            if (touch.ExecuteNonQuery() != 1)
            {
                throw new ArgumentException("The note does not exist.", nameof(id));
            }
        }

        using (SqliteCommand clear = Create(connection, transaction, "DELETE FROM note_tags WHERE note_id=$id;"))
        {
            clear.Parameters.AddWithValue("$id", id.ToString());
            clear.ExecuteNonQuery();
        }

        for (int order = 0; order < tags.Count; order++)
        {
            using SqliteCommand insert = Create(connection, transaction,
                "INSERT INTO note_tags(note_id,tag,sort_order) VALUES($id,$tag,$order);");
            insert.Parameters.AddWithValue("$id", id.ToString());
            insert.Parameters.AddWithValue("$tag", tags[order]);
            insert.Parameters.AddWithValue("$order", order);
            insert.ExecuteNonQuery();
        }
    }

    public static List<DateContentSummary> ReadMonthSummary(
        SqliteConnection connection,
        string startInclusive,
        string endExclusive)
    {
        // One read-only pass: tag each source row with a marker column, then GROUP BY date so
        // SUM(is_note) is the note count and MAX(is_clip)/MAX(is_file) flag presence. Dates with no
        // content simply produce no group and are absent from the result.
        using SqliteCommand command = Create(connection, null,
            "SELECT local_date,SUM(is_note),MAX(is_clip),MAX(is_file) FROM (" +
            "SELECT local_date,1 AS is_note,0 AS is_clip,0 AS is_file FROM notes " +
            "UNION ALL SELECT local_date,0,1,0 FROM clipboard_items " +
            "UNION ALL SELECT local_date,0,0,1 FROM day_files) " +
            "WHERE local_date>=$start AND local_date<$end " +
            "GROUP BY local_date ORDER BY local_date;");
        command.Parameters.AddWithValue("$start", startInclusive);
        command.Parameters.AddWithValue("$end", endExclusive);
        using SqliteDataReader reader = command.ExecuteReader();
        var result = new List<DateContentSummary>();
        while (reader.Read())
        {
            result.Add(new DateContentSummary(
                LocalDate.Parse(reader.GetString(0)).Value,
                reader.GetInt32(1),
                reader.GetInt32(2) != 0,
                reader.GetInt32(3) != 0));
        }

        return result;
    }

    public static List<NoteSummary> ReadAllNotes(
        SqliteConnection connection,
        string? startInclusive,
        string? endInclusive)
    {
        bool bounded = startInclusive is not null;
        using SqliteCommand command = Create(connection, null,
            "SELECT id,local_date,title,body,sort_order,is_favorite," +
            "EXISTS(SELECT 1 FROM settings WHERE key='note.custom-title.' || notes.id) FROM notes" +
            (bounded ? " WHERE local_date>=$start AND local_date<=$end" : string.Empty) +
            " ORDER BY local_date DESC,sort_order;");
        if (bounded)
        {
            command.Parameters.AddWithValue("$start", startInclusive!);
            command.Parameters.AddWithValue("$end", endInclusive!);
        }

        using SqliteDataReader reader = command.ExecuteReader();
        var result = new List<NoteSummary>();
        while (reader.Read())
        {
            int sortOrder = reader.GetInt32(4);
            bool hasCustomTitle = reader.GetInt32(6) != 0;
            result.Add(new NoteSummary(
                Guid.Parse(reader.GetString(0)),
                LocalDate.Parse(reader.GetString(1)).Value,
                hasCustomTitle ? reader.GetString(2) : UntitledNote.TitleFor(sortOrder + 1),
                reader.GetString(3),
                sortOrder,
                reader.GetInt32(5) != 0));
        }

        return result;
    }

    public static void SetCustomTitle(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NoteId id,
        bool hasCustomTitle,
        string utc)
    {
        string key = $"note.custom-title.{id}";
        using SqliteCommand command = Create(connection, transaction, hasCustomTitle
            ? "INSERT INTO settings(key,value,updated_utc) VALUES($key,'1',$utc) ON CONFLICT(key) DO UPDATE SET value='1',updated_utc=excluded.updated_utc;"
            : "DELETE FROM settings WHERE key=$key;");
        command.Parameters.AddWithValue("$key", key);
        if (hasCustomTitle) command.Parameters.AddWithValue("$utc", utc);
        command.ExecuteNonQuery();
    }

    private static SqliteCommand Create(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }
}
