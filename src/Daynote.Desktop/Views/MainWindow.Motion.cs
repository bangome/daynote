using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Daynote.App.Shell.Product;
using Daynote.Desktop.ViewModels;
using Daynote.Motion;

namespace Daynote.Desktop.Views;

/// <summary>
/// The motion spec on the Mac: M5's date change (the mini calendar's highlight fades in 120 ms and
/// the day's content moves 12 points), and the keys the day panel's arrivals are read by (M2).
/// The tick (M3) is <see cref="DeskTodoCheck"/>; the overlays (M6) are <see cref="Popover"/>.
/// </summary>
public partial class MainWindow
{
    static MainWindow() =>
        RowArrivals.RegisterKey<TodoItemViewModel>(static todo => todo.Key);

    private DesktopShellViewModel? _motionShell;
    private Core.Domain.LocalDate? _shownDate;

    private void AttachMotion(DesktopShellViewModel? shell)
    {
        if (_motionShell is { } previous)
        {
            previous.PropertyChanged -= OnMotionShellPropertyChanged;
        }

        _motionShell = shell;
        _shownDate = shell?.SelectedDate;
        if (shell is not null)
        {
            shell.PropertyChanged += OnMotionShellPropertyChanged;
        }
    }

    private void OnMotionShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DesktopShellViewModel.SelectedDate) || _motionShell is not { } shell)
        {
            return;
        }

        Core.Domain.LocalDate to = shell.SelectedDate;
        if (_shownDate is not { } from || from == to)
        {
            _shownDate = to;
            return;
        }

        _shownDate = to;
        int direction = to.CompareTo(from) > 0 ? 1 : -1;

        // The note column and the day panel move; the sidebar, which holds the calendar, does not.
        foreach (Control content in new Control?[] { this.FindControl<Control>("NoteColumn"), this.FindControl<Control>("DayPanelContent") }.OfType<Control>())
        {
            if (content.IsEffectivelyVisible)
            {
                _ = MotionPlayer.Play(content, "m5", Choreography.ContentPush(content, null, direction));
            }
        }

        // The calendar is rebuilt after the date is read; its new highlight fades in once it is there.
        Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<ItemsControl>("MiniCalendar") is not { } calendar)
            {
                return;
            }

            foreach (Border face in calendar.GetVisualDescendants().OfType<Button>()
                .Where(static b => b.Classes.Contains("selected"))
                .SelectMany(static b => b.GetVisualDescendants().OfType<Border>().Where(static f => f.Classes.Contains("face"))))
            {
                _ = MotionPlayer.Play(face, "m5", Choreography.HighlightFade(face));
            }
        }, DispatcherPriority.Loaded);
    }
}
