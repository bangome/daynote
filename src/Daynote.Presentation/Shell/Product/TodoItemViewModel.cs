using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Localization;
using Daynote.Core.Agenda;
using Daynote.App.Composition;
using Daynote.Core.Domain;

namespace Daynote.App.Shell.Product;

/// <summary>How much of the when a row has to say.</summary>
public enum TodoRowScope
{
    /// <summary>The day panel, where the day is the heading and only the clock is news.</summary>
    Day,

    /// <summary>The 할 일 tab, which crosses dates and has to name the day.</summary>
    AllDates,
}

/// <summary>
/// One row in a to-do list: an <see cref="AgendaDayRow"/>, which is either an item or one
/// occurrence of a rule (design §04, 4c/4d).
/// </summary>
/// <remarks>
/// It used to wrap a <c>-[]</c> line parsed out of a note body, and ticking it rewrote that line.
/// It now carries an entity, and ticking it goes through <see cref="ToggleAgendaItem"/> — which on
/// an occurrence writes an override rather than touching the rule.
/// </remarks>
public sealed partial class TodoItemViewModel : ObservableObject
{
    private readonly AgendaDayRow row;
    private readonly TodoRowScope scope;
    private readonly string listName;
    private readonly DateTime now;
    private readonly Func<AgendaDayRow, Task> onToggle;
    private readonly Func<AgendaDayRow, Task> onJump;
    private readonly Func<AgendaDayRow, Task>? onEdit;
    private readonly Func<AgendaDayRow, Task>? onDelete;

    public TodoItemViewModel(
        AgendaDayRow row,
        TodoRowScope scope,
        string listName,
        DateTime now,
        Func<AgendaDayRow, Task> onToggle,
        Func<AgendaDayRow, Task> onJump,
        Func<AgendaDayRow, Task>? onEdit = null,
        Func<AgendaDayRow, Task>? onDelete = null,
        string listColor = "")
    {
        this.row = row;
        ListColor = listColor;
        this.scope = scope;
        this.listName = listName;
        this.now = now;
        this.onToggle = onToggle ?? throw new ArgumentNullException(nameof(onToggle));
        this.onJump = onJump ?? throw new ArgumentNullException(nameof(onJump));
        this.onEdit = onEdit;
        this.onDelete = onDelete;
    }

    public bool Checked => row.IsDone;

    /// <summary>The day it falls on, so a view can keep only one day's rows.</summary>
    public LocalDate Date => LocalDates.FromDateOnly(
        DateOnly.FromDateTime(row.Falls?.Value ?? now));

    public string Text => row.Item.Title;

    /// <summary>
    /// Which line this is across rebuilds: the item, or the series and the occurrence. The lists
    /// are rebuilt whole on every change, and a view that wants to know which row is new (the
    /// motion spec's M2) compares these rather than the view models, which are new every time.
    /// </summary>
    public string Key => KeyOf(row);

    /// <summary>The <see cref="Key"/> a row would have, for code that holds the row and not its view model.</summary>
    public static string KeyOf(AgendaDayRow row) => string.Create(
        CultureInfo.InvariantCulture, $"{row.Item.SeriesId ?? row.Item.Id:N}/{row.RecurrenceId?.Value:O}");

    /// <summary>True for an occurrence of a rule, which the design marks with ↻ and nothing else.</summary>
    public bool IsRepeat => row.IsOccurrence;

    public bool HasDue => DueLabel.Length > 0;

    /// <summary>
    /// The when, as much of it as the surface needs: a clock in the day panel, a date and a clock
    /// in the cross-date list. Empty for a to-do that carries only a day, in the one place where
    /// the day is already the heading.
    /// </summary>
    public string DueLabel
    {
        get
        {
            CultureInfo culture = CultureInfo.CurrentCulture;
            if (scope == TodoRowScope.Day)
            {
                return row.At is { } at ? at.Value.ToString(AppStrings.TodoRowTimeFormat, culture) : string.Empty;
            }

            if (row.At is { } when)
            {
                return when.Value.ToString(AppStrings.TodoRowDateTimeFormat, culture);
            }

            return row.Falls is { } day
                ? day.Value.ToString(AppStrings.TodoRowDateFormat, culture)
                : string.Empty;
        }
    }

    public bool Overdue => row.IsOverdue(now);

    public bool IsEvent => row.Item.Kind == AgendaKind.Event;

    public bool IsTask => !IsEvent;

    /// <summary>An event with a start and no end: the whole day (<see cref="AgendaDay"/>'s model of it).</summary>
    public bool IsAllDay => IsEvent && row.Item.EndsAt is null;

    /// <summary>
    /// True when it is pinned to an hour. An all-day event and a to-do with only a day are not,
    /// which is what puts them in the timeline's "시각 없음" box rather than in the run of hours.
    /// </summary>
    public bool IsTimed => row.At is not null && !IsAllDay;

    /// <summary>The list's colour as <c>#rrggbb</c>: the check's ring and an event's dot (체크 링 색 = 리스트 색).</summary>
    public string ListColor { get; }

    /// <summary>"09:00", the clock it falls at, for the timeline's hour column. Empty when it has none.</summary>
    public string TimeLabel => IsTimed && row.At is { } at
        ? at.Value.ToString(AppStrings.TodoRowTimeFormat, CultureInfo.CurrentCulture)
        : string.Empty;

    /// <summary>An event's start in the day panel's left column: "09:00", or "종일".</summary>
    public string StartLabel => IsAllDay ? AppStrings.DeskEventAllDay : TimeLabel;

    /// <summary>An event's end under its start, "09:30". Empty for an all-day one.</summary>
    public string EndLabel => End is { } end
        ? end.ToString(AppStrings.TodoRowTimeFormat, CultureInfo.CurrentCulture)
        : string.Empty;

    /// <summary>An event's span spelled out, "오전 9:00 – 오전 9:30", or "하루 종일".</summary>
    public string RangeLabel
    {
        get
        {
            if (IsAllDay || row.At is not { } at)
            {
                return AppStrings.DeskEventAllDayLong;
            }

            CultureInfo culture = CultureInfo.CurrentCulture;
            string start = at.Value.ToString(AppStrings.DeskEventTimeFormat, culture);
            return End is { } end
                ? string.Format(culture, AppStrings.DeskEventRangeFormat, start, end.ToString(AppStrings.DeskEventTimeFormat, culture))
                : start;
        }
    }

    /// <summary>An event that is over, which the day panel and the timeline draw faded.</summary>
    public bool IsPast => IsEvent && (End ?? row.Falls?.Value.Date.AddDays(1)) is { } over && over <= now;

    /// <summary>
    /// When an event ends. An occurrence ends as long after its own start as the rule's first one
    /// did, since the item it carries is the series with the series' times.
    /// </summary>
    private DateTime? End =>
        IsEvent && row.At is { } at && row.Item.StartsAt is { } start && row.Item.EndsAt is { } end
            ? at.Value + (end.Value - start.Value)
            : null;

    /// <summary>
    /// Which list it is in. The built-in one has no stored name and contributes nothing: saying
    /// "내 할 일" on every row would be noise on the rows that are merely ordinary.
    /// </summary>
    public string NoteLabel => listName;

    public bool HasNoteLabel => listName.Length > 0;

    [RelayCommand]
    private Task Toggle() => onToggle(row);

    [RelayCommand]
    private Task Jump() => onJump(row);

    /// <summary>편집, from a row's menu. A repeating one asks first which of its days is meant.</summary>
    [RelayCommand]
    private Task Edit() => onEdit?.Invoke(row) ?? Task.CompletedTask;

    /// <summary>삭제, from a row's menu or its Delete key. A repeating one asks first.</summary>
    [RelayCommand]
    private Task Delete() => onDelete?.Invoke(row) ?? Task.CompletedTask;
}
