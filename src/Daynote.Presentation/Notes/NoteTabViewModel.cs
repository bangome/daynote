using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Daynote.App.Localization;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;

namespace Daynote.App.Notes;

/// <summary>Save-state cue for a note tab and its mirrored sidebar row (DESIGN Section 5).</summary>
public enum NoteSaveState
{
    Clean,
    Dirty,
    Saving,
    Error,
}

/// <summary>
/// One note in the current date's ordered set. Wraps an immutable <see cref="NoteId"/> plus the
/// mutable editing buffer. A projection tab is the unpersisted "Note 1" that materializes on first
/// edit; its <see cref="Id"/> is the identity it will keep once persisted.
/// </summary>
public sealed partial class NoteTabViewModel : ObservableObject, ILanguageAware
{
    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private NoteSaveState _saveState;

    /// <summary>Mirrors the workspace selection so the tab strip can render the selected marker and reveal its close command.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Favorite flag mirrored from the persisted note; toggled through <see cref="Core.Notes.ToggleNoteFavorite"/>.</summary>
    [ObservableProperty]
    private bool _isFavorite;

    /// <summary>First non-empty body line, shown as the note-list preview (DESIGN redesign note rows).</summary>
    [ObservableProperty]
    private string _preview = string.Empty;

    /// <summary>Note tags mirrored from the persisted note; edited through <see cref="Core.Notes.SetNoteTags"/>.</summary>
    public ObservableCollection<string> Tags { get; } = [];

    public bool HasTags => Tags.Count > 0;

    // Title-bearing accessible names. These used to be XAML StringFormat bindings with the Korean
    // suffix baked into the markup; deriving them here lets one binding follow both the title and
    // the active language.

    /// <summary>Accessible name and tooltip for this tab's close button.</summary>
    public string CloseLabel => Format(AppStrings.CloseNoteFormat);

    /// <summary>Accessible name and tooltip for the sidebar's "move up" button.</summary>
    public string MoveUpLabel => Format(AppStrings.MoveNoteUpFormat);

    /// <summary>Accessible name and tooltip for the sidebar's "move down" button.</summary>
    public string MoveDownLabel => Format(AppStrings.MoveNoteDownFormat);

    private string Format(string template) => string.Format(CultureInfo.CurrentCulture, template, Title);

    partial void OnTitleChanged(string value) => RaiseDerivedLabels();

    void ILanguageAware.OnLanguageChanged() => RaiseDerivedLabels();

    private void RaiseDerivedLabels()
    {
        OnPropertyChanged(nameof(CloseLabel));
        OnPropertyChanged(nameof(MoveUpLabel));
        OnPropertyChanged(nameof(MoveDownLabel));
    }

    /// <summary>Recomputes the preview line from the current body (called after edits and reloads).</summary>
    public void RefreshPreview()
    {
        string? line = null;
        foreach (string candidate in (Body ?? string.Empty).Split('\n'))
        {
            if (candidate.Trim().Length > 0)
            {
                line = candidate.Trim();
                break;
            }
        }

        Preview = line ?? string.Empty;
    }

    /// <summary>Replaces the mirrored tag set and raises <see cref="HasTags"/>.</summary>
    public void SetTags(IReadOnlyList<string> tags)
    {
        Tags.Clear();
        foreach (string tag in tags)
        {
            Tags.Add(tag);
        }

        OnPropertyChanged(nameof(HasTags));
    }

    public NoteTabViewModel(
        NoteId id,
        LocalDate localDate,
        int sortOrder,
        string title,
        string body,
        bool hasCustomTitle,
        bool isProjection,
        int revision)
    {
        Id = id;
        LocalDate = localDate;
        SortOrder = sortOrder;
        _title = title;
        Body = body;
        HasCustomTitle = hasCustomTitle;
        IsProjection = isProjection;
        Revision = revision;
        LocalizationService.Instance.Observe(this);
    }

    public NoteId Id { get; }

    public LocalDate LocalDate { get; }

    public int SortOrder { get; private set; }

    public int DisplayNumber => SortOrder + 1;

    public string Body { get; set; }

    public bool HasCustomTitle { get; set; }

    public bool IsProjection { get; private set; }

    public int Revision { get; set; }

    public static NoteTabViewModel FromNote(Note note, int revision, NoteId projectionId)
    {
        ArgumentNullException.ThrowIfNull(note);
        NoteId id = note.Id ?? projectionId;
        var tab = new NoteTabViewModel(
            id,
            note.LocalDate,
            note.SortOrder,
            note.Title,
            note.Body,
            note.HasCustomTitle,
            note.IsProjection,
            revision)
        {
            IsFavorite = note.IsFavorite,
        };
        tab.SetTags(note.Tags);
        tab.RefreshPreview();
        return tab;
    }

    public void MarkPersisted(int revision)
    {
        Revision = revision;
        IsProjection = false;
        SaveState = NoteSaveState.Clean;
    }
}
