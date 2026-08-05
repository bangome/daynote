using WpfSelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace Daynote.App.Sidebar;

public partial class SidebarView : WpfUserControl
{
    private bool _syncing;

    public SidebarView()
    {
        InitializeComponent();
    }

    private async void OnNoteSelectionChanged(object sender, WpfSelectionChangedEventArgs e)
    {
        if (_syncing || DataContext is not SidebarViewModel viewModel)
        {
            return;
        }

        if (NoteList.SelectedItem is not Notes.NoteTabViewModel tab || ReferenceEquals(tab, viewModel.Notes.SelectedTab))
        {
            return;
        }

        bool proceeded = await viewModel.Notes.SelectNoteAsync(tab).ConfigureAwait(true);
        if (!proceeded)
        {
            _syncing = true;
            NoteList.SelectedItem = viewModel.Notes.SelectedTab;
            _syncing = false;
        }
    }
}
