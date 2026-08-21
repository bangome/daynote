using WpfUserControl = System.Windows.Controls.UserControl;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The search results dropdown that floats under the command bar. Lifted out of ProductWindow.xaml,
/// which had grown past the reviewable-size limit; it is a self-contained surface over
/// <see cref="SearchDropdownViewModel"/> with no code-behind of its own. Where it sits and whether it
/// is shown stay with the window that hosts it.
/// </summary>
public partial class SearchDropdownView : WpfUserControl
{
    public SearchDropdownView()
    {
        InitializeComponent();
    }
}
