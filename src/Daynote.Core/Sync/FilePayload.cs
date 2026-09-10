using System.Text.Json;
using System.Text.Json.Serialization;
using Daynote.Core.Domain;

namespace Daynote.Core.Sync;

/// <summary>
/// An attachment's metadata, as the single JSON object that gets encrypted into one blob
/// (docs/CLOUD_SYNC.md §5).
/// </summary>
/// <remarks>
/// The filename is in here, not beside it, and that is the point: the server stores attachments
/// without learning what any of them is called or which day it belongs to. What it unavoidably
/// learns is the size and the blinded key, because it has to count a quota and name an object.
/// <para>
/// <c>asset_hash</c> is the plaintext content hash. It never leaves the device in this form — the
/// blinded key sent alongside is <c>HMAC(HKDF(DEK), hash)</c> — but it is what the receiving device
/// uses to name the bytes in its own content-addressed store, and to derive that same blinded key.
/// </para>
/// <para>
/// The property names are wire format, pinned for the same reason the note payload's are: renaming
/// one orphans every attachment already in the cloud.
/// </para>
/// </remarks>
internal sealed record FilePayload(
    [property: JsonPropertyName("local_date")] string LocalDate,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("byte_length")] long ByteLength,
    [property: JsonPropertyName("asset_hash")] string AssetHash,
    [property: JsonPropertyName("created_utc")] string CreatedUtc);

internal static class FilePayloadCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    internal static string Serialize(SyncFile file) =>
        JsonSerializer.Serialize(
            new FilePayload(
                file.LocalDate.ToString(),
                file.DisplayName,
                file.ByteLength,
                file.AssetHash,
                SyncTimestamps.ToWire(file.CreatedUtc)),
            Options);

    /// <summary>
    /// Rebuilds an attachment from a decrypted payload. Returns a failure rather than throwing:
    /// the bytes decrypted, so anything wrong past that point is a version or corruption problem to
    /// report and skip, not a crash inside a background sync.
    /// </summary>
    internal static DomainResult<SyncFile> Deserialize(
        string id,
        string json,
        DateTimeOffset updatedUtc)
    {
        FilePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<FilePayload>(json, Options);
        }
        catch (JsonException)
        {
            return Malformed("The attachment payload is not valid JSON.");
        }

        if (payload is null)
        {
            return Malformed("The attachment payload is empty.");
        }

        DomainResult<LocalDate> date = LocalDate.Parse(payload.LocalDate);
        if (!date.IsSuccess)
        {
            return Malformed("The attachment payload carries an unreadable date.");
        }

        if (string.IsNullOrWhiteSpace(payload.AssetHash))
        {
            return Malformed("The attachment payload names no content.");
        }

        if (payload.ByteLength < 0)
        {
            return Malformed("The attachment payload declares a negative length.");
        }

        DomainResult<DateTimeOffset> created = SyncTimestamps.ParseWire(payload.CreatedUtc);
        DateTimeOffset createdUtc = created.IsSuccess ? created.Value : updatedUtc;
        // An unreadable creation time is not fatal on its own: the attachment is still the file it
        // is, and the update clock is what ordering actually uses.

        return DomainResult<SyncFile>.Success(
            new SyncFile(
                id,
                date.Value,
                payload.DisplayName ?? string.Empty,
                payload.ByteLength,
                payload.AssetHash,
                createdUtc));
    }

    private static DomainResult<SyncFile> Malformed(string message) =>
        DomainResult<SyncFile>.Failure(DomainErrorCode.MalformedCiphertext, message);
}
