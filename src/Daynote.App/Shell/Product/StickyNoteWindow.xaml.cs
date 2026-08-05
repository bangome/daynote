using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Daynote.App.Localization;

namespace Daynote.App.Shell.Product;

/// <summary>
/// A pinnable floating post-it that mirrors the shell's live editor buffer. It binds two-way to the same
/// <see cref="NoteWorkspaceViewModel.EditorText"/> and selected-tab title the main editor uses, so edits
/// flow both ways in real time and ride the shared autosave path — no snapshot copy is kept.
/// </summary>
public partial class StickyNoteWindow : Window, INotifyPropertyChanged
{
    public StickyNoteWindow(ProductShellViewModel shell)
    {
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
        InitializeComponent();
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The shared shell whose editor buffer and selected-note title this post-it mirrors live.</summary>
    public ProductShellViewModel Shell { get; }

    public bool IsPinned
    {
        get => Topmost;
        private set
        {
            if (Topmost == value)
            {
                return;
            }

            Topmost = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PinToolTip));
        }
    }

    public string PinToolTip => IsPinned ? AppStrings.UnpinStickyNote : AppStrings.PinStickyNote;

    private void OnToggleTopmost(object sender, RoutedEventArgs e) => IsPinned = !IsPinned;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
