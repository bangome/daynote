using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Daynote.Motion;

namespace Daynote.Mobile.Views;

/// <summary>
/// The phone's to-do checkbox: a 22-point rounded square in a 44-point target, ticked the way the
/// motion spec's M3 draws it (<see cref="TickButton"/>).
/// </summary>
/// <remarks>
/// The outline, the fill and the pulse ring are three shapes so the tick can grow the fill and the
/// ring without scaling the outline (Daynote.Mobile.Styles: <c>checkbox</c>, <c>checkfill</c>,
/// <c>checkpulse</c>).
/// </remarks>
public sealed class TodoCheck : TickButton
{
    private readonly Border _pulse;
    private readonly Border _fill;
    private readonly Glyph _mark;

    public TodoCheck()
    {
        Classes.Add("check");
        _pulse = new Border { Classes = { "checkpulse" }, Opacity = 0, IsHitTestVisible = false, RenderTransformOrigin = RelativePoint.Center };
        _fill = new Border { Classes = { "checkfill" }, RenderTransformOrigin = RelativePoint.Center };
        _mark = new Glyph
        {
            Kind = GlyphKind.Check,
            Width = 11,
            Height = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _mark.Bind(Glyph.ForegroundProperty, this.GetResourceObservable("Mobile.OnPri"));

        Content = new Panel
        {
            Width = 22,
            Height = 22,
            Children = { _pulse, new Border { Classes = { "checkbox" } }, _fill, _mark },
        };
    }

    protected override Visual Fill => _fill;

    protected override Visual? Pulse => _pulse;

    protected override void DrawMark(double fraction) => _mark.Draw = fraction;
}
