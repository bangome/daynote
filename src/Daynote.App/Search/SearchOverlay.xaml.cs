using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace Daynote.App.Search;

public partial class SearchOverlay : WpfUserControl
{
    public SearchOverlay()
    {
        InitializeComponent();
    }

    private SearchViewModel? ViewModel => DataContext as SearchViewModel;

    /// <summary>Moves keyboard focus into the result list (Down from the search box).</summary>
    public void FocusFirstResult()
    {
        if (ViewModel is not { } viewModel || viewModel.Results.Count == 0)
        {
            return;
        }

        viewModel.SelectedResult ??= viewModel.Results[0];
        ResultList.UpdateLayout();
        if (ResultList.ItemContainerGenerator.ContainerFromItem(viewModel.SelectedResult)
            is System.Windows.Controls.ListBoxItem container)
        {
            container.Focus();
        }
        else
        {
            ResultList.Focus();
        }
    }

    private void OnKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        switch (e.Key)
        {
            case WpfKey.Enter when viewModel.SelectedResult is { } selected:
                _ = viewModel.ActivateAsync(selected);
                e.Handled = true;
                break;
            case WpfKey.Escape:
                if (viewModel.Query.Length > 0)
                {
                    viewModel.ClearQuery();
                }
                else
                {
                    viewModel.Close();
                }

                e.Handled = true;
                break;
        }
    }

    private void OnResultInvoked(object sender, WpfMouseButtonEventArgs e)
    {
        if (ViewModel is { SelectedResult: { } selected } viewModel)
        {
            _ = viewModel.ActivateAsync(selected);
            e.Handled = true;
        }
    }
}
