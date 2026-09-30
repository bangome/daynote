using System.Windows.Input;
using UserControl = System.Windows.Controls.UserControl;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The four lists the sidebar opens, filling the middle of the window.
/// </summary>
public partial class DeskListsView : UserControl
{
    public DeskListsView() => InitializeComponent();

    /// <summary>Writes a copy of the attachment wherever the user points; see DeskDayPanelView.</summary>
    private void OnFileCardDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is System.Windows.FrameworkElement { DataContext: FileItemViewModel file })
        {
            file.SaveCommand.Execute(null);
            e.Handled = true;
        }
    }
}
