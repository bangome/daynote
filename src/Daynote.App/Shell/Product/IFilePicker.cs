namespace Daynote.App.Shell.Product;

/// <summary>
/// Abstracts the Win32 open-file dialog so the files panel is testable without a real dialog. Returns the
/// absolute paths the user chose, or an empty list when cancelled.
/// </summary>
public interface IFilePicker
{
    IReadOnlyList<string> PickFiles();
}
