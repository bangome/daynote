using System.Windows.Input;
using UserControl = System.Windows.Controls.UserControl;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The selected day's to-dos and files, at the right of the window.
/// </summary>
public partial class DeskDayPanelView : UserControl
{
    public DeskDayPanelView() => InitializeComponent();

    /// <summary>
    /// Writes a copy of the attachment wherever the user points. The stored bytes live under a
    /// content-addressed name, so this is the only way one gets back out; the same gesture works on
    /// the cards in the files list.
    /// </summary>
    private void OnFileCardDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is System.Windows.FrameworkElement { DataContext: FileItemViewModel file })
        {
            file.SaveCommand.Execute(null);
            e.Handled = true;
        }
    }
}
