using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.Core.Domain;

namespace Daynote.App.Shell.Product;

/// <summary>
/// Common marker for a single row in the timeline's flat item list, letting one <c>ItemsControl</c>
/// hold both date-boundary headers and note cards while WPF picks a template per concrete type.
/// </summary>
public abstract class TimelineRow
{
}

/// <summary>
/// A prominent date boundary separating one day's notes from the next in the timeline. Carries the
/// localized day heading and the per-date note count.
/// </summary>
public sealed class TimelineDateHeaderRow : TimelineRow
{
    public TimelineDateHeaderRow(LocalDate date, int noteCount)
    {
        Date = date;
        Heading = LocalDates.DisplayDayHeading(date);
        CountText = string.Format(CultureInfo.CurrentCulture, AppStrings.NoteCountFormat, noteCount);
    }

    public LocalDate Date { get; }

    public string Heading { get; }

    public string CountText { get; }
}

/// <summary>
/// A read-only note card in the timeline. Shows a truncated body with an expand/collapse toggle and
/// opens the note in the normal editor (on its own date) when activated.
/// </summary>
[ObservableObject]
public sealed partial class TimelineNoteRow : TimelineRow
{
    private readonly Func<Guid, LocalDate, Task> _open;

    public TimelineNoteRow(
        Guid id,
        LocalDate date,
        string title,
        bool isFavorite,
        string fullBody,
        string summary,
        bool isTruncated,
        Func<Guid, LocalDate, Task> open)
    {
        Id = id;
        Date = date;
        Title = title;
        IsFavorite = isFavorite;
        FullBody = fullBody;
        Summary = summary;
        IsTruncated = isTruncated;
        _open = open ?? throw new ArgumentNullException(nameof(open));
    }

    public Guid Id { get; }

    public LocalDate Date { get; }

    public string Title { get; }

    public bool IsFavorite { get; }

    public string FullBody { get; }

    public string Summary { get; }

    public bool IsTruncated { get; }

    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>The body actually shown: the full body when expanded or short, the summary otherwise.</summary>
    public string DisplayBody => IsExpanded || !IsTruncated ? FullBody : Summary;

    /// <summary>Localized toggle caption; re-read on both expand and language changes.</summary>
    public string ExpandLabel => IsExpanded ? AppStrings.TimelineCollapse : AppStrings.TimelineExpand;

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayBody));
        OnPropertyChanged(nameof(ExpandLabel));
    }

    /// <summary>Called by the owning view model on a language switch to refresh catalog-derived text.</summary>
    public void OnLanguageChanged() => OnPropertyChanged(nameof(ExpandLabel));

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private Task Open() => _open(Id, Date);
}
