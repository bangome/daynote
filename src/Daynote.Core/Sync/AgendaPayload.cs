using System.Text.Json;
using System.Text.Json.Serialization;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;

namespace Daynote.Core.Sync;

/// <summary>
/// A to-do or an event, as the single JSON object that gets encrypted into one blob
/// (docs/CLOUD_SYNC.md §5.1, docs/TODOS.md §9).
/// </summary>
/// <remarks>
/// Everything but the id and the update clock is in here, so the server learns no more about a
/// to-do than it does about a note: not its title, not when it is due, not which list it is in.
/// <para>
/// The names are iCalendar's wherever iCalendar has one — <c>rrule</c>, <c>exdate</c>,
/// <c>recurrence_id</c>, <c>status</c>, <c>priority</c> — because this shape is also what the
/// <c>.ics</c> feed and CalDAV will be built from, and two vocabularies for one field is how the
/// mapping starts drifting.
/// </para>
/// <para>
/// The property names are wire format. Renaming one orphans every item already in the cloud, so
/// they are pinned by <see cref="JsonPropertyNameAttribute"/> and must not follow C# renames.
/// </para>
/// </remarks>
internal sealed record AgendaItemPayload(
    [property: JsonPropertyName("list_id")] string ListId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("tz")] string Zone,
    [property: JsonPropertyName("starts_at")] string? StartsAt,
    [property: JsonPropertyName("ends_at")] string? EndsAt,
    [property: JsonPropertyName("due_at")] string? DueAt,
    [property: JsonPropertyName("has_due_time")] bool HasDueTime,
    [property: JsonPropertyName("rrule")] string? Rrule,
    [property: JsonPropertyName("series_id")] string? SeriesId,
    [property: JsonPropertyName("recurrence_id")] string? RecurrenceId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("completed_utc")] string? CompletedUtc,
    [property: JsonPropertyName("priority")] int Priority,
    [property: JsonPropertyName("timeline_visibility")] string TimelineVisibility,
    [property: JsonPropertyName("source_note_id")] string? SourceNoteId,
    [property: JsonPropertyName("exdate")] IReadOnlyList<string> ExceptionDates,
    [property: JsonPropertyName("alarms")] IReadOnlyList<int> AlarmLeadMinutes,
    [property: JsonPropertyName("created_utc")] string CreatedUtc);

/// <summary>A to-do container, as one encrypted blob. Small, and rarely edited.</summary>
internal sealed record AgendaListPayload(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("sort_order")] int SortOrder,
    [property: JsonPropertyName("is_default")] bool IsDefault,
    [property: JsonPropertyName("created_utc")] string CreatedUtc);

internal static class AgendaPayloadCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    internal static string Serialize(AgendaItem item) =>
        JsonSerializer.Serialize(
            new AgendaItemPayload(
                item.ListId.ToString(),
                WireKind(item.Kind),
                item.Title,
                item.Description,
                item.Zone,
                item.StartsAt?.ToString(),
                item.EndsAt?.ToString(),
                item.DueAt?.ToString(),
                item.HasDueTime,
                item.Rrule,
                item.SeriesId?.ToString(),
                item.RecurrenceId?.ToString(),
                WireStatus(item.Status),
                item.CompletedUtc is { } completed ? SyncTimestamps.ToWire(completed) : null,
                item.Priority,
                WireVisibility(item.TimelineVisibility),
                item.SourceNoteId?.ToString(),
                [.. item.ExceptionDates.Select(static value => value.ToString())],
                item.AlarmLeadMinutes,
                SyncTimestamps.ToWire(item.CreatedUtc)),
            Options);

    internal static string Serialize(AgendaList list) =>
        JsonSerializer.Serialize(
            new AgendaListPayload(
                list.Name,
                list.SortOrder,
                list.IsDefault,
                SyncTimestamps.ToWire(list.CreatedUtc)),
            Options);

    /// <summary>
    /// Rebuilds an item from a decrypted payload. Returns a failure rather than throwing, for the
    /// same reason the note codec does: the bytes decrypted, so anything wrong past this point is a
    /// version or corruption problem to report and skip, not a crash in a background sync.
    /// </summary>
    internal static DomainResult<AgendaItem> DeserializeItem(
        string id,
        string json,
        DateTimeOffset updatedUtc)
    {
        if (!Guid.TryParse(id, out Guid itemId))
        {
            return InvalidItem("An agenda payload arrived under an id that is not a GUID.");
        }

        AgendaItemPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<AgendaItemPayload>(json, Options);
        }
        catch (JsonException)
        {
            return InvalidItem("An agenda payload was not valid JSON.");
        }

        if (payload is null)
        {
            return InvalidItem("An agenda payload was empty.");
        }

        if (!Guid.TryParse(payload.ListId, out Guid listId))
        {
            return InvalidItem("An agenda payload named no list.");
        }

        if (!TryReadKind(payload.Kind, out AgendaKind kind) ||
            !TryReadStatus(payload.Status, out AgendaStatus status) ||
            !TryReadVisibility(payload.TimelineVisibility, out TimelineVisibility visibility))
        {
            return InvalidItem("An agenda payload carried an unknown enumeration value.");
        }

        if (payload.Title is null || string.IsNullOrEmpty(payload.Zone))
        {
            return InvalidItem("An agenda payload was missing its title or zone.");
        }

        if (payload.Priority is < 0 or > 9)
        {
            return InvalidItem("An agenda payload carried a priority outside 0-9.");
        }

        if (!TryReadClock(payload.StartsAt, out WallClock? startsAt) ||
            !TryReadClock(payload.EndsAt, out WallClock? endsAt) ||
            !TryReadClock(payload.DueAt, out WallClock? dueAt) ||
            !TryReadClock(payload.RecurrenceId, out WallClock? recurrenceId))
        {
            return InvalidItem("An agenda payload carried an unreadable wall-clock time.");
        }

        var exceptions = new List<WallClock>();
        foreach (string value in payload.ExceptionDates ?? [])
        {
            if (!TryReadClock(value, out WallClock? exception) || exception is null)
            {
                return InvalidItem("An agenda payload carried an unreadable EXDATE.");
            }

            exceptions.Add(exception.Value);
        }

        Guid? seriesId = null;
        if (payload.SeriesId is not null)
        {
            if (!Guid.TryParse(payload.SeriesId, out Guid parsed))
            {
                return InvalidItem("An agenda payload carried an unreadable series id.");
            }

            seriesId = parsed;
        }

        Guid? sourceNoteId = null;
        if (payload.SourceNoteId is not null && Guid.TryParse(payload.SourceNoteId, out Guid note))
        {
            // Allowed to dangle and allowed to be unreadable: it only drives a jump back to where
            // the item was captured, and losing that is never a reason to drop the item.
            sourceNoteId = note;
        }

        DomainResult<DateTimeOffset> created = SyncTimestamps.ParseWire(payload.CreatedUtc);
        if (!created.IsSuccess)
        {
            return InvalidItem("An agenda payload carried an invalid created timestamp.");
        }

        DateTimeOffset? completedUtc = null;
        if (payload.CompletedUtc is not null)
        {
            DomainResult<DateTimeOffset> completed = SyncTimestamps.ParseWire(payload.CompletedUtc);
            if (!completed.IsSuccess)
            {
                return InvalidItem("An agenda payload carried an invalid completion timestamp.");
            }

            completedUtc = completed.Value;
        }

        return DomainResult<AgendaItem>.Success(new AgendaItem(
            itemId,
            listId,
            kind,
            payload.Title,
            payload.Description ?? string.Empty,
            payload.Zone,
            startsAt,
            endsAt,
            dueAt,
            payload.HasDueTime,
            payload.Rrule,
            seriesId,
            recurrenceId,
            status,
            completedUtc,
            payload.Priority,
            visibility,
            sourceNoteId,
            exceptions,
            payload.AlarmLeadMinutes ?? [],
            created.Value,
            updatedUtc));
    }

    internal static DomainResult<AgendaList> DeserializeList(
        string id,
        string json,
        DateTimeOffset updatedUtc)
    {
        if (!Guid.TryParse(id, out Guid listId))
        {
            return InvalidList("An agenda list payload arrived under an id that is not a GUID.");
        }

        AgendaListPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<AgendaListPayload>(json, Options);
        }
        catch (JsonException)
        {
            return InvalidList("An agenda list payload was not valid JSON.");
        }

        if (payload is null || payload.Name is null)
        {
            return InvalidList("An agenda list payload was empty.");
        }

        if (payload.SortOrder < 0)
        {
            return InvalidList("An agenda list payload carried a negative sort order.");
        }

        DomainResult<DateTimeOffset> created = SyncTimestamps.ParseWire(payload.CreatedUtc);
        if (!created.IsSuccess)
        {
            return InvalidList("An agenda list payload carried an invalid created timestamp.");
        }

        return DomainResult<AgendaList>.Success(new AgendaList(
            listId,
            payload.Name,
            payload.SortOrder,
            // The default flag is not taken from the wire. `is_default` is unique in the schema, so
            // honouring a remote one would make applying a page depend on the order its rows happen
            // to arrive in. The default is the fixed id and nothing else.
            listId == AgendaList.DefaultId,
            created.Value,
            updatedUtc));
    }

    private static bool TryReadClock(string? value, out WallClock? clock)
    {
        if (value is null)
        {
            clock = null;
            return true;
        }

        try
        {
            clock = WallClock.Parse(value);
            return true;
        }
        catch (FormatException)
        {
            clock = null;
            return false;
        }
    }

    private static string WireKind(AgendaKind kind) => kind == AgendaKind.Event ? "event" : "task";

    private static bool TryReadKind(string? value, out AgendaKind kind)
    {
        kind = value == "event" ? AgendaKind.Event : AgendaKind.Task;
        return value is "event" or "task";
    }

    private static string WireStatus(AgendaStatus status) => status switch
    {
        AgendaStatus.Completed => "completed",
        AgendaStatus.Cancelled => "cancelled",
        _ => "needs_action",
    };

    private static bool TryReadStatus(string? value, out AgendaStatus status)
    {
        switch (value)
        {
            case "completed":
                status = AgendaStatus.Completed;
                return true;
            case "cancelled":
                status = AgendaStatus.Cancelled;
                return true;
            case "needs_action":
                status = AgendaStatus.NeedsAction;
                return true;
            default:
                status = AgendaStatus.NeedsAction;
                return false;
        }
    }

    private static string WireVisibility(TimelineVisibility visibility) => visibility switch
    {
        TimelineVisibility.Always => "always",
        TimelineVisibility.Never => "never",
        _ => "auto",
    };

    private static bool TryReadVisibility(string? value, out TimelineVisibility visibility)
    {
        switch (value)
        {
            case "always":
                visibility = TimelineVisibility.Always;
                return true;
            case "never":
                visibility = TimelineVisibility.Never;
                return true;
            case "auto":
                visibility = TimelineVisibility.Auto;
                return true;
            default:
                visibility = TimelineVisibility.Auto;
                return false;
        }
    }

    private static DomainResult<AgendaItem> InvalidItem(string message) =>
        DomainResult<AgendaItem>.Failure(DomainErrorCode.MalformedSyncPayload, message);

    private static DomainResult<AgendaList> InvalidList(string message) =>
        DomainResult<AgendaList>.Failure(DomainErrorCode.MalformedSyncPayload, message);
}
