using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Localization;
using Daynote.Core.Agenda;

namespace Daynote.Desktop.ViewModels;

/// <summary>One to-do in the menu bar popover (menu bar design §01, B1–B7).</summary>
/// <remarks>
/// Ticking one leaves it where it is, ringed in and struck through, rather than dropping it from the
/// list: the desktop variant of M8 settles the reorder on the next refresh, and a row that vanished
/// under the pointer would be the only proof it was ticked. The next opening reads it as done.
/// </remarks>
public sealed partial class MenuBarTodoRowViewModel : ObservableObject
{
    private readonly Func<MenuBarTodoRowViewModel, Task> onToggle;

    public MenuBarTodoRowViewModel(
        AgendaDayRow row,
        int tone,
        bool isJustAdded,
        DateTime now,
        Func<MenuBarTodoRowViewModel, Task> onToggle)
    {
        this.onToggle = onToggle ?? throw new ArgumentNullException(nameof(onToggle));
        Row = row;
        Tone = tone;
        IsJustAdded = isJustAdded;
        IsOverdue = row.IsOverdue(now);
        IsDone = row.IsDone;
    }

    /// <summary>The row as it now stands; replaced after a tick, which may have made an override.</summary>
    public AgendaDayRow Row { get; internal set; }

    public string Title => Row.Item.Title;

    /// <summary>
    /// Which list colour the check ring takes: 0 the built-in list (the sun), then the others in
    /// turn. The schema has no colour column yet (docs/TODOS.md §12), so this is positional.
    /// </summary>
    public int Tone { get; }

    public bool IsToneSun => Tone == 0;

    public bool IsToneIndigo => Tone == 1;

    public bool IsToneGreen => Tone == 2;

    public string TimeText => Row.At is { } at
        ? at.Value.ToString(AppStrings.TodoRowTimeFormat, CultureInfo.CurrentCulture)
        : string.Empty;

    public bool HasTime => Row.At is not null;

    /// <summary>
    /// Past its clock and not ticked. The desktop never rings (design §99), so the red time here is
    /// the reminder.
    /// </summary>
    public bool IsOverdue { get; }

    /// <summary>True for an occurrence of a rule, marked ↻ as in the day panel.</summary>
    public bool IsRepeat => Row.IsOccurrence;

    /// <summary>Made from this popover since it opened: top of the list, tinted, labelled 방금.</summary>
    public bool IsJustAdded { get; }

    [ObservableProperty]
    public partial bool IsDone { get; set; }

    /// <summary>Ticked from this popover: it keeps the sun's wash, as a new row does (B7).</summary>
    [ObservableProperty]
    public partial bool IsTickedHere { get; set; }

    /// <summary>Red only while it is still owed; ticked, it is just a time.</summary>
    public bool ShowsOverdue => IsOverdue && !IsDone;

    /// <summary>The row the user just acted on, here: made, or ticked.</summary>
    public bool IsHighlighted => IsJustAdded || IsTickedHere;

    partial void OnIsDoneChanged(bool value) => OnPropertyChanged(nameof(ShowsOverdue));

    partial void OnIsTickedHereChanged(bool value) => OnPropertyChanged(nameof(IsHighlighted));

    [RelayCommand]
    private Task Toggle() => onToggle(this);
}
