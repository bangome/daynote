using System.Collections.ObjectModel;
using System.Globalization;
using Daynote.Core.Agenda;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Notes;
using Daynote.App.Shell.Product;
using Daynote.Core.Domain;

namespace Daynote.Mobile.ViewModels;

/// <summary>One day of the week strip on the home screen.</summary>
public sealed partial class WeekDayViewModel(
    LocalDate date,
    string weekdayLabel,
    bool isSelected,
    bool isToday,
    bool hasNotes,
    Func<LocalDate, Task> onSelect) : ObservableObject
{
    public LocalDate Date { get; } = date;

    public string WeekdayLabel { get; } = weekdayLabel;

    public string DayText { get; } = date.Day.ToString(CultureInfo.CurrentCulture);

    public bool IsSelected { get; } = isSelected;

    /// <summary>Today, when it is not also the selected day: the selected pill already says "here".</summary>
    public bool IsTodayRing { get; } = isToday && !isSelected;

    public bool IsSunday { get; } = Daynote.App.Composition.LocalDates.ToDateOnly(date).DayOfWeek == DayOfWeek.Sunday;

    public bool HasNotes { get; } = hasNotes;

    [RelayCommand]
    private Task Select() => onSelect(Date);
}

/// <summary>
/// A note on the home screen: the tab it opens, plus what the card shows that the tab does not
/// carry - a longer preview and the note's to-do progress.
/// </summary>
/// <remarks>
/// The title, the star and the tags are read live off the tab, so a rename or a new tag in the
/// editor is on the card the moment the editor closes; the preview and the progress are taken when
/// the card is built, which is every time the day's list is.
/// </remarks>
public sealed class DayNoteCardViewModel
{
    public DayNoteCardViewModel(NoteTabViewModel tab, int todoDone, int todoTotal, string emptyPreview)
    {
        Tab = tab;
        TodoDone = todoDone;
        TodoTotal = todoTotal;
        Preview = BuildPreview(tab.Body, emptyPreview);
    }

    public NoteTabViewModel Tab { get; }

    public string Preview { get; }

    public int TodoDone { get; }

    public int TodoTotal { get; }

    public bool HasTodos => TodoTotal > 0;

    /// <summary>Whether the card has a bottom line at all: tags on its left, progress on its right.</summary>
    public bool HasFooter => Tab.HasTags || HasTodos;

    /// <summary>The filled share of the progress track, 0 to 1.</summary>
    public double TodoFraction => TodoTotal == 0 ? 0 : (double)TodoDone / TodoTotal;

    /// <summary>The track is 44 points long; the fill is that share of it.</summary>
    public double TodoFillWidth => Math.Round(44 * TodoFraction, 1);

    public string TodoText => string.Create(CultureInfo.CurrentCulture, $"{TodoDone} / {TodoTotal}");

    /// <summary>The first three lines with anything on them, run together with a middle dot.</summary>
    public static string BuildPreview(string? body, string empty)
    {
        string[] lines = [.. (body ?? string.Empty)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Take(3)];
        return lines.Length == 0 ? empty : string.Join(" · ", lines);
    }
}

/// <summary>Which band of the to-do list a line falls in, and so the colour of its dot.</summary>
public enum TodoGroupKind
{
    Overdue,
    Today,
    Upcoming,
    NoDate,
    Done,
}

/// <summary>A to-do as the phone lists it: the shared item, plus its "업무 · 09/30" caption.</summary>
/// <remarks>
/// The caption used to name the note the <c>-[]</c> line was parsed out of. A to-do is its own
/// row now and often has no note behind it at all, so what identifies it is the list it is in —
/// which is also what the design labels a row with (§04, 4c).
/// </remarks>
public sealed class TodoRowViewModel(TodoItemViewModel item, AgendaDayRow row)
{
    public TodoItemViewModel Item { get; } = item;

    public string NoteCaption { get; } = Caption(item, row);

    private static string Caption(TodoItemViewModel item, AgendaDayRow row)
    {
        string day = row.Item.Anchor is { } anchor
            ? string.Create(CultureInfo.InvariantCulture, $"{anchor.Value.Month:00}/{anchor.Value.Day:00}")
            : string.Empty;

        if (item.NoteLabel.Length == 0)
        {
            return day;
        }

        return day.Length == 0
            ? item.NoteLabel
            : string.Create(CultureInfo.InvariantCulture, $"{item.NoteLabel} · {day}");
    }
}

/// <summary>One band of the Lists page's to-do tab: 지남, 오늘, 예정, 날짜 없음, 완료.</summary>
public sealed class TodoGroupViewModel(TodoGroupKind kind, string label, IReadOnlyList<TodoRowViewModel> items)
{
    public TodoGroupKind Kind { get; } = kind;

    public string Label { get; } = label;

    public string CountText { get; } = items.Count.ToString(CultureInfo.CurrentCulture);

    public IReadOnlyList<TodoRowViewModel> Items { get; } = items;

    public bool IsOverdue => Kind == TodoGroupKind.Overdue;

    public bool IsToday => Kind == TodoGroupKind.Today;

    public bool IsUpcoming => Kind == TodoGroupKind.Upcoming;

    public bool IsNoDate => Kind == TodoGroupKind.NoDate;

    public bool IsDone => Kind == TodoGroupKind.Done;

    /// <summary>
    /// Sorts parsed lines into the five bands, in the order the page shows them, leaving out the
    /// empty ones.
    /// </summary>
    public static IReadOnlyList<TodoGroupViewModel> Build(
        IReadOnlyList<AgendaDayRow> lines,
        DateTimeOffset now,
        Func<AgendaDayRow, TodoRowViewModel> row,
        Func<TodoGroupKind, string> label)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var bands = new Dictionary<TodoGroupKind, List<TodoRowViewModel>>();
        foreach (AgendaDayRow line in lines)
        {
            TodoGroupKind kind = KindOf(line, now);
            if (!bands.TryGetValue(kind, out List<TodoRowViewModel>? band))
            {
                band = [];
                bands[kind] = band;
            }

            band.Add(row(line));
        }

        return [.. Enum.GetValues<TodoGroupKind>()
            .Where(bands.ContainsKey)
            .Select(kind => new TodoGroupViewModel(kind, label(kind), bands[kind]))];
    }

    public static TodoGroupKind KindOf(AgendaDayRow row, DateTimeOffset now)
    {
        if (row.IsDone)
        {
            return TodoGroupKind.Done;
        }

        if (row.IsOverdue(now.DateTime))
        {
            return TodoGroupKind.Overdue;
        }

        // Owed on a day: today or before it is today's problem, later is upcoming. A to-do with
        // no day at all is neither, and gets the band that says so.
        return row.Item.Anchor is not { } anchor
            ? TodoGroupKind.NoDate
            : anchor.Value.Date <= now.Date ? TodoGroupKind.Today : TodoGroupKind.Upcoming;
    }
}

/// <summary>A tag on the Lists page's tag tab, and the notes that carry it.</summary>
public sealed partial class TagChipViewModel : ObservableObject
{
    private readonly Action<TagChipViewModel> _onSelect;

    public TagChipViewModel(TagSummary summary, Func<TagOccurrence, Task> onJump, Action<TagChipViewModel> onSelect)
    {
        Name = summary.Tag;
        CountText = summary.Count.ToString(CultureInfo.CurrentCulture);
        Notes = [.. summary.Occurrences.Select(occurrence => new TagNoteRowViewModel(occurrence, onJump))];
        _onSelect = onSelect;
    }

    /// <summary>The tag without its hash, which the view puts back in front.</summary>
    public string Name { get; }

    public string CountText { get; }

    public ObservableCollection<TagNoteRowViewModel> Notes { get; }

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Select() => _onSelect(this);
}

/// <summary>A note under the selected tag.</summary>
public sealed partial class TagNoteRowViewModel(TagOccurrence occurrence, Func<TagOccurrence, Task> onJump)
{
    public string Title { get; } = occurrence.NoteTitle;

    public string DateLabel { get; } = string.Create(
        CultureInfo.InvariantCulture, $"{occurrence.Date.Month:00}/{occurrence.Date.Day:00}");

    [RelayCommand]
    private Task Open() => onJump(occurrence);
}

/// <summary>A starred note on the Lists page: its day as "09/30", its title, its first line.</summary>
public sealed partial class FavoriteCardViewModel(Daynote.Core.Notes.NoteSummary note, Func<Daynote.Core.Notes.NoteSummary, Task> onOpen)
{
    public string DateLabel { get; } = string.Create(
        CultureInfo.InvariantCulture, $"{note.LocalDate.Month:00}/{note.LocalDate.Day:00}");

    public string Title { get; } = note.Title;

    public string Preview { get; } = (note.Body ?? string.Empty)
        .Split('\n')
        .Select(line => line.Trim())
        .FirstOrDefault(line => line.Length > 0) ?? string.Empty;

    [RelayCommand]
    private Task Open() => onOpen(note);
}
