using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Shell.Product;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;

namespace Daynote.Mobile.ViewModels;

/// <summary>
/// What a note made: the count in the toolbar, the row that rises for a moment after 만들기, and
/// the sheet behind the count (phone §02).
/// </summary>
/// <remarks>
/// The chip the body draws around the @ span is not enough on its own. It sits right beside the
/// caret, so it reads as "the characters changed", and it says nothing about which date or which
/// list the thing went to.
/// <para>
/// So the confirmation appears where the object actually lives — the note's own collection of
/// items — rather than in a toast. A toast is a claim that something happened somewhere else; a
/// row rising in the place that holds it is the thing itself arriving. It settles into the count
/// after a moment, which is also how it stops being in the way.
/// </para>
/// </remarks>
public sealed partial class MobileShellViewModel
{
    /// <summary>Everything this note made with @, any date, newest-dated last.</summary>
    public ObservableCollection<TodoItemViewModel> NoteItems { get; } = [];

    public bool HasNoteItems => NoteItems.Count > 0;

    /// <summary>"이 노트의 항목 3" — the toolbar's permanent seat for them.</summary>
    public string NoteItemCountText => MobileStrings.Format("MobileNoteItemsCount", NoteItems.Count);

    [ObservableProperty]
    private bool _isNoteItemsSheetOpen;

    /// <summary>The one just made, for as long as it is worth showing. Null the rest of the time.</summary>
    [ObservableProperty]
    private TodoItemViewModel? _justMade;

    /// <summary>
    /// True when what was just made landed on a day other than the one being written. Then the
    /// row says where it went and offers to go there, because that is the case where a user
    /// would otherwise look for it on today and not find it.
    /// </summary>
    [ObservableProperty]
    private bool _isJustMadeElsewhere;

    /// <summary>Either 방금, or "10/8에 추가됨".</summary>
    [ObservableProperty]
    private string _justMadeWhenText = string.Empty;

    /// <summary>The day the just-made item went to, for 보기.</summary>
    private LocalDate _justMadeDate;

    /// <summary>How long the row stays up. 2.5 seconds: long enough to read, short enough not to nag.</summary>
    public static readonly TimeSpan JustMadeFlash = TimeSpan.FromSeconds(2.5);

    /// <summary>The wait itself, as a seam: a test should not spend two and a half seconds here.</summary>
    public Func<TimeSpan, Task> FlashDelay { get; set; } = wait => Task.Delay(wait);

    /// <summary>Which flash is current; an older one must not clear a newer row.</summary>
    private int _flashToken;

    [RelayCommand]
    private void OpenNoteItems() => IsNoteItemsSheetOpen = NoteItems.Count > 0;

    [RelayCommand]
    private void CloseNoteItems() => IsNoteItemsSheetOpen = false;

    /// <summary>보기: the day it went to, which is the one place it can be seen in context.</summary>
    [RelayCommand]
    private async Task ViewJustMade()
    {
        JustMade = null;
        await CloseEditorAsync().ConfigureAwait(true);
        await SelectDateAsync(_justMadeDate).ConfigureAwait(true);
    }

    partial void OnIsNoteItemsSheetOpenChanged(bool value) => OnPropertyChanged(nameof(ShowDock));

    /// <summary>
    /// Rebuilds the note's collection out of the items already read for the lists. Everything the
    /// @ command made in this note, whatever date it landed on and whether or not it is done — it
    /// is a record of what this note produced, not a queue.
    /// </summary>
    private void RebuildNoteItems(IReadOnlyList<AgendaItem> items, DateTime now)
    {
        NoteItems.Clear();
        if (Notes.SelectedTab is { IsProjection: false, Id: { } noteId })
        {
            foreach (AgendaItem item in items
                .Where(item => item.SourceNoteId == noteId.Value)
                .OrderBy(static item => item.Anchor?.Value ?? DateTime.MaxValue)
                .ThenBy(static item => item.Title, StringComparer.CurrentCulture))
            {
                NoteItems.Add(Todo.Row(Row(item), now));
            }
        }

        OnPropertyChanged(nameof(HasNoteItems));
        OnPropertyChanged(nameof(NoteItemCountText));
        if (NoteItems.Count == 0)
        {
            IsNoteItemsSheetOpen = false;
        }
    }

    /// <summary>
    /// Raises the row for the item just made, then lets it settle into the count.
    /// </summary>
    private async Task FlashJustMadeAsync(AgendaItem made, DateTime now)
    {
        _justMadeDate = LocalDates.FromDateOnly(
            DateOnly.FromDateTime(made.Anchor?.Value ?? now));
        IsJustMadeElsewhere = _justMadeDate != SelectedDate;
        JustMadeWhenText = IsJustMadeElsewhere
            ? MobileStrings.Format(
                "MobileNoteItemsAddedTo",
                LocalDates.ToDateOnly(_justMadeDate).ToString(
                    MobileStrings.Get("MobileNoteItemsAddedDateFormat"),
                    Daynote.App.Localization.LocalizationService.Instance.Culture))
            : MobileStrings.Get("MobileJustNow");
        JustMade = Todo.Row(Row(made), now);

        int token = ++_flashToken;
        await FlashDelay(JustMadeFlash).ConfigureAwait(true);
        if (_flashToken == token)
        {
            JustMade = null;
        }
    }

    /// <summary>One item as a row: its own day, and a clock only if it carries one.</summary>
    private static AgendaDayRow Row(AgendaItem item) =>
        new(item, RecurrenceId: null, item.HasClockTime ? item.Anchor : null);
}
