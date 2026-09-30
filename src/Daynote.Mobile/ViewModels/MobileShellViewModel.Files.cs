using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Account;
using Daynote.App.Localization;
using Daynote.App.Shell.Product;
using Daynote.Core.Files;
using Daynote.Core.Sync;

namespace Daynote.Mobile.ViewModels;

/// <summary>
/// The selected day's attachments on a phone: the rows on the day screen and the editor, attaching
/// from the photo library or files, opening one, saving a copy, and deleting.
/// </summary>
/// <remarks>
/// <para>
/// Everything that touches the store goes through the shared <see cref="FilesPanelViewModel"/>, the
/// same one the desktop's 파일 panel uses, so an attachment added here is written, queued for sync
/// and tombstoned on delete exactly as one added on the desktop is. What this adds is what a phone
/// needs around it: rows that say whether the file has reached the cloud, an image viewer, a hand-off
/// to another app for everything else, and a confirmation before a delete that reaches every device.
/// </para>
/// <para>
/// What the account's plan allows is stated, never sold. A phone app may not point at a purchase
/// made elsewhere (App Store 3.1.1, Play payments policy), so an account whose files do not sync is
/// told so in one neutral line, and attaching keeps working locally just as it does on the desktop.
/// </para>
/// </remarks>
public sealed partial class MobileShellViewModel
{
    /// <summary>Wide enough for a full-screen image on the densest phone, small enough to decode quickly.</summary>
    private const int ViewerDecodeWidth = 2048;

    private IFilePicker? _filePicker;
    private IThumbnailLoader? _thumbnailLoader;

    /// <summary>The ids of the day's attachments still waiting to be pushed, as the outbox last said.</summary>
    private IReadOnlySet<string> _pendingUploads = new HashSet<string>();

    /// <summary>The day's attachments as the phone lists them, newest first.</summary>
    public ObservableCollection<MobileFileRowViewModel> DayFiles { get; } = [];

    public bool HasDayFiles => DayFiles.Count > 0;

    /// <summary>
    /// Hands an attachment to another app. Set by composition from the head, or Avalonia's launcher;
    /// null means there is no way out but saving a copy.
    /// </summary>
    public Func<string, byte[], Task<bool>>? OpenFileExternally { get; set; }

    /// <summary>Reads which attachments are still queued for upload. Null in a build without sync.</summary>
    public Func<CancellationToken, Task<IReadOnlySet<string>>>? PendingFileUploads { get; set; }

    /// <summary>A one-off line after an attach or an open that did not go as asked: too large, unreadable.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilesNotice))]
    private string? _filesNotice;

    public bool HasFilesNotice => !string.IsNullOrEmpty(FilesNotice);

    /// <summary>The sheet that asks "사진 or 파일".</summary>
    [ObservableProperty]
    private bool _isAttachSheetOpen;

    /// <summary>The row whose menu (save a copy, delete) is up, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFileMenuOpen))]
    [NotifyPropertyChangedFor(nameof(FileMenuTitle))]
    private MobileFileRowViewModel? _menuFile;

    public bool IsFileMenuOpen => MenuFile is not null;

    public string FileMenuTitle => MenuFile?.Name ?? string.Empty;

    /// <summary>The menu has turned into the delete question.</summary>
    [ObservableProperty]
    private bool _isConfirmingDelete;

    /// <summary>The image on screen in the viewer, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImageViewerOpen))]
    [NotifyPropertyChangedFor(nameof(ViewerTitle))]
    private MobileFileRowViewModel? _viewerFile;

    /// <summary>The decoded picture the viewer shows; opaque here, a bitmap to the view.</summary>
    [ObservableProperty]
    private object? _viewerImage;

    public bool IsImageViewerOpen => ViewerFile is not null;

    public string ViewerTitle => ViewerFile?.Name ?? string.Empty;

    /// <summary>Whether any of the attachment layers covers the tabs.</summary>
    private bool IsFileLayerOpen => IsAttachSheetOpen || IsFileMenuOpen || IsImageViewerOpen;

    partial void OnIsAttachSheetOpenChanged(bool value) => OnPropertyChanged(nameof(ShowDock));

    partial void OnMenuFileChanged(MobileFileRowViewModel? value)
    {
        IsConfirmingDelete = false;
        OnPropertyChanged(nameof(ShowDock));
    }

    partial void OnViewerFileChanged(MobileFileRowViewModel? value)
    {
        if (value is null)
        {
            ViewerImage = null;
        }

        OnPropertyChanged(nameof(ShowDock));
    }

    /// <summary>
    /// Why this day's files will not reach the other devices, or empty when they will. Neutral on
    /// purpose: it says what is true and nothing about how to change it.
    /// </summary>
    public string FilesSyncNote => !HasDayFiles ? string.Empty : Account switch
    {
        // A build with no sync at all has nothing to say about syncing.
        null => string.Empty,
        { IsSignedIn: false } => MobileStrings.Get("MobileFilesLocalOnly"),
        { Status.Kind: SyncStatusKind.Unpaid } or { Entitlement.State: EntitlementState.Expired } =>
            MobileStrings.Get("MobileFilesNotSynced"),
        { Entitlement: { QuotaBytes: long quota, UsedBytes: long used } } when quota > 0 && used >= quota =>
            MobileStrings.Get("MobileFilesQuotaFull"),
        _ => string.Empty,
    };

    public bool HasFilesSyncNote => FilesSyncNote.Length > 0;

    /// <summary>
    /// Whether the rows may say "업로드 대기": signed in, and file sync not withheld. When it is
    /// withheld the note above says so once, instead of every row saying it again.
    /// </summary>
    private bool ShowsUploadState => Account is { IsSignedIn: true } && !HasFilesSyncNote;

    /// <summary>Hooks the rows to the shared panel. Called once from the constructor.</summary>
    private void AttachFiles(IFilePicker picker, IThumbnailLoader thumbnails)
    {
        _filePicker = picker;
        _thumbnailLoader = thumbnails;
        Files.Items.CollectionChanged += (_, _) => RebuildFileRows();
    }

    /// <summary>
    /// Mirrors the panel's cards as rows, keeping the row of a card that is still there so a refresh
    /// does not reset what a row is showing.
    /// </summary>
    private void RebuildFileRows()
    {
        var existing = DayFiles.ToDictionary(row => row.Item);
        DayFiles.Clear();
        foreach (FileItemViewModel item in Files.Items)
        {
            DayFiles.Add(existing.TryGetValue(item, out MobileFileRowViewModel? row)
                ? row
                : new MobileFileRowViewModel(item, OpenFileAsync, ShowFileMenu));
        }

        ApplyUploadState();
        OnPropertyChanged(nameof(HasDayFiles));
        RefreshFilesSyncNote();
    }

    private void RefreshFilesSyncNote()
    {
        OnPropertyChanged(nameof(FilesSyncNote));
        OnPropertyChanged(nameof(HasFilesSyncNote));
        ApplyUploadState();
    }

    private void ApplyUploadState()
    {
        bool show = ShowsUploadState;
        foreach (MobileFileRowViewModel row in DayFiles)
        {
            row.IsUploadPending = show && _pendingUploads.Contains(row.Item.Id.ToString("D"));
        }
    }

    /// <summary>Re-reads the outbox, so a row that has reached the cloud stops saying it has not.</summary>
    private async Task RefreshPendingUploadsAsync(CancellationToken cancellationToken = default)
    {
        if (PendingFileUploads is not { } read)
        {
            return;
        }

        try
        {
            _pendingUploads = await read(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            System.Diagnostics.Trace.TraceError(exception.ToString());
        }

        ApplyUploadState();
    }

    /// <summary>Loads the day's files and what of them is still waiting to upload.</summary>
    private async Task LoadDayFilesAsync(Core.Domain.LocalDate date, CancellationToken cancellationToken = default)
    {
        await Files.LoadForDateAsync(date, cancellationToken).ConfigureAwait(true);
        await RefreshPendingUploadsAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    private void OpenAttachSheet()
    {
        FilesNotice = null;
        IsAttachSheetOpen = true;
    }

    [RelayCommand]
    private void CloseAttachSheet() => IsAttachSheetOpen = false;

    [RelayCommand]
    private Task AttachPhotos() => AttachAsync(photos: true);

    [RelayCommand]
    private Task AttachFilesFromPicker() => AttachAsync(photos: false);

    /// <summary>
    /// Picks, checks the size, and stores each file on the selected day through the panel, which
    /// writes it the way the desktop does and so queues it for sync.
    /// </summary>
    private async Task AttachAsync(bool photos)
    {
        IsAttachSheetOpen = false;
        FilesNotice = null;
        if (_filePicker is not { } picker)
        {
            return;
        }

        IReadOnlyList<string> paths = photos && picker is IPhotoPicker photoPicker
            ? await photoPicker.PickPhotosAsync().ConfigureAwait(true)
            : await picker.PickFilesAsync().ConfigureAwait(true);

        int added = 0, tooLarge = 0, failed = 0;
        foreach (string path in paths)
        {
            try
            {
                if (new FileInfo(path).Length > FileCapturePolicy.MaxFileBytes)
                {
                    tooLarge++;
                    continue;
                }

                await using (FileStream stream = File.OpenRead(path))
                {
                    if (await Files.AddFromStreamAsync(Path.GetFileName(path), stream).ConfigureAwait(true) is null)
                    {
                        failed++;
                    }
                    else
                    {
                        added++;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failed++;
            }
            finally
            {
                DiscardStagedCopy(picker, path);
            }
        }

        FilesNotice = tooLarge > 0
            ? MobileStrings.Format("MobileFilesTooLargeFormat", FileCapturePolicy.MaxFileBytes / (1024 * 1024))
            : failed > 0 ? MobileStrings.Get("MobileFilesAddFailed") : null;

        if (added > 0)
        {
            await RefreshPendingUploadsAsync().ConfigureAwait(true);
            _syncScheduler?.NotifySaved();
        }
    }

    /// <summary>A copy the picker staged is the app's own; once the store has the bytes it goes.</summary>
    private static void DiscardStagedCopy(IFilePicker picker, string path)
    {
        if (picker is not IPhotoPicker { StagingDirectory: { Length: > 0 } staging })
        {
            return;
        }

        string full = Path.GetFullPath(path);
        if (!full.StartsWith(Path.GetFullPath(staging), StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            File.Delete(full);
            if (Path.GetDirectoryName(full) is { } slot && !string.Equals(Path.GetFullPath(slot), Path.GetFullPath(staging), StringComparison.Ordinal))
            {
                Directory.Delete(slot, recursive: false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// A tap on a row: a picture opens in the viewer, anything else in another app, and a file with
    /// no app to take it is offered as a copy to save.
    /// </summary>
    private async Task OpenFileAsync(MobileFileRowViewModel row)
    {
        // A long press raises the menu while the finger is still down, and lifting it then taps the
        // row: that tap is the end of the press, not an open.
        if (IsFileMenuOpen)
        {
            return;
        }

        FilesNotice = null;
        if (row.Item.IsAwaitingDownload)
        {
            FilesNotice = MobileStrings.Get("MobileFilesStillDownloading");
            return;
        }

        byte[]? bytes = await Files.ReadBytesAsync(row.Item).ConfigureAwait(true);
        if (bytes is null)
        {
            FilesNotice = MobileStrings.Get("MobileFilesOpenFailed");
            return;
        }

        if (row.Item.IsImage && _thumbnailLoader is { } loader)
        {
            object? image = null;
            try
            {
                image = await loader.LoadAsync(bytes, ViewerDecodeWidth, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not OperationCanceledException)
            {
                // Not a picture this platform can decode: hand it on like any other file.
                System.Diagnostics.Trace.TraceWarning(exception.Message);
            }

            if (image is not null)
            {
                ViewerImage = image;
                ViewerFile = row;
                return;
            }
        }

        bool opened = false;
        if (OpenFileExternally is { } open)
        {
            try
            {
                opened = await open(row.Name, bytes).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                System.Diagnostics.Trace.TraceError(exception.ToString());
            }
        }

        if (!opened)
        {
            FilesNotice = MobileStrings.Get("MobileFilesNoApp");
            await row.Item.SaveCommand.ExecuteAsync(null).ConfigureAwait(true);
        }
    }

    private void ShowFileMenu(MobileFileRowViewModel row)
    {
        FilesNotice = null;
        MenuFile = row;
    }

    [RelayCommand]
    private void CloseFileMenu() => MenuFile = null;

    [RelayCommand]
    private void CloseImageViewer() => ViewerFile = null;

    /// <summary>Writes a copy wherever the user points, from the menu or the viewer.</summary>
    [RelayCommand]
    private async Task SaveFileCopy()
    {
        MobileFileRowViewModel? row = MenuFile ?? ViewerFile;
        MenuFile = null;
        if (row is null)
        {
            return;
        }

        if (row.Item.IsAwaitingDownload)
        {
            FilesNotice = MobileStrings.Get("MobileFilesStillDownloading");
            return;
        }

        await row.Item.SaveCommand.ExecuteAsync(null).ConfigureAwait(true);
    }

    /// <summary>The menu's delete: asks first, because the delete reaches every device.</summary>
    [RelayCommand]
    private void RequestDeleteFile()
    {
        if (MenuFile is not null)
        {
            IsConfirmingDelete = true;
        }
    }

    [RelayCommand]
    private async Task ConfirmDeleteFile()
    {
        MobileFileRowViewModel? row = MenuFile;
        MenuFile = null;
        if (row is null)
        {
            return;
        }

        if (ViewerFile == row)
        {
            ViewerFile = null;
        }

        await row.Item.DeleteCommand.ExecuteAsync(null).ConfigureAwait(true);
        _syncScheduler?.NotifySaved();
    }

    /// <summary>The viewer's delete goes through the same question as the row's.</summary>
    [RelayCommand]
    private void DeleteViewerFile()
    {
        if (ViewerFile is { } row)
        {
            MenuFile = row;
            IsConfirmingDelete = true;
        }
    }

    /// <summary>The back gesture over an attachment layer: closes the top one.</summary>
    private bool CloseTopFileLayer()
    {
        if (IsFileMenuOpen)
        {
            MenuFile = null;
            return true;
        }

        if (IsAttachSheetOpen)
        {
            IsAttachSheetOpen = false;
            return true;
        }

        if (IsImageViewerOpen)
        {
            ViewerFile = null;
            return true;
        }

        return false;
    }
}

/// <summary>
/// An attachment as the phone lists it: the shared card, plus the one state a phone has to show
/// that the desktop card does not — whether the file has reached the cloud yet.
/// </summary>
public sealed partial class MobileFileRowViewModel : ObservableObject
{
    private readonly Func<MobileFileRowViewModel, Task> _open;
    private readonly Action<MobileFileRowViewModel> _menu;

    public MobileFileRowViewModel(
        FileItemViewModel item,
        Func<MobileFileRowViewModel, Task> open,
        Action<MobileFileRowViewModel> menu)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        _open = open;
        _menu = menu;
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FileItemViewModel.SaveFailed) or nameof(FileItemViewModel.SavedTo))
            {
                RefreshDetail();
            }
        };
    }

    public FileItemViewModel Item { get; }

    public string Name => Item.Name;

    public string Ext => Item.Ext;

    public object? Thumbnail => Item.Thumbnail;

    public bool HasThumbnail => Item.HasThumbnail;

    /// <summary>The metadata is here but its upload has not gone through yet.</summary>
    [ObservableProperty]
    private bool _isUploadPending;

    partial void OnIsUploadPendingChanged(bool value) => RefreshDetail();

    /// <summary>
    /// The line under the name: the size, or the state that matters more than the size. In order: a
    /// save that failed, a save that worked, bytes still coming, bytes still going.
    /// </summary>
    public string Detail => Item switch
    {
        { SaveFailed: true } => MobileStrings.Get("SaveFileFailed"),
        { WasSaved: true } => MobileStrings.Get("MobileFileCopySaved"),
        { IsAwaitingDownload: true } => string.Create(
            CultureInfo.CurrentCulture, $"{Item.SizeLabel} · {MobileStrings.Get("FileAwaitingDownload")}"),
        _ when IsUploadPending => string.Create(
            CultureInfo.CurrentCulture, $"{Item.SizeLabel} · {MobileStrings.Get("MobileFileUploadPending")}"),
        _ => Item.SizeLabel,
    };

    /// <summary>The detail is a failure, drawn in the danger colour.</summary>
    public bool IsAlert => Item.SaveFailed;

    /// <summary>The detail is a transfer still under way, drawn in the accent.</summary>
    public bool IsBusy => !Item.SaveFailed && !Item.WasSaved && (Item.IsAwaitingDownload || IsUploadPending);

    /// <summary>The detail line is built from catalog strings.</summary>
    public void OnLanguageChanged() => RefreshDetail();

    private void RefreshDetail()
    {
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(IsAlert));
        OnPropertyChanged(nameof(IsBusy));
    }

    [RelayCommand]
    private Task Open() => _open(this);

    [RelayCommand]
    private void ShowMenu() => _menu(this);
}
