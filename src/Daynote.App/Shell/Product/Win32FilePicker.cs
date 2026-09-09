namespace Daynote.App.Shell.Product;

/// <summary>Production <see cref="IFilePicker"/> over the WPF open-file dialog (multi-select).</summary>
public sealed class Win32FilePicker : IFilePicker
{
    public Task<IReadOnlyList<string>> PickFilesAsync(CancellationToken cancellationToken = default)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            CheckFileExists = true,
            Title = Localization.AppStrings.AddFile,
        };

        IReadOnlyList<string> chosen = dialog.ShowDialog() == true ? dialog.FileNames : [];
        return Task.FromResult(chosen);
    }

    public Task<string?> PickSavePathAsync(string suggestedFileName, CancellationToken cancellationToken = default)
    {
        // The suggested name carries the attachment's own extension, so the dialog's filter is built
        // from it rather than fixed: saving a .png should not offer to append ".zip".
        string extension = System.IO.Path.GetExtension(suggestedFileName);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = suggestedFileName,
            DefaultExt = extension,
            AddExtension = extension.Length > 0,
            OverwritePrompt = true,
            Title = Localization.AppStrings.SaveFileTitle,
            Filter = extension.Length > 1
                ? string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    Localization.AppStrings.SaveFileFilter,
                    extension.TrimStart('.').ToUpperInvariant(),
                    extension)
                : Localization.AppStrings.SaveFileFilterAny,
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }
}
