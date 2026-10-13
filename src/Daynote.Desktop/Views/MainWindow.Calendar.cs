using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Daynote.App.Shell.Product;

namespace Daynote.Desktop.Views;

/// <summary>
/// The month view's day card: it comes up half a second after the pointer rests on a day, beside the
/// day (right, or left when there is no room), and goes a moment after the pointer leaves both.
/// </summary>
public partial class MainWindow
{
    private static readonly TimeSpan BoardHoverDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan BoardHoverGrace = TimeSpan.FromMilliseconds(180);

    private DispatcherTimer? _boardShow;
    private DispatcherTimer? _boardHide;

    private void OnBoardCellEntered(object? sender, PointerEventArgs e)
    {
        _boardHide?.Stop();
        if (sender is not Control { DataContext: CalendarBoardDay { IsInMonth: true } day } cell || _shell is not { } shell)
        {
            return;
        }

        _boardShow?.Stop();
        _boardShow = new DispatcherTimer { Interval = BoardHoverDelay };
        _boardShow.Tick += (_, _) =>
        {
            _boardShow?.Stop();
            ShowBoardCard(shell.CalendarBoard, day, cell);
        };
        _boardShow.Start();
    }

    private void OnBoardCellExited(object? sender, PointerEventArgs e)
    {
        _boardShow?.Stop();
        if (_shell is not { } shell)
        {
            return;
        }

        _boardHide?.Stop();
        _boardHide = new DispatcherTimer { Interval = BoardHoverGrace };
        _boardHide.Tick += (_, _) =>
        {
            _boardHide?.Stop();
            shell.CalendarBoard.Hover = null;
        };
        _boardHide.Start();
    }

    private void OnBoardHoverEntered(object? sender, PointerEventArgs e) => _boardHide?.Stop();

    /// <summary>Puts the card for <paramref name="day"/> beside its cell, the way the design measures it.</summary>
    public void ShowBoardCard(CalendarBoardViewModel board, CalendarBoardDay day, Control cell)
    {
        if (cell.TranslatePoint(default, Root) is not { } topLeft)
        {
            return;
        }

        double width = BoardHover.Width;
        bool right = topLeft.X + cell.Bounds.Width + width + 8 < Root.Bounds.Width;
        double x = right ? topLeft.X + cell.Bounds.Width + 6 : Math.Max(8, topLeft.X - width - 6);
        double y = Math.Max(12, Math.Min(topLeft.Y, Root.Bounds.Height - 392));
        BoardHover.Margin = new Thickness(x, y, 0, 0);
        board.Hover = day;
    }
}
