using System.Globalization;
using System.Text.RegularExpressions;
using Daynote.Core.Domain;
using Daynote.Core.Notes;

namespace Daynote.App.Notes;

/// <summary>
/// One parsed todo line from a note body. Mirrors the design's <c>parseTodos()</c> item shape
/// (calendar-notes.dc.html): a checkbox line <c>-[] task (M/D H:mm)</c> with an optional trailing due
/// stamp, carrying its origin note so the panel can jump to and rewrite the exact source line.
/// </summary>
public readonly record struct TodoLine(
    Guid NoteId,
    LocalDate Date,
    string NoteTitle,
    int LineIndex,
    bool Checked,
    string Text,
    string DueLabel,
    DateTimeOffset? Due,
    bool Overdue);

/// <summary>
/// Pure port of the design's todo logic. The checkbox grammar is
/// <c>^\s*-\s?\[( |x|X)?\]\s*(.*)$</c>; a due suffix <c>(M/D)</c> or <c>(M/D H:mm)</c> at the end of the
/// task text is lifted into <see cref="TodoLine.Due"/> (year taken from <paramref name="now"/>, defaulting
/// to 23:59 when no time is given, matching the design). Items sort unchecked-first, then by due time with
/// undated items last. <see cref="ToggleLine"/> rewrites a single body line's checkbox in place.
/// </summary>
public static partial class TodoParsing
{
    [GeneratedRegex(@"^\s*-\s?\[( |x|X)?\]\s*(.*)$")]
    private static partial Regex CheckboxLine();

    [GeneratedRegex(@"\((\d{1,2})/(\d{1,2})(?:\s+(\d{1,2}):(\d{2}))?\)\s*$")]
    private static partial Regex DueSuffix();

    [GeneratedRegex(@"^(\s*-\s?\[)( |x|X)?(\].*)$")]
    private static partial Regex ToggleTarget();

    /// <summary>Parses and sorts todos across the given notes, oldest-relative ordering per the design.</summary>
    public static IReadOnlyList<TodoLine> Parse(IEnumerable<NoteSummary> notes, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var items = new List<TodoLine>();
        foreach (NoteSummary note in notes)
        {
            string[] lines = (note.Body ?? string.Empty).Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                Match match = CheckboxLine().Match(lines[index]);
                if (!match.Success)
                {
                    continue;
                }

                bool @checked = string.Equals(match.Groups[1].Value, "x", StringComparison.OrdinalIgnoreCase);
                string text = match.Groups[2].Value;
                DateTimeOffset? due = null;
                string dueLabel = string.Empty;

                Match dueMatch = DueSuffix().Match(text);
                if (dueMatch.Success)
                {
                    int month = int.Parse(dueMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                    int day = int.Parse(dueMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                    bool hasTime = dueMatch.Groups[3].Success;
                    int hour = hasTime ? int.Parse(dueMatch.Groups[3].Value, CultureInfo.InvariantCulture) : 23;
                    int minute = hasTime ? int.Parse(dueMatch.Groups[4].Value, CultureInfo.InvariantCulture) : 59;
                    due = BuildDue(now, month, day, hour, minute);
                    dueLabel = hasTime
                        ? $"{month}/{day} {dueMatch.Groups[3].Value}:{dueMatch.Groups[4].Value}"
                        : $"{month}/{day}";
                    text = text[..dueMatch.Index].TrimEnd();
                    text = text.TrimEnd();
                }

                text = text.Trim();
                items.Add(new TodoLine(
                    note.Id,
                    note.LocalDate,
                    note.Title,
                    index,
                    @checked,
                    text.Length == 0 ? Localization.AppStrings.TodoEmptyText : text,
                    dueLabel,
                    due,
                    due is { } d && !@checked && d < now));
            }
        }

        items.Sort(static (a, b) =>
        {
            int byChecked = (a.Checked ? 1 : 0) - (b.Checked ? 1 : 0);
            if (byChecked != 0)
            {
                return byChecked;
            }

            long at = a.Due?.Ticks ?? long.MaxValue;
            long bt = b.Due?.Ticks ?? long.MaxValue;
            return at.CompareTo(bt);
        });

        return items;
    }

    /// <summary>
    /// Rewrites the checkbox marker on <paramref name="lineIndex"/> of <paramref name="body"/>, flipping
    /// checked/unchecked. Returns the original body unchanged when the line is not a checkbox line.
    /// </summary>
    public static string ToggleLine(string body, int lineIndex)
    {
        ArgumentNullException.ThrowIfNull(body);
        string[] lines = body.Split('\n');
        if (lineIndex < 0 || lineIndex >= lines.Length)
        {
            return body;
        }

        Match match = ToggleTarget().Match(lines[lineIndex]);
        if (!match.Success)
        {
            return body;
        }

        bool wasChecked = string.Equals(match.Groups[2].Value, "x", StringComparison.OrdinalIgnoreCase);
        lines[lineIndex] = match.Groups[1].Value + (wasChecked ? " " : "x") + match.Groups[3].Value;
        return string.Join('\n', lines);
    }

    private static DateTimeOffset? BuildDue(DateTimeOffset now, int month, int day, int hour, int minute)
    {
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(now.Year, month)
            || hour is < 0 or > 23 || minute is < 0 or > 59)
        {
            return null;
        }

        return new DateTimeOffset(now.Year, month, day, hour, minute, 0, now.Offset);
    }
}
