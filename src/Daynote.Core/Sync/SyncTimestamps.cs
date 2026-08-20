using System.Globalization;
using Daynote.Core.Domain;

namespace Daynote.Core.Sync;

/// <summary>
/// The two timestamp formats cloud sync deals with, and the rule for never confusing them.
/// </summary>
/// <remarks>
/// The local database stores <c>DateTimeOffset.ToString("O")</c>
/// (<c>2026-08-20T12:34:56.7890000+00:00</c>). The wire uses the same precision with a <c>Z</c>
/// suffix (<c>2026-08-20T12:34:56.7890000Z</c>) so the server can compare timestamps as plain
/// strings.
/// <para>
/// These two must never be compared against each other as strings. After a shared
/// <c>…56.789</c> prefix, the local form continues <c>0000+00:00</c> and the wire form
/// <c>0000Z</c>; <c>'+'</c> (0x2B) sorts before <c>'Z'</c> (0x5A), so the local form of an instant
/// reads as *older* than the wire form of the same instant. Convert at the boundary — which is what
/// this type is for — and compare <see cref="DateTimeOffset"/> values everywhere else.
/// </para>
/// </remarks>
public static class SyncTimestamps
{
    /// <summary>Sortable, unambiguous, and tick-precise. What crosses the network.</summary>
    public const string WireFormat = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

    /// <summary>What the SQLite <c>_utc</c> columns already hold, app-wide.</summary>
    private const string LocalFormat = "O";

    public static string ToWire(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(WireFormat, CultureInfo.InvariantCulture);

    public static string ToLocal(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(LocalFormat, CultureInfo.InvariantCulture);

    public static DomainResult<DateTimeOffset> ParseWire(string? value)
    {
        if (value is null ||
            !DateTimeOffset.TryParseExact(
                value,
                WireFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed))
        {
            return DomainResult<DateTimeOffset>.Failure(
                DomainErrorCode.InvalidSyncTimestamp,
                $"A sync timestamp must be '{WireFormat}'.");
        }

        return DomainResult<DateTimeOffset>.Success(parsed);
    }

    /// <summary>
    /// Reads a timestamp written by the app or by the tombstone triggers. Round-trip parsing is
    /// permissive on purpose: rows predate this feature and were written by several code paths.
    /// </summary>
    public static bool TryParseLocal(string? value, out DateTimeOffset parsed)
    {
        if (value is null)
        {
            parsed = default;
            return false;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AdjustToUniversal,
            out parsed);
    }
}
