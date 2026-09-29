using Avalonia;
using Avalonia.Controls;

namespace Daynote.Desktop.Views;

/// <summary>
/// Cards in as many equal columns as fit, the CSS <c>repeat(auto-fill, minmax(N, 1fr))</c> the design's
/// favourites and files grids are written in.
/// </summary>
/// <remarks>
/// Neither <see cref="WrapPanel"/> (fixed-width items leave a ragged right edge) nor
/// <see cref="Avalonia.Controls.Primitives.UniformGrid"/> (a fixed column count) does this: the column
/// count follows the width, and the columns share what is left over. Each row is as tall as its tallest
/// card, so a row of cards lines up at the bottom as well.
/// </remarks>
public sealed class AutoFillGrid : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<AutoFillGrid, double>(nameof(MinItemWidth), 200);

    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<AutoFillGrid, double>(nameof(Gap), 12);

    static AutoFillGrid() => AffectsMeasure<AutoFillGrid>(MinItemWidthProperty, GapProperty);

    /// <summary>The narrowest a column may get before one fewer is used.</summary>
    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    /// <summary>Space between columns and between rows.</summary>
    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    private int ColumnsFor(double width) =>
        double.IsInfinity(width) || width <= 0
            ? 1
            : Math.Max(1, (int)Math.Floor((width + Gap) / (MinItemWidth + Gap)));

    private double ColumnWidth(double width, int columns) =>
        double.IsInfinity(width) ? MinItemWidth : Math.Max(0, (width - (Gap * (columns - 1))) / columns);

    protected override Size MeasureOverride(Size availableSize)
    {
        int columns = ColumnsFor(availableSize.Width);
        double columnWidth = ColumnWidth(availableSize.Width, columns);
        double height = 0;
        double rowHeight = 0;
        int index = 0;
        foreach (Control child in Children.Where(static c => c.IsVisible))
        {
            child.Measure(new Size(columnWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if (++index % columns == 0)
            {
                height += rowHeight + Gap;
                rowHeight = 0;
            }
        }

        height += rowHeight;
        if (index > 0 && index % columns == 0)
        {
            height -= Gap;
        }

        double width = double.IsInfinity(availableSize.Width)
            ? (columnWidth * Math.Min(columns, Math.Max(1, index))) + (Gap * (Math.Min(columns, Math.Max(1, index)) - 1))
            : availableSize.Width;
        return new Size(width, Math.Max(0, height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int columns = ColumnsFor(finalSize.Width);
        double columnWidth = ColumnWidth(finalSize.Width, columns);
        // A hidden card takes no slot, so the visible ones close up rather than leaving a hole.
        var children = Children.Where(static c => c.IsVisible).ToList();
        double y = 0;
        for (int start = 0; start < children.Count; start += columns)
        {
            List<Control> row = children.Skip(start).Take(columns).ToList();
            double rowHeight = row.Max(static child => child.DesiredSize.Height);
            for (int i = 0; i < row.Count; i++)
            {
                row[i].Arrange(new Rect(i * (columnWidth + Gap), y, columnWidth, rowHeight));
            }

            y += rowHeight + Gap;
        }

        return finalSize;
    }
}
