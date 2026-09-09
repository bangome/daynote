using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.Core.Domain;
using Daynote.Core.Files;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The 파일 tab: per-date attachments via the day-file use cases. "파일 · 이미지 추가" opens a multi-select
/// dialog (through <see cref="IFilePicker"/>) and streams each file into <see cref="AddDayFile"/>; image
/// files render a thumbnail decoded from the content-addressed store. Delete removes the reference.
/// </summary>
public sealed partial class FilesPanelViewModel : ObservableObject
{
    private const int ThumbnailDecodeWidth = 320;

    private readonly AddDayFile _addFile;
    private readonly ListDayFiles _listFiles;
    private readonly DeleteDayFile _deleteFile;
    private readonly IFileAssetStore _assetStore;
    private readonly IFilePicker _picker;
    private readonly IThumbnailLoader? _thumbnails;
    private LocalDate _date;

    /// <summary>
    /// The listed files as the repository returned them. The cards carry what the UI draws; saving
    /// needs the store path, which is not the card's business to know.
    /// </summary>
    private IReadOnlyList<DayFile> _files = [];

    /// <param name="thumbnails">Null shows the image badge instead of a decoded preview (tests, headless).</param>
    public FilesPanelViewModel(
        AddDayFile addFile,
        ListDayFiles listFiles,
        DeleteDayFile deleteFile,
        IFileAssetStore assetStore,
        IFilePicker picker,
        IThumbnailLoader? thumbnails = null)
    {
        _thumbnails = thumbnails;
        _addFile = addFile ?? throw new ArgumentNullException(nameof(addFile));
        _listFiles = listFiles ?? throw new ArgumentNullException(nameof(listFiles));
        _deleteFile = deleteFile ?? throw new ArgumentNullException(nameof(deleteFile));
        _assetStore = assetStore ?? throw new ArgumentNullException(nameof(assetStore));
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
    }

    public ObservableCollection<FileItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private bool _isEmpty = true;

    public async Task LoadForDateAsync(LocalDate date, CancellationToken cancellationToken = default)
    {
        _date = date;
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DayFile> files = await _listFiles.ExecuteAsync(_date, cancellationToken).ConfigureAwait(true);
        _files = files;
        Items.Clear();
        foreach (DayFile file in files)
        {
            object? thumbnail = await TryLoadThumbnailAsync(file, cancellationToken).ConfigureAwait(true);
            Items.Add(new FileItemViewModel(file, thumbnail, DeleteItemAsync, SaveItemAsync));
        }

        IsEmpty = Items.Count == 0;
    }

    [RelayCommand]
    private async Task AddFilesAsync()
    {
        IReadOnlyList<string> paths = await _picker.PickFilesAsync().ConfigureAwait(true);
        bool added = false;
        foreach (string path in paths)
        {
            try
            {
                await using FileStream stream = File.OpenRead(path);
                await _addFile.ExecuteAsync(_date, Path.GetFileName(path), stream).ConfigureAwait(true);
                added = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DayFileTooLargeException)
            {
                // Skip an unreadable or oversized file; the rest still import.
            }
        }

        if (added)
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Stores <paramref name="content"/> as a day file on the loaded date (body paste/drop path) and
    /// prepends its card. The display name is uniquified against the current list ("이름 (2).png") so a
    /// body link resolves to exactly this file. Returns null when the stream is unreadable or oversized.
    /// </summary>
    public async Task<DayFile?> AddFromStreamAsync(string displayName, Stream content, CancellationToken cancellationToken = default)
    {
        try
        {
            DayFile file = await _addFile.ExecuteAsync(_date, UniquifyName(displayName), content, cancellationToken).ConfigureAwait(true);
            DayFile available = file with { IsAvailable = true };
            _files = [available, .. _files];
            object? thumbnail = await TryLoadThumbnailAsync(available, cancellationToken).ConfigureAwait(true);
            Items.Insert(0, new FileItemViewModel(available, thumbnail, DeleteItemAsync, SaveItemAsync));
            IsEmpty = false;
            return available;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DayFileTooLargeException)
        {
            return null;
        }
    }

    /// <summary>Highlights the newest card matching <paramref name="displayName"/> and clears the rest.</summary>
    public void Highlight(string displayName)
    {
        // Items are newest-first (RefreshAsync lists newest-first; AddFromStreamAsync inserts at 0),
        // so the first match is the newest file with that name.
        bool found = false;
        foreach (FileItemViewModel item in Items)
        {
            bool target = !found && string.Equals(item.Name, displayName, StringComparison.Ordinal);
            item.IsHighlighted = target;
            found |= target;
        }
    }

    /// <summary>Appends " (2)", " (3)"… before the extension until the name is unused in this date's list.</summary>
    private string UniquifyName(string displayName)
    {
        if (Items.All(item => !string.Equals(item.Name, displayName, StringComparison.Ordinal)))
        {
            return displayName;
        }

        int dot = displayName.LastIndexOf('.');
        string stem = dot > 0 ? displayName[..dot] : displayName;
        string extension = dot > 0 ? displayName[dot..] : string.Empty;
        for (int suffix = 2; ; suffix++)
        {
            string candidate = $"{stem} ({suffix}){extension}";
            if (Items.All(item => !string.Equals(item.Name, candidate, StringComparison.Ordinal)))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Writes a copy of an attachment wherever the user points. The bytes live in the content-addressed
    /// store under a hashed name, so "open the folder" would show them nothing they could use; this is
    /// how an attachment gets back out of the app.
    /// </summary>
    /// <remarks>
    /// Failures are reported on the card rather than thrown. The two that actually happen — the asset
    /// is gone (a store reconciled away a file whose row survived) and the destination cannot be
    /// written (a read-only stick, a folder someone else owns) — are both things the user can see and
    /// act on, and neither is worth losing the panel over.
    /// </remarks>
    private async Task SaveItemAsync(FileItemViewModel item)
    {
        item.SaveFailed = false;

        DayFile? file = _files.FirstOrDefault(candidate => candidate.Id == item.Id);
        if (file is null)
        {
            item.SaveFailed = true;
            return;
        }

        byte[]? bytes;
        try
        {
            bytes = await _assetStore.ReadAsync(file.RelativePath).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            bytes = null;
        }

        if (bytes is null)
        {
            item.SaveFailed = true;
            return;
        }

        // Asked after the bytes are in hand: a dialog that closes and then says the file was missing
        // is worse than one that never opens.
        string? destination = await _picker.PickSavePathAsync(item.Name).ConfigureAwait(true);
        if (string.IsNullOrEmpty(destination))
        {
            return;
        }

        try
        {
            await File.WriteAllBytesAsync(destination, bytes).ConfigureAwait(true);
            item.SavedTo = destination;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            item.SaveFailed = true;
        }
    }

    private async Task DeleteItemAsync(FileItemViewModel item)
    {
        DayFileDeleteReceipt receipt = await _deleteFile.ExecuteAsync(item.Id).ConfigureAwait(true);
        if (receipt.Deleted)
        {
            Items.Remove(item);
            IsEmpty = Items.Count == 0;
        }
    }

    private async Task<object?> TryLoadThumbnailAsync(DayFile file, CancellationToken cancellationToken)
    {
        if (_thumbnails is null || !file.IsImage || !file.IsAvailable)
        {
            return null;
        }

        try
        {
            byte[]? bytes = await _assetStore.ReadAsync(file.RelativePath, cancellationToken).ConfigureAwait(true);
            return bytes is null
                ? null
                : await _thumbnails.LoadAsync(bytes, ThumbnailDecodeWidth, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }
}
