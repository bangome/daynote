using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.Core.Files;

namespace Daynote.App.Shell.Product;

/// <summary>
/// One attachment card in the 파일 tab. Image files with available bytes show a thumbnail band; other
/// files show an extension badge. Size labels follow the design's fmtSize (MB / KB / B).
/// </summary>
public sealed partial class FileItemViewModel : ObservableObject
{
    private readonly Func<FileItemViewModel, Task> _onDelete;
    private readonly Func<FileItemViewModel, Task> _onSave;

    public FileItemViewModel(
        DayFile file,
        object? thumbnail,
        Func<FileItemViewModel, Task> onDelete,
        Func<FileItemViewModel, Task> onSave)
    {
        Id = file.Id;
        Name = file.DisplayName;
        Ext = ExtensionOf(file.DisplayName);
        IsImage = file.IsImage;
        IsAvailable = file.IsAvailable;
        Thumbnail = thumbnail;
        SizeLabel = string.Create(CultureInfo.CurrentCulture, $"{FormatSize(file.ByteLength)} · {Ext}");
        _onDelete = onDelete;
        _onSave = onSave;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string Ext { get; }

    public bool IsImage { get; }

    /// <summary>
    /// Whether this device holds the bytes.
    /// </summary>
    /// <remarks>
    /// False is an ordinary state once cloud sync is on, not an error: another device attached the
    /// file, the row arrived on the pull, and the object is still coming (docs/CLOUD_SYNC.md §5.5).
    /// The card has to say so, or a still-downloading attachment looks exactly like a ready one
    /// until the user double-clicks it and the save fails for no visible reason.
    /// </remarks>
    public bool IsAvailable { get; }

    /// <summary>Drives the "내려받는 중" line and hides the actions that need the bytes.</summary>
    public bool IsAwaitingDownload => !IsAvailable;

    public object? Thumbnail { get; }

    public bool HasThumbnail => Thumbnail is not null;

    /// <summary>Image file whose bytes are missing: show the image badge instead of a thumbnail.</summary>
    public bool ShowImageBadge => IsImage && Thumbnail is null;

    /// <summary>Non-image file: show the extension badge.</summary>
    public bool ShowDocBadge => !IsImage;

    public string SizeLabel { get; }

    /// <summary>True when a body file-link click targeted this card; drives the accent border.</summary>
    [ObservableProperty]
    private bool _isHighlighted;

    /// <summary>Where the last copy went, or null. Shown on the card so the save is not silent.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WasSaved))]
    private string? _savedTo;

    public bool WasSaved => !string.IsNullOrEmpty(SavedTo);

    /// <summary>The bytes could not be read, or the destination could not be written.</summary>
    [ObservableProperty]
    private bool _saveFailed;

    [RelayCommand]
    private Task Delete() => _onDelete(this);

    /// <summary>Double-clicking the card writes a copy wherever the user points.</summary>
    /// <summary>
    /// Saves a copy elsewhere. Disabled while the bytes are still coming down, because there is
    /// nothing to copy — better a greyed action than a save that fails after the file dialog.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task Save() => _onSave(this);

    private static string ExtensionOf(string name)
    {
        int dot = name.LastIndexOf('.');
        string ext = dot >= 0 && dot < name.Length - 1 ? name[(dot + 1)..] : string.Empty;
        return ext.ToUpperInvariant() is { Length: > 4 } trimmed ? trimmed[..4] : ext.ToUpperInvariant();
    }

    /// <summary>Design fmtSize: &gt;1MB → "X.X MB"; &gt;1KB → "N KB"; else "N B".</summary>
    public static string FormatSize(long bytes) => bytes > 1_048_576
        ? string.Create(CultureInfo.CurrentCulture, $"{bytes / 1_048_576.0:0.0} MB")
        : bytes > 1024
            ? string.Create(CultureInfo.CurrentCulture, $"{(int)Math.Round(bytes / 1024.0)} KB")
            : string.Create(CultureInfo.CurrentCulture, $"{bytes} B");
}
