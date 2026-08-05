using System.Windows.Controls;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace Daynote.App.Shell.Product;

/// <summary>
/// Host for the read-only timeline. Its only code-behind concern is infinite scroll: when the viewport
/// nears the bottom it asks the view model for the next page, guarding against re-entrant loads.
/// </summary>
public partial class TimelineView : WpfUserControl
{
    private const double NearBottomThreshold = 300d;

    public TimelineView()
    {
        InitializeComponent();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 && e.ExtentHeightChange == 0)
        {
            return;
        }

        if (DataContext is not TimelineViewModel viewModel || !viewModel.HasMore)
        {
            return;
        }

        var scroller = (ScrollViewer)sender;
        if (e.VerticalOffset >= scroller.ScrollableHeight - NearBottomThreshold)
        {
            viewModel.LoadMoreCommand.Execute(null);
        }
    }
}
