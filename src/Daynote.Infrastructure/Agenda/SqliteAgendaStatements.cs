using System.Globalization;
using Daynote.Core.Agenda;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Agenda;

/// <summary>The SQL behind <see cref="SqliteAgendaRepository"/>, kept out of it as the Files pair does.</summary>
internal static class SqliteAgendaStatements
{
    private const string ItemColumns =
        "SELECT id,list_id,kind,title,description,tz,starts_at,ends_at,due_at,has_due_time," +
        "rrule,series_id,recurrence_id,status,completed_utc,priority,timeline_visibility," +
        "source_note_id,created_utc,updated_utc FROM agenda_items";

    private const string ListColumns =
        "SELECT id,name,sort_order,is_default,created_utc,updated_utc FROM agenda_lists";

    // ── Lists ────────────────────────────────────────────────────────────────────────────────

    public static List<AgendaList> ReadLists(SqliteConnection connection)
    {
        using SqliteCommand command = Create(
            connection, null, ListColumns + " ORDER BY is_default DESC,sort_order,name;");
        using SqliteDataReader reader = command.ExecuteReader();
        var lists = new List<AgendaList>();
        while (reader.Read())
        {
            lists.Add(ReadList(reader));
        }

        return lists;
    }

    public static AgendaList InsertList(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        string name,
        string nowUtc)
    {
        // Appended rather than inserted: a new list goes to the end of whatever order the user has.
        using SqliteCommand next = Create(
            connection, transaction, "SELECT COALESCE(MAX(sort_order),0)+1 FROM agenda_lists;");
        int order = Convert.ToInt32(next.ExecuteScalar(), CultureInfo.InvariantCulture);

        using SqliteCommand command = Create(
            connection,
            transaction,
            "INSERT INTO agenda_lists(id,name,sort_order,is_default,created_utc,updated_utc) " +
            "VALUES ($id,$name,$order,0,$now,$now);");
        command.Parameters.AddWithValue("$id", Format(id));
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$order", order);
        command.Parameters.AddWithValue("$now", nowUtc);
        command.ExecuteNonQuery();

        return new AgendaList(id, name, order, false, ParseUtc(nowUtc), ParseUtc(nowUtc));
    }

    public static AgendaList? RenameList(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        string name,
        string nowUtc)
    {
        using SqliteCommand command = Create(
            connection,
            transaction,
            "UPDATE agenda_lists SET name=$name,updated_utc=$now WHERE id=$id;");
        command.Parameters.AddWithValue("$id", Format(id));
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$now", nowUtc);
        return command.ExecuteNonQuery() == 0 ? null : ReadList(connection, transaction, id);
    }

    /// <summary>
    /// Moves the list's items to the default and removes it. The default itself is refused: it is
    /// where everything else lands, so there would be nowhere to put its contents.
    /// </summary>
    public static int? DeleteList(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        string nowUtc)
    {
        if (id == AgendaList.DefaultId)
        {
            return null;
        }

        using SqliteCommand move = Create(
            connection,
            transaction,
            "UPDATE agenda_items SET list_id=$default,updated_utc=$now WHERE list_id=$id;");
        move.Parameters.AddWithValue("$default", Format(AgendaList.DefaultId));
        move.Parameters.AddWithValue("$id", Format(id));
        move.Parameters.AddWithValue("$now", nowUtc);
        int moved = move.ExecuteNonQuery();

        using SqliteCommand remove = Create(
            connection, transaction, "DELETE FROM agenda_lists WHERE id=$id;");
        remove.Parameters.AddWithValue("$id", Format(id));
        return remove.ExecuteNonQuery() == 0 ? null : moved;
    }

    // ── Items ────────────────────────────────────────────────────────────────────────────────

    public static AgendaItem? ReadItem(SqliteConnection connection, Guid id)
    {
        using SqliteCommand command = Create(connection, null, ItemColumns + " WHERE id=$id;");
        command.Parameters.AddWithValue("$id", Format(id));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Hydrate(connection, ReadRow(reader)) : null;
    }

    /// <summary>
    /// What is anchored to one date. A series is anchored to the day its rule starts, so the
    /// caller expanding rules over a window does not see it twice.
    /// </summary>
    public static List<AgendaItem> ReadForDate(SqliteConnection connection, DateOnly date)
    {
        string prefix = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T";
        using SqliteCommand command = Create(
            connection,
            null,
            ItemColumns + " WHERE substr(COALESCE(due_at,starts_at),1,11)=$prefix " +
            "ORDER BY COALESCE(due_at,starts_at),title;");
        command.Parameters.AddWithValue("$prefix", prefix);
        return HydrateAll(connection, command);
    }

    public static List<AgendaItem> ReadAll(SqliteConnection connection)
    {
        using SqliteCommand command = Create(
            connection, null, ItemColumns + " ORDER BY COALESCE(due_at,starts_at),title;");
        return HydrateAll(connection, command);
    }

    public static List<AgendaItem> ReadSeries(SqliteConnection connection)
    {
        using SqliteCommand command = Create(
            connection, null, ItemColumns + " WHERE rrule IS NOT NULL ORDER BY title;");
        return HydrateAll(connection, command);
    }

    public static List<AgendaItem> ReadForList(SqliteConnection connection, Guid listId)
    {
        using SqliteCommand command = Create(
            connection,
            null,
            ItemColumns + " WHERE list_id=$list ORDER BY COALESCE(due_at,starts_at),title;");
        command.Parameters.AddWithValue("$list", Format(listId));
        return HydrateAll(connection, command);
    }

    /// <summary>
    /// Writes the row and replaces its exception dates and alarms. Replacing rather than diffing:
    /// both sets are tiny, and a diff needs a second code path that the first one will outgrow.
    /// </summary>
    public static void Save(SqliteConnection connection, SqliteTransaction transaction, AgendaItem item)
    {
        using (SqliteCommand command = Create(
            connection,
            transaction,
            "INSERT INTO agenda_items(id,list_id,kind,title,description,tz,starts_at,ends_at,due_at," +
            "has_due_time,rrule,series_id,recurrence_id,status,completed_utc,priority," +
            "timeline_visibility,source_note_id,created_utc,updated_utc) " +
            "VALUES ($id,$list,$kind,$title,$description,$tz,$starts,$ends,$due,$hasDueTime,$rrule," +
            "$series,$recurrence,$status,$completed,$priority,$visibility,$note,$created,$updated) " +
            "ON CONFLICT(id) DO UPDATE SET list_id=excluded.list_id,kind=excluded.kind," +
            "title=excluded.title,description=excluded.description,tz=excluded.tz," +
            "starts_at=excluded.starts_at,ends_at=excluded.ends_at,due_at=excluded.due_at," +
            "has_due_time=excluded.has_due_time,rrule=excluded.rrule,series_id=excluded.series_id," +
            "recurrence_id=excluded.recurrence_id,status=excluded.status," +
            "completed_utc=excluded.completed_utc,priority=excluded.priority," +
            "timeline_visibility=excluded.timeline_visibility,source_note_id=excluded.source_note_id," +
            "updated_utc=excluded.updated_utc;"))
        {
            command.Parameters.AddWithValue("$id", Format(item.Id));
            command.Parameters.AddWithValue("$list", Format(item.ListId));
            command.Parameters.AddWithValue("$kind", item.Kind == AgendaKind.Event ? "event" : "task");
            command.Parameters.AddWithValue("$title", item.Title);
            command.Parameters.AddWithValue("$description", item.Description);
            command.Parameters.AddWithValue("$tz", item.Zone);
            AddNullable(command, "$starts", item.StartsAt?.ToString());
            AddNullable(command, "$ends", item.EndsAt?.ToString());
            AddNullable(command, "$due", item.DueAt?.ToString());
            command.Parameters.AddWithValue("$hasDueTime", item.HasDueTime ? 1 : 0);
            AddNullable(command, "$rrule", item.Rrule);
            AddNullable(command, "$series", item.SeriesId is { } series ? Format(series) : null);
            AddNullable(command, "$recurrence", item.RecurrenceId?.ToString());
            command.Parameters.AddWithValue("$status", StatusText(item.Status));
            AddNullable(command, "$completed", item.CompletedUtc is { } done ? FormatUtc(done) : null);
            command.Parameters.AddWithValue("$priority", item.Priority);
            command.Parameters.AddWithValue("$visibility", VisibilityText(item.TimelineVisibility));
            AddNullable(command, "$note", item.SourceNoteId is { } note ? Format(note) : null);
            command.Parameters.AddWithValue("$created", FormatUtc(item.CreatedUtc));
            command.Parameters.AddWithValue("$updated", FormatUtc(item.UpdatedUtc));
            command.ExecuteNonQuery();
        }

        ReplaceChildren(connection, transaction, "agenda_exdates", "occurrence", item.Id,
            item.ExceptionDates.Select(static date => (object)date.ToString()));
        ReplaceChildren(connection, transaction, "agenda_alarms", "lead_minutes", item.Id,
            item.AlarmLeadMinutes.Select(static lead => (object)lead));
    }

    public static bool Delete(SqliteConnection connection, SqliteTransaction transaction, Guid id)
    {
        // Overrides cascade through series_id, exdates and alarms through item_id.
        using SqliteCommand command = Create(
            connection, transaction, "DELETE FROM agenda_items WHERE id=$id;");
        command.Parameters.AddWithValue("$id", Format(id));
        return command.ExecuteNonQuery() > 0;
    }

    // ── Merge ────────────────────────────────────────────────────────────────────────────────
    //
    // What cloud sync needs on top of the repository: last-write-wins needs to read one timestamp
    // without hydrating a row, and a list arriving from another device is an upsert rather than
    // the create/rename pair the UI offers.

    public static DateTimeOffset? ReadUpdatedUtc(
        SqliteConnection connection, SqliteTransaction? transaction, string table, Guid id)
    {
        using SqliteCommand command = Create(
            connection, transaction, $"SELECT updated_utc FROM {table} WHERE id=$id;");
        command.Parameters.AddWithValue("$id", Format(id));
        return command.ExecuteScalar() is string value ? ParseUtc(value) : null;
    }

    /// <summary>
    /// Writes a list as it arrived from another device. <c>is_default</c> is derived from the id
    /// and the incoming flag is ignored: the column is unique, so honouring a remote claim would
    /// make applying a page depend on the order its rows happen to arrive in — and fail outright
    /// when two of them claim it.
    /// </summary>
    public static void SaveList(
        SqliteConnection connection, SqliteTransaction transaction, AgendaList list)
    {
        using SqliteCommand command = Create(
            connection,
            transaction,
            "INSERT INTO agenda_lists(id,name,sort_order,is_default,created_utc,updated_utc) " +
            "VALUES ($id,$name,$order,$default,$created,$updated) " +
            "ON CONFLICT(id) DO UPDATE SET name=excluded.name,sort_order=excluded.sort_order," +
            "updated_utc=excluded.updated_utc;");
        command.Parameters.AddWithValue("$id", Format(list.Id));
        command.Parameters.AddWithValue("$name", list.Name);
        command.Parameters.AddWithValue("$order", list.SortOrder);
        command.Parameters.AddWithValue("$default", list.Id == AgendaList.DefaultId ? 1 : 0);
        command.Parameters.AddWithValue("$created", FormatUtc(list.CreatedUtc));
        command.Parameters.AddWithValue("$updated", FormatUtc(list.UpdatedUtc));
        command.ExecuteNonQuery();
    }

    /// <summary>Whether a list exists, so an item naming an unknown one can fall back (§9).</summary>
    public static bool ListExists(
        SqliteConnection connection, SqliteTransaction? transaction, Guid id)
    {
        using SqliteCommand command = Create(
            connection, transaction, "SELECT 1 FROM agenda_lists WHERE id=$id;");
        command.Parameters.AddWithValue("$id", Format(id));
        return command.ExecuteScalar() is not null;
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────────────────────

    private static void ReplaceChildren(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string column,
        Guid itemId,
        IEnumerable<object> values)
    {
        using (SqliteCommand clear = Create(
            connection, transaction, $"DELETE FROM {table} WHERE item_id=$id;"))
        {
            clear.Parameters.AddWithValue("$id", Format(itemId));
            clear.ExecuteNonQuery();
        }

        foreach (object value in values)
        {
            using SqliteCommand insert = Create(
                connection,
                transaction,
                $"INSERT OR IGNORE INTO {table}(item_id,{column}) VALUES ($id,$value);");
            insert.Parameters.AddWithValue("$id", Format(itemId));
            insert.Parameters.AddWithValue("$value", value);
            insert.ExecuteNonQuery();
        }
    }

    private static List<AgendaItem> HydrateAll(SqliteConnection connection, SqliteCommand command)
    {
        var rows = new List<AgendaItem>();
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                rows.Add(ReadRow(reader));
            }
        }

        // After the reader closes: SQLite allows one at a time on a connection.
        return [.. rows.Select(row => Hydrate(connection, row))];
    }

    private static AgendaItem Hydrate(SqliteConnection connection, AgendaItem row) =>
        row with
        {
            ExceptionDates = [.. ReadChildren(connection, "agenda_exdates", "occurrence", row.Id)
                .Select(value => WallClock.Parse((string)value))],
            AlarmLeadMinutes = [.. ReadChildren(connection, "agenda_alarms", "lead_minutes", row.Id)
                .Select(value => Convert.ToInt32(value, CultureInfo.InvariantCulture))],
        };

    private static List<object> ReadChildren(
        SqliteConnection connection,
        string table,
        string column,
        Guid itemId)
    {
        using SqliteCommand command = Create(
            connection, null, $"SELECT {column} FROM {table} WHERE item_id=$id ORDER BY {column};");
        command.Parameters.AddWithValue("$id", Format(itemId));
        using SqliteDataReader reader = command.ExecuteReader();
        var values = new List<object>();
        while (reader.Read())
        {
            values.Add(reader.GetValue(0));
        }

        return values;
    }

    private static AgendaItem ReadRow(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        reader.GetString(2) == "event" ? AgendaKind.Event : AgendaKind.Task,
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        Wall(reader, 6),
        Wall(reader, 7),
        Wall(reader, 8),
        reader.GetInt32(9) == 1,
        reader.IsDBNull(10) ? null : reader.GetString(10),
        reader.IsDBNull(11) ? null : Guid.Parse(reader.GetString(11)),
        Wall(reader, 12),
        ParseStatus(reader.GetString(13)),
        reader.IsDBNull(14) ? null : ParseUtc(reader.GetString(14)),
        reader.GetInt32(15),
        ParseVisibility(reader.GetString(16)),
        reader.IsDBNull(17) ? null : Guid.Parse(reader.GetString(17)),
        [],
        [],
        ParseUtc(reader.GetString(18)),
        ParseUtc(reader.GetString(19)));

    private static AgendaList ReadList(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        reader.GetInt32(2),
        reader.GetInt32(3) == 1,
        ParseUtc(reader.GetString(4)),
        ParseUtc(reader.GetString(5)));

    private static AgendaList? ReadList(
        SqliteConnection connection, SqliteTransaction? transaction, Guid id)
    {
        using SqliteCommand command = Create(connection, transaction, ListColumns + " WHERE id=$id;");
        command.Parameters.AddWithValue("$id", Format(id));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadList(reader) : null;
    }

    private static WallClock? Wall(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : WallClock.Parse(reader.GetString(ordinal));

    private static void AddNullable(SqliteCommand command, string name, string? value) =>
        command.Parameters.AddWithValue(name, (object?)value ?? DBNull.Value);

    private static SqliteCommand Create(
        SqliteConnection connection, SqliteTransaction? transaction, string text)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = text;
        return command;
    }

    private static string StatusText(AgendaStatus status) => status switch
    {
        AgendaStatus.Completed => "completed",
        AgendaStatus.Cancelled => "cancelled",
        _ => "needs_action",
    };

    private static AgendaStatus ParseStatus(string text) => text switch
    {
        "completed" => AgendaStatus.Completed,
        "cancelled" => AgendaStatus.Cancelled,
        _ => AgendaStatus.NeedsAction,
    };

    private static string VisibilityText(TimelineVisibility visibility) => visibility switch
    {
        TimelineVisibility.Always => "always",
        TimelineVisibility.Never => "never",
        _ => "auto",
    };

    private static TimelineVisibility ParseVisibility(string text) => text switch
    {
        "always" => TimelineVisibility.Always,
        "never" => TimelineVisibility.Never,
        _ => TimelineVisibility.Auto,
    };

    public static string Format(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>
    /// The one <c>_utc</c> format this database uses, app-wide: <c>DateTimeOffset.ToString("O")</c>
    /// in UTC. Not cosmetic — sync compares <c>queued_utc</c> against
    /// <c>SyncTimestamps.ToLocal</c> as an exact string, so a second format here would leave every
    /// agenda row queued forever (migration 004's header, docs/CLOUD_SYNC.md §7.3).
    /// </summary>
    public static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtc(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
