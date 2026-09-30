using UserControl = System.Windows.Controls.UserControl;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The design's navy sidebar. All markup and bindings; the class exists so the window can host it
/// and the tutorial can point at the elements inside it by name.
/// </summary>
public partial class DeskSidebarView : UserControl
{
    public DeskSidebarView() => InitializeComponent();
}
