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

    /// <summary>Every date's files, once the library has been asked for (the 파일 view, design v2).</summary>
    private List<(DayFile File, FileItemViewModel Item)>? _library;

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

    /// <summary>The 파일 view: every date's files, newest date first, narrowed by <see cref="LibraryQuery"/>.</summary>
    public ObservableCollection<FileDateGroupViewModel> Groups { get; } = [];

    /// <summary>The 파일 view's search: a name or an extension.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLibraryQuery))]
    private string _libraryQuery = string.Empty;

    public bool HasLibraryQuery => LibraryQuery.Trim().Length > 0;

    /// <summary>How many files there are on every date, whatever the search shows.</summary>
    [ObservableProperty]
    private int _libraryCount;

    [ObservableProperty]
    private bool _isLibraryEmpty = true;

    public string LibraryEmptyText => HasLibraryQuery
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, Localization.AppStrings.DeskFilesNoMatchFormat, LibraryQuery.Trim())
        : Localization.AppStrings.DeskFilesNone;

    /// <summary>"+ 10/13에 추가": the files added from the 파일 view go to the day being looked at.</summary>
    public string LibraryAddText => string.Format(
        System.Globalization.CultureInfo.CurrentCulture,
        Localization.AppStrings.DeskFilesAddToFormat,
        Composition.LocalDates.ToDateOnly(_date).ToString(Localization.AppStrings.DeskNoticeDateFormat, System.Globalization.CultureInfo.CurrentCulture));

    [RelayCommand]
    private void ClearLibraryQuery() => LibraryQuery = string.Empty;

    partial void OnLibraryQueryChanged(string value)
    {
        RebuildGroups();
        OnPropertyChanged(nameof(LibraryEmptyText));
    }

    /// <summary>Every date's files, newest date first; for the month view, which draws its own lines.</summary>
    public async Task<IReadOnlyList<DayFile>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await _listFiles.ExecuteAllAsync(cancellationToken).ConfigureAwait(true);

    /// <summary>Reads every date's files into the 파일 view.</summary>
    public async Task LoadLibraryAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DayFile> files = await ListAllAsync(cancellationToken).ConfigureAwait(true);
        var library = new List<(DayFile File, FileItemViewModel Item)>(files.Count);
        foreach (DayFile file in files)
        {
            object? thumbnail = await TryLoadThumbnailAsync(file, cancellationToken).ConfigureAwait(true);
            library.Add((file, new FileItemViewModel(file, thumbnail, DeleteLibraryItemAsync, SaveItemAsync)));
        }

        _library = library;
        LibraryCount = library.Count;
        RebuildGroups();
    }

    private void RebuildGroups()
    {
        Groups.Clear();
        if (_library is null)
        {
            IsLibraryEmpty = true;
            return;
        }

        string query = LibraryQuery.Trim();
        LocalDate today = Composition.LocalDates.FromDateOnly(DateOnly.FromDateTime(DateTime.Now));
        foreach (IGrouping<LocalDate, (DayFile File, FileItemViewModel Item)> day in _library
                     .Where(entry => query.Length == 0 || entry.File.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                     .GroupBy(static entry => entry.File.LocalDate)
                     .OrderByDescending(static group => group.Key))
        {
            Groups.Add(new FileDateGroupViewModel(day.Key, day.Key == today, [.. day.Select(static entry => entry.Item)], OpenDay));
        }

        IsLibraryEmpty = Groups.Count == 0;
        OnPropertyChanged(nameof(LibraryEmptyText));
    }

    /// <summary>What a date heading in the 파일 view opens: that day, in the editor. Set by the shell.</summary>
    public Func<LocalDate, Task>? OpenDay { get; set; }

    private async Task DeleteLibraryItemAsync(FileItemViewModel item)
    {
        DayFileDeleteReceipt receipt = await _deleteFile.ExecuteAsync(item.Id).ConfigureAwait(true);
        if (receipt.Deleted)
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

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
        OnPropertyChanged(nameof(LibraryAddText));
        if (_library is not null)
        {
            await LoadLibraryAsync(cancellationToken).ConfigureAwait(true);
        }
        else
        {
            LibraryCount = await _listFiles.CountAllAsync(cancellationToken).ConfigureAwait(true);
        }
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
            if (_library is not null)
            {
                await LoadLibraryAsync(cancellationToken).ConfigureAwait(true);
            }
            else
            {
                LibraryCount++;
            }

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

        DayFile? file = _files.FirstOrDefault(candidate => candidate.Id == item.Id)
            ?? _library?.FirstOrDefault(entry => entry.File.Id == item.Id).File;
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
            await _picker.CompleteSaveAsync(destination).ConfigureAwait(true);
            item.SavedTo = destination;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            item.SaveFailed = true;
        }
    }

    /// <summary>
    /// The bytes behind a card, or null when this device does not hold them (still downloading, or
    /// lost). For the phone, which opens an attachment rather than only saving a copy of it.
    /// </summary>
    public async Task<byte[]?> ReadBytesAsync(FileItemViewModel item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_files.FirstOrDefault(candidate => candidate.Id == item.Id) is not { } file)
        {
            return null;
        }

        try
        {
            return await _assetStore.ReadAsync(file.RelativePath, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task DeleteItemAsync(FileItemViewModel item)
    {
        DayFileDeleteReceipt receipt = await _deleteFile.ExecuteAsync(item.Id).ConfigureAwait(true);
        if (receipt.Deleted)
        {
            Items.Remove(item);
            IsEmpty = Items.Count == 0;
            if (_library is not null)
            {
                await LoadLibraryAsync().ConfigureAwait(true);
            }
            else
            {
                LibraryCount = Math.Max(0, LibraryCount - 1);
            }
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

/// <summary>One date in the 파일 view: "10월 13일 (화) · 오늘", how many, and the cards.</summary>
public sealed partial class FileDateGroupViewModel
{
    private readonly Func<LocalDate, Task>? _open;

    public FileDateGroupViewModel(LocalDate date, bool isToday, IReadOnlyList<FileItemViewModel> files, Func<LocalDate, Task>? open)
    {
        Date = date;
        Files = files;
        _open = open;
        string heading = Composition.LocalDates.ToDateOnly(date)
            .ToString(Localization.AppStrings.DeskTabMenuDateFormat, Localization.LocalizationService.Instance.Culture);
        Heading = isToday ? string.Format(System.Globalization.CultureInfo.CurrentCulture, Localization.AppStrings.DeskFilesTodayFormat, heading) : heading;
        CountText = string.Format(System.Globalization.CultureInfo.CurrentCulture, Localization.AppStrings.DeskFilesCountFormat, files.Count);
    }

    public LocalDate Date { get; }

    public string Heading { get; }

    public string CountText { get; }

    public IReadOnlyList<FileItemViewModel> Files { get; }

    [RelayCommand]
    private Task Open() => _open?.Invoke(Date) ?? Task.CompletedTask;
}
