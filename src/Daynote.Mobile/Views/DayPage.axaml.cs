using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Daynote.Mobile.ViewModels;

namespace Daynote.Mobile.Views;

/// <summary>One of the phone's pages. All behaviour is in <see cref="ViewModels.MobileShellViewModel"/>.</summary>
public partial class DayPage : UserControl
{
    private int? _handledWeekSwipeId;

    public DayPage() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>A long press on a file row: the same menu as its dots.</summary>
    private void OnFileRowContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if ((sender as Control)?.DataContext is MobileFileRowViewModel row)
        {
            row.ShowMenuCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Pages the week once per horizontal gesture while leaving vertical motion to the page scroller.</summary>
    private void OnWeekStripSwipe(object? sender, SwipeGestureEventArgs e)
    {
        if (_handledWeekSwipeId == e.Id)
        {
            e.Handled = true;
            return;
        }

        if (DataContext is not MobileShellViewModel shell)
        {
            return;
        }

        _handledWeekSwipeId = e.Id;
        if (e.SwipeDirection == SwipeDirection.Left)
        {
            _ = shell.NextWeekCommand.ExecuteAsync(null);
        }
        else if (e.SwipeDirection == SwipeDirection.Right)
        {
            _ = shell.PreviousWeekCommand.ExecuteAsync(null);
        }

        e.Handled = true;
    }

    private void OnWeekStripSwipeEnded(object? sender, SwipeGestureEndedEventArgs e)
    {
        if (_handledWeekSwipeId == e.Id)
        {
            _handledWeekSwipeId = null;
        }

        e.Handled = true;
    }
}
