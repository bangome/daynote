using System.Windows;
using System.Windows.Input;
using UserControl = System.Windows.Controls.UserControl;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The command palette over the shell: quick actions, then the unified search's results.
/// </summary>
public partial class DeskPaletteView : UserControl
{
    public DeskPaletteView()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    /// <summary>Opening the palette puts the caret in the query, which is the only reason it opens.</summary>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            _ = Dispatcher.BeginInvoke(() => SearchBox.Focus());
        }
    }

    /// <summary>A click on the scrim dismisses it; a click on the card is not one.</summary>
    private void OnScrimClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ProductShellViewModel shell)
        {
            shell.ClosePaletteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnCardClick(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
