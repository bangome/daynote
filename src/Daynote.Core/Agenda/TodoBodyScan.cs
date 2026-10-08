using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Daynote.Core.Domain;

namespace Daynote.Core.Agenda;

/// <summary>One <c>-[ ]</c> line found in a note body, as the one-time migration will store it.</summary>
/// <param name="Id">
/// Derived from the note and the line, never random. See <see cref="TodoBodyScan"/>.
/// </param>
public readonly record struct ScannedTodo(
    Guid Id,
    int LineIndex,
    string Text,
    bool Completed,
    WallClock? DueAt,
    bool HasDueTime);

/// <summary>
/// The <c>-[ ]</c> checkbox grammar, and the walk that turns a note body into entities
/// (docs/TODOS.md §8).
/// </summary>
/// <remarks>
/// This is the single definition of the grammar. <c>TodoParsing</c> in the presentation layer reads
/// it from here rather than keeping its own copy, because the migration has to find exactly the
/// lines the panel has been showing the user — a pattern that drifted by one character would
/// silently leave some of them behind as plain text.
/// <para>
/// <b>Ids are derived, not generated.</b> Two devices both run this migration, offline, against the
/// same note bodies; random ids would give the user every task twice after the next sync. The id is
/// a UUIDv5 over the note id, the task text, and how many identical texts precede it in that note,
/// so both devices produce the same one and last-write-wins merges them into a single row.
/// </para>
/// <para>
/// The window where that fails is real and accepted: if the body is edited on one device between
/// the two migrations, the text differs and the task duplicates. The alternatives are worse — the
/// line number shifts whenever a line is inserted above, and the text alone would collapse two
/// genuinely separate identical lines into one. §8 already calls this migration irreversible and
/// requires a backup before it.
/// </para>
/// </remarks>
public static partial class TodoBodyScan
{
    /// <summary>Namespace for the derived ids. Fixed forever: changing it re-migrates everything.</summary>
    private static readonly Guid Namespace = new("5a1f6c2e-7b84-4f0a-9d33-1c0de0000001");

    [GeneratedRegex(@"^\s*-\s?\[( |x|X)?\]\s*(.*)$")]
    public static partial Regex CheckboxLine();

    [GeneratedRegex(@"\((\d{1,2})/(\d{1,2})(?:\s+(\d{1,2}):(\d{2}))?\)\s*$")]
    public static partial Regex DueSuffix();

    [GeneratedRegex(@"^(\s*-\s?\[)( |x|X)?(\].*)$")]
    public static partial Regex ToggleTarget();

    /// <summary>
    /// Every checkbox line in one note body, in order.
    /// </summary>
    /// <param name="noteDate">
    /// The note's own date. It supplies the year for a <c>(M/D)</c> stamp, which carries none — and
    /// the note it was written in is far better evidence of which year was meant than today is.
    /// </param>
    public static IReadOnlyList<ScannedTodo> Scan(Guid noteId, LocalDate noteDate, string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return [];
        }

        var found = new List<ScannedTodo>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        string[] lines = body.Split('\n');

        for (int index = 0; index < lines.Length; index += 1)
        {
            Match match = CheckboxLine().Match(lines[index]);
            if (!match.Success)
            {
                continue;
            }

            bool completed = string.Equals(
                match.Groups[1].Value, "x", StringComparison.OrdinalIgnoreCase);
            string text = match.Groups[2].Value;
            WallClock? due = null;
            bool hasTime = false;

            Match dueMatch = DueSuffix().Match(text);
            if (dueMatch.Success)
            {
                due = BuildDue(noteDate, dueMatch, out hasTime);
                if (due is not null)
                {
                    text = text[..dueMatch.Index].TrimEnd();
                }
            }

            text = text.Trim();
            if (text.Length == 0)
            {
                // A bare "- []" is formatting the user left behind, not a task. Making an untitled
                // to-do out of it would put a blank row in the panel that nobody can act on.
                continue;
            }

            int ordinal = seen.TryGetValue(text, out int previous) ? previous + 1 : 0;
            seen[text] = ordinal;

            found.Add(new ScannedTodo(
                DeriveId(noteId, ordinal, text),
                index,
                text,
                completed,
                due,
                hasTime));
        }

        return found;
    }

    /// <summary>RFC 4122 version 5: SHA-1 over the namespace and the name, with the version bits set.</summary>
    private static Guid DeriveId(Guid noteId, int ordinal, string text)
    {
        string name = string.Create(
            CultureInfo.InvariantCulture,
            $"{noteId:D}|{ordinal}|{text}");

        Span<byte> input = stackalloc byte[16];
        WriteBigEndian(Namespace, input);
        byte[] hash = SHA1.HashData([.. input.ToArray(), .. Encoding.UTF8.GetBytes(name)]);

        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return ReadBigEndian(hash);
    }

    /// <summary>
    /// Guid's own byte order is little-endian for the first three fields, which is a Microsoft
    /// detail rather than part of RFC 4122. Writing it big-endian is what makes the derived id the
    /// same on any runtime that follows the spec.
    /// </summary>
    private static void WriteBigEndian(Guid value, Span<byte> destination)
    {
        value.TryWriteBytes(destination);
        (destination[0], destination[3]) = (destination[3], destination[0]);
        (destination[1], destination[2]) = (destination[2], destination[1]);
        (destination[4], destination[5]) = (destination[5], destination[4]);
        (destination[6], destination[7]) = (destination[7], destination[6]);
    }

    private static Guid ReadBigEndian(byte[] hash)
    {
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        (bytes[0], bytes[3]) = (bytes[3], bytes[0]);
        (bytes[1], bytes[2]) = (bytes[2], bytes[1]);
        (bytes[4], bytes[5]) = (bytes[5], bytes[4]);
        (bytes[6], bytes[7]) = (bytes[7], bytes[6]);
        return new Guid(bytes);
    }

    private static WallClock? BuildDue(LocalDate noteDate, Match dueMatch, out bool hasTime)
    {
        int month = int.Parse(dueMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        int day = int.Parse(dueMatch.Groups[2].Value, CultureInfo.InvariantCulture);
        hasTime = dueMatch.Groups[3].Success;
        int hour = hasTime ? int.Parse(dueMatch.Groups[3].Value, CultureInfo.InvariantCulture) : 23;
        int minute = hasTime ? int.Parse(dueMatch.Groups[4].Value, CultureInfo.InvariantCulture) : 59;

        int year = noteDate.Year;
        if (month is < 1 or > 12
            || day < 1 || day > DateTime.DaysInMonth(year, month)
            || hour is < 0 or > 23
            || minute is < 0 or > 59)
        {
            // Not a due stamp after all — "(13/40)" is just text. Left in the title rather than
            // dropped, because the user wrote it.
            hasTime = false;
            return null;
        }

        return new WallClock(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified));
    }
}
