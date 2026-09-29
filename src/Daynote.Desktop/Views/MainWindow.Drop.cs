using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Daynote.Desktop.Views;

/// <summary>
/// Files dropped on the files list's drop zone or on the day panel are kept on the selected day, the
/// same store the "+ 추가" picker writes to. Folders and anything that is not a file are ignored.
/// </summary>
public partial class MainWindow
{
    private void AttachFileDrop()
    {
        foreach (Control target in new Control[] { FileDropZone, DayPanel })
        {
            target.AddHandler(DragDrop.DragOverEvent, OnFileDragOver);
            target.AddHandler(DragDrop.DropEvent, OnFileDrop);
        }
    }

    private static void OnFileDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.TryGetFiles() is { Length: > 0 } ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnFileDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_shell is not { } shell || e.DataTransfer.TryGetFiles() is not { Length: > 0 } items)
        {
            return;
        }

        foreach (IStorageFile file in items.OfType<IStorageFile>())
        {
            try
            {
                await using Stream stream = await file.OpenReadAsync().ConfigureAwait(true);
                await shell.Files.AddFromStreamAsync(file.Name, stream).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // One unreadable file does not stop the rest; AddFromStreamAsync already skips oversized ones.
            }
        }
    }
}
