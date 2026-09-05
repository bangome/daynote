using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Notes;

namespace Daynote.App.Shell.Product;

/// <summary>
/// One row in the 할 일 panel: a parsed <see cref="TodoLine"/> from any note on any date. Toggling the
/// checkbox rewrites the source note's body line through the save pipeline; the row body jumps to that
/// note's date. Overdue due labels render in red (DESIGN redesign todo tab).
/// </summary>
public sealed partial class TodoItemViewModel : ObservableObject
{
    private readonly TodoLine _line;
    private readonly Func<TodoLine, Task> _onToggle;
    private readonly Func<TodoLine, Task> _onJump;

    public TodoItemViewModel(TodoLine line, Func<TodoLine, Task> onToggle, Func<TodoLine, Task> onJump)
    {
        _line = line;
        _onToggle = onToggle;
        _onJump = onJump;
    }

    public bool Checked => _line.Checked;

    public string Text => _line.Text;

    public bool HasDue => _line.DueLabel.Length > 0;

    public string DueLabel => _line.DueLabel;

    public bool Overdue => _line.Overdue;

    /// <summary>"note title · M/D" origin label shown beneath the task text.</summary>
    public string NoteLabel => string.Create(
        CultureInfo.CurrentCulture, $"{_line.NoteTitle} · {_line.Date.Month}/{_line.Date.Day}");

    [RelayCommand]
    private Task Toggle() => _onToggle(_line);

    [RelayCommand]
    private Task Jump() => _onJump(_line);
}
