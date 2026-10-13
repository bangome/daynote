using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Daynote.App.Shell.Product;

namespace Daynote.Desktop.Views;

/// <summary>A sidebar list's rename box: it takes the keys as it appears, and keeps the name when it loses them.</summary>
public partial class MainWindow
{
    private void OnListRenamePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && sender is TextBox { IsVisible: true } box)
        {
            Dispatcher.UIThread.Post(() =>
            {
                box.Focus();
                box.SelectAll();
            }, DispatcherPriority.Loaded);
        }
    }

    /// <summary>Clicking away keeps what was typed, the way a note's heading does.</summary>
    private void OnListRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: AgendaListRowViewModel { IsRenaming: true } list })
        {
            list.CommitRenameCommand.Execute(null);
        }
    }
}
