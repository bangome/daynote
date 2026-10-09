using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Daynote.App.Shell.Product;
using Daynote.Mobile.ViewModels;

namespace Daynote.Mobile.Views;

/// <summary>One of the phone's pages. All behaviour is in <see cref="ViewModels.MobileShellViewModel"/>.</summary>
public partial class ListsPage : UserControl
{
    public ListsPage() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// A long press on a list chip: its menu. There is nowhere on a chip for a dots button, so the
    /// press is the only affordance — the same one a file row uses on the day page.
    /// </summary>
    private void OnListChipContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if ((sender as Control)?.DataContext is AgendaListRowViewModel list
            && DataContext is MobileShellViewModel shell)
        {
            shell.OpenAgendaListMenu(list);
            e.Handled = true;
        }
    }
}
