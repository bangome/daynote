using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Daynote.Motion;
using Path = Avalonia.Controls.Shapes.Path;

namespace Daynote.Desktop.Views;

/// <summary>
/// The desktop's round to-do check, ticked the way the motion spec's M3 has it on a Mac: the fill
/// eases out over 200 ms with no overshoot, the check draws itself, the strikethrough passes, and
/// there is no pulse (<see cref="TickButton"/>).
/// </summary>
/// <remarks>
/// The ring is the button's own border (<c>Button.check</c>); the fill is a circle inside it so
/// the tick can grow it from the middle, and the check is a dashed stroke so it can draw itself.
/// </remarks>
public sealed class DeskTodoCheck : TickButton
{
    /// <summary>The check's length in its 10-unit box, both segments.</summary>
    private const double MarkLength = 10.7;

    private readonly Ellipse _fill;
    private readonly Path _mark;

    public DeskTodoCheck()
    {
        Classes.Add("check");
        _fill = new Ellipse { Classes = { "checkfill" }, Margin = new Thickness(-1.5), RenderTransformOrigin = RelativePoint.Center };
        _mark = new Path
        {
            Classes = { "icon", "mark" },
            StrokeThickness = 1.8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _mark.Bind(Path.DataProperty, this.GetResourceObservable("Daynote.Desk.Geo.Check"));
        Content = new Panel { Children = { _fill, _mark } };
    }

    protected override Visual Fill => _fill;

    protected override Visual? Pulse => null;

    /// <summary>A dash as long as the stroke, offset so only the drawn part shows; dashes are in stroke widths.</summary>
    protected override void DrawMark(double fraction)
    {
        _mark.Opacity = fraction <= 0 ? 0 : 1;
        double length = MarkLength / _mark.StrokeThickness;
        _mark.StrokeDashArray = fraction >= 1 ? null : [length, length];
        _mark.StrokeDashOffset = (1 - fraction) * length;
    }
}
