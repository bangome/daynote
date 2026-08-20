using System.Text.Json;
using System.Text.Json.Serialization;
using Daynote.Core.Domain;

namespace Daynote.Core.Sync;

/// <summary>
/// The note fields, as the single JSON object that gets encrypted into one blob
/// (docs/CLOUD_SYNC.md §5.1).
/// </summary>
/// <remarks>
/// One blob rather than per-field encryption: it is simpler, and it leaves the server no per-field
/// structure to mine. Note that <c>local_date</c> is inside the envelope, so the server cannot even
/// tell which days the user writes on.
/// <para>
/// The property names are wire format. Renaming one silently orphans every note already in the
/// cloud, so they are pinned by <see cref="JsonPropertyNameAttribute"/> and must not follow C#
/// renames.
/// </para>
/// </remarks>
internal sealed record NotePayload(
    [property: JsonPropertyName("local_date")] string LocalDate,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("sort_order")] int SortOrder,
    [property: JsonPropertyName("is_favorite")] bool IsFavorite,
    [property: JsonPropertyName("has_custom_title")] bool HasCustomTitle,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("created_utc")] string CreatedUtc);

internal static class NotePayloadCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        // Compact and predictable: the ciphertext length leaks a rough size, so there is no reason to
        // pad it with formatting.
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    internal static string Serialize(SyncNote note) =>
        JsonSerializer.Serialize(
            new NotePayload(
                note.LocalDate.ToString(),
                note.Title,
                note.Body,
                note.SortOrder,
                note.IsFavorite,
                note.HasCustomTitle,
                note.Tags,
                SyncTimestamps.ToWire(note.CreatedUtc)),
            Options);

    /// <summary>
    /// Rebuilds a note from a decrypted payload. Returns a failure rather than throwing for anything
    /// malformed: the bytes decrypted correctly, so this is a version or corruption problem the engine
    /// should report and skip past, not a crash in a background sync.
    /// </summary>
    internal static DomainResult<SyncNote> Deserialize(
        string id,
        string json,
        DateTimeOffset updatedUtc)
    {
        NotePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<NotePayload>(json, Options);
        }
        catch (JsonException)
        {
            return Invalid("A note payload was not valid JSON.");
        }

        if (payload is null)
        {
            return Invalid("A note payload was empty.");
        }

        DomainResult<LocalDate> date = LocalDate.Parse(payload.LocalDate);
        if (!date.IsSuccess)
        {
            return Invalid("A note payload carried an invalid local date.");
        }

        DomainResult<DateTimeOffset> created = SyncTimestamps.ParseWire(payload.CreatedUtc);
        if (!created.IsSuccess)
        {
            return Invalid("A note payload carried an invalid created timestamp.");
        }

        if (payload.Title is null || payload.Body is null)
        {
            return Invalid("A note payload was missing its title or body.");
        }

        if (payload.SortOrder < 0)
        {
            return Invalid("A note payload carried a negative sort order.");
        }

        return DomainResult<SyncNote>.Success(new SyncNote(
            id,
            date.Value,
            payload.Title,
            payload.Body,
            payload.SortOrder,
            payload.IsFavorite,
            payload.HasCustomTitle,
            payload.Tags ?? [],
            created.Value,
            updatedUtc));
    }

    private static DomainResult<SyncNote> Invalid(string message) =>
        DomainResult<SyncNote>.Failure(DomainErrorCode.MalformedSyncPayload, message);
}
