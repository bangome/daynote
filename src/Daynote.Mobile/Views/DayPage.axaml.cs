using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Daynote.App.Composition;
using Daynote.Core.Domain;
using Daynote.Mobile.ViewModels;
using Daynote.Motion;

namespace Daynote.Mobile.Views;

/// <summary>One of the phone's pages. All behaviour is in <see cref="ViewModels.MobileShellViewModel"/>.</summary>
public partial class DayPage : UserControl
{
    private int? _handledWeekSwipeId;

    private MobileShellViewModel? _observed;

    /// <summary>The date the page last showed, to tell which way a change went.</summary>
    private LocalDate? _shownDate;

    public DayPage()
    {
        InitializeComponent();
        if (this.FindControl<ItemsControl>("WeekDays") is { } days)
        {
            days.SizeChanged += (_, _) => PlacePill();
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_observed is { } previous)
        {
            previous.PropertyChanged -= OnShellPropertyChanged;
        }

        _observed = DataContext as MobileShellViewModel;
        _shownDate = _observed?.SelectedDate;
        if (_observed is { } shell)
        {
            shell.PropertyChanged += OnShellPropertyChanged;
        }

        PlacePill();
    }

    // ── M5: moving to another date ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A new date: the pill springs to it within the week, or the strip slides a whole week over;
    /// and the day's content is pushed the way time went while it cross-fades.
    /// </summary>
    /// <remarks>
    /// The change is seen before anything is rebuilt - the property changes first and the day's
    /// notes are read after - so the stills taken here are still of the day being left.
    /// </remarks>
    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MobileShellViewModel.SelectedDate) || _observed is not { } shell)
        {
            return;
        }

        LocalDate to = shell.SelectedDate;
        if (_shownDate is not { } from || from == to)
        {
            _shownDate = to;
            PlacePill();
            return;
        }

        _shownDate = to;
        int direction = to.CompareTo(from) > 0 ? 1 : -1;
        if (!IsEffectivelyVisible)
        {
            PlacePill();
            return;
        }

        if (WeekStart(from) == WeekStart(to))
        {
            if (this.FindControl<Border>("Pill") is { } pill)
            {
                double fromX = PillX(from);
                PlacePill();
                _ = MotionPlayer.Play(pill, "m5", Choreography.PillSlide(pill, fromX, PillX(to)));
            }
        }
        // No still of what leaves (MotionSnapshot): on a phone's 3x screen it was painted at the
        // wrong scale, so the old day's titles showed oversized under the new ones. The new week
        // and day arrive on their own; the old ones are already gone when the date changes.
        else if (this.FindControl<Panel>("WeekLive") is { } live)
        {
            PlacePill();
            _ = MotionPlayer.Play(live, "m5", Choreography.WeekSlide(live, null, live.Bounds.Width, direction));
        }

        if (this.FindControl<StackPanel>("DayContent") is { } content)
        {
            _ = MotionPlayer.Play(content, "m5", Choreography.ContentPush(content, null, direction));
        }
    }

    /// <summary>Puts the pill under the selected day, sized to its cell, with nothing in motion.</summary>
    private void PlacePill()
    {
        if (this.FindControl<Border>("Pill") is not { } pill || _observed is not { } shell)
        {
            return;
        }

        pill.Width = Math.Max(0, CellWidth - 3);
        MotionTransform.For(pill).X = PillX(shell.SelectedDate);
    }

    private double CellWidth => (this.FindControl<ItemsControl>("WeekDays")?.Bounds.Width ?? 0) / 7;

    /// <summary>Where the pill sits for <paramref name="date"/>: its weekday's cell, inside the button's 1.5-point margin.</summary>
    private double PillX(LocalDate date) => ((int)LocalDates.ToDateOnly(date).DayOfWeek * CellWidth) + 1.5;

    private static DateOnly WeekStart(LocalDate date)
    {
        DateOnly day = LocalDates.ToDateOnly(date);
        return day.AddDays(-(int)day.DayOfWeek);
    }

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
