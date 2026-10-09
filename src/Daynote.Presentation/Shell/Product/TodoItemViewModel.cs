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

    public TodoItemViewModel(
        AgendaDayRow row,
        TodoRowScope scope,
        string listName,
        DateTime now,
        Func<AgendaDayRow, Task> onToggle,
        Func<AgendaDayRow, Task> onJump)
    {
        this.row = row;
        this.scope = scope;
        this.listName = listName;
        this.now = now;
        this.onToggle = onToggle ?? throw new ArgumentNullException(nameof(onToggle));
        this.onJump = onJump ?? throw new ArgumentNullException(nameof(onJump));
    }

    public bool Checked => row.IsDone;

    /// <summary>The day it falls on, so a view can keep only one day's rows.</summary>
    public LocalDate Date => LocalDates.FromDateOnly(
        DateOnly.FromDateTime(row.At?.Value ?? row.Item.Anchor?.Value ?? now));

    public string Text => row.Item.Title;

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

            return row.Item.Anchor is { } day
                ? day.Value.ToString(AppStrings.TodoRowDateFormat, culture)
                : string.Empty;
        }
    }

    public bool Overdue => row.IsOverdue(now);

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
}
