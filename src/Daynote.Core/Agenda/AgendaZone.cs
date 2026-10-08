namespace Daynote.Core.Agenda;

/// <summary>
/// The IANA zone id every wall-clock field is read against (docs/TODOS.md §6).
/// </summary>
/// <remarks>
/// IANA and not the Windows name, because the id travels: it goes into the sync payload, into
/// <c>DTSTART;TZID=</c> on the way out to a calendar, and onto a phone that has never heard of
/// "Korea Standard Time". Windows is the only platform that needs the conversion and .NET ships
/// the mapping for it.
/// </remarks>
public static class AgendaZone
{
    /// <summary>
    /// This device's zone, or <c>"UTC"</c> when the mapping fails. A wrong-but-valid zone is worse
    /// than UTC: it would silently shift every time the user reads back, where UTC is at least a
    /// visible, explainable offset.
    /// </summary>
    public static string Local()
    {
        TimeZoneInfo local = TimeZoneInfo.Local;
        if (local.HasIanaId)
        {
            return local.Id;
        }

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(local.Id, out string? iana) ? iana : "UTC";
    }
}
