using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Daynote.Desktop.Views;

/// <summary>
/// The day's note tabs scroll sideways without a bar: a mouse wheel turned either way moves them,
/// and while tabs run past an end, a round step there pages toward them.
/// </summary>
public partial class MainWindow
{
    /// <summary>How far one wheel notch moves the tabs.</summary>
    private const double NoteTabsWheelStep = 48;

    private void AttachNoteTabs()
    {
        // Tunnelling, because the scroll viewer would take a horizontal wheel itself and drop a vertical one.
        NoteTabsScroll.AddHandler(PointerWheelChangedEvent, OnNoteTabsWheel, RoutingStrategies.Tunnel);
        NoteTabsScroll.ScrollChanged += (_, _) => UpdateNoteTabSteps();
        NoteTabsScroll.SizeChanged += (_, _) => UpdateNoteTabSteps();
        TabMenuLayer.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && TabMenuLayer.IsVisible)
            {
                PlaceTabMenu();
            }
        };
    }

    private void OnNoteTabsWheel(object? sender, PointerWheelEventArgs e)
    {
        // A trackpad's sideways swipe comes as X; a wheel's turn as Y. Whichever is larger moves the strip.
        double delta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
        if (delta == 0 || NoteTabsScroll.Extent.Width <= NoteTabsScroll.Viewport.Width)
        {
            return;
        }

        ScrollNoteTabsTo(NoteTabsScroll.Offset.X - (delta * NoteTabsWheelStep));
        e.Handled = true;
    }

    private void OnNoteTabsLeft(object? sender, RoutedEventArgs e) => StepNoteTabs(-1);

    private void OnNoteTabsRight(object? sender, RoutedEventArgs e) => StepNoteTabs(1);

    /// <summary>Pages most of a view's width, so the tab that was cut at the end comes whole into sight.</summary>
    private void StepNoteTabs(int direction) =>
        ScrollNoteTabsTo(NoteTabsScroll.Offset.X + (direction * Math.Max(NoteTabsWheelStep, NoteTabsScroll.Viewport.Width * 0.7)));

    private void ScrollNoteTabsTo(double x)
    {
        double max = Math.Max(0, NoteTabsScroll.Extent.Width - NoteTabsScroll.Viewport.Width);
        NoteTabsScroll.Offset = new Vector(Math.Clamp(x, 0, max), NoteTabsScroll.Offset.Y);
    }

    /// <summary>Puts the note menu under its button, its right edge 14px in from the button's.</summary>
    private void PlaceTabMenu()
    {
        if (TabMenuButton.TranslatePoint(new Point(TabMenuButton.Bounds.Width, TabMenuButton.Bounds.Height), TabMenuLayer) is not { } corner)
        {
            return;
        }

        double left = Math.Max(12, corner.X + 14 - TabMenu.Width);
        TabMenu.Margin = new Thickness(left, corner.Y + 14, 0, 0);
    }

    /// <summary>A press beside the menu closes it, as the design's full-window catcher does.</summary>
    private void OnTabMenuLayerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender) && _shell is { } shell)
        {
            shell.CloseTabMenuCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void UpdateNoteTabSteps()
    {
        double max = NoteTabsScroll.Extent.Width - NoteTabsScroll.Viewport.Width;
        NoteTabsLeft.IsVisible = max > 0.5 && NoteTabsScroll.Offset.X > 0.5;
        NoteTabsRight.IsVisible = max > 0.5 && NoteTabsScroll.Offset.X < max - 0.5;
    }
}
