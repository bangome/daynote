using Avalonia;
using Avalonia.Controls;

namespace Daynote.Desktop.Views;

/// <summary>
/// The header's one responsive rule. The design lays the date, the week strip and the actions out
/// with <c>flex-wrap</c>: when the three do not fit on one line, the week strip drops to a line of its
/// own under them. A Grid does not wrap, so this moves the strip between the two rows as the header
/// is resized; the columns never change.
/// </summary>
public partial class MainWindow
{
    /// <summary>The strip's horizontal margin while it shares the line with the date and the actions.</summary>
    private const double WeekStripGap = 24;

    private void AttachHeaderWrap() => Header.SizeChanged += (_, _) => UpdateHeaderWrap();

    private void UpdateHeaderWrap()
    {
        double available = Header.Bounds.Width - Header.Padding.Left - Header.Padding.Right;
        if (available <= 0)
        {
            return;
        }

        // Desired widths do not depend on where the strip sits, so the decision is stable: moving it
        // cannot make it fit or not fit on the next pass.
        double lead = HeaderGrid.Children
            .OfType<Control>()
            .Where(static child => Grid.GetColumn(child) < 2 && child.IsVisible)
            .Sum(static child => child.DesiredSize.Width);
        double needed = lead + WeekStrip.DesiredSize.Width - WeekStrip.Margin.Left - WeekStrip.Margin.Right
            + (2 * WeekStripGap) + HeaderActions.DesiredSize.Width;
        bool wrap = needed > available;

        int row = wrap ? 1 : 0;
        if (Grid.GetRow(WeekStrip) == row)
        {
            return;
        }

        Grid.SetRow(WeekStrip, row);
        Grid.SetColumn(WeekStrip, wrap ? 0 : 2);
        Grid.SetColumnSpan(WeekStrip, wrap ? 4 : 1);
        WeekStrip.Margin = wrap ? new Thickness(0, 14, 0, 0) : new Thickness(WeekStripGap, 0, WeekStripGap, 0);
    }
}
