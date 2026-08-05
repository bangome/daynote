namespace Daynote.App.Shell.Product;

/// <summary>Production <see cref="IFilePicker"/> over the WPF open-file dialog (multi-select).</summary>
public sealed class Win32FilePicker : IFilePicker
{
    public IReadOnlyList<string> PickFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            CheckFileExists = true,
            Title = Localization.AppStrings.AddFile,
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }
}
