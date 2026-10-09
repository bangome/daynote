using Avalonia;
using Avalonia.Controls;

namespace Daynote.Motion;

/// <summary>
/// M6 on the desktop, for an overlay that is a scrim around one surface (the palette, settings, the
/// account card): open fades the scrim and grows the surface from 96% in 180 ms; close is a 120 ms
/// fade, after which the overlay is hidden.
/// </summary>
/// <remarks>
/// Set <c>Popover.IsOpen</c> in place of an <c>IsVisible</c> binding, and <c>IsVisible="False"</c>
/// beside it for the first frame. The surface is the scrim's child (a <see cref="Decorator"/>) or
/// its last child (a <see cref="Panel"/>); its <c>RenderTransformOrigin</c> is the anchor it grows
/// from.
/// </remarks>
public static class Popover
{
    public static readonly AttachedProperty<bool> IsOpenProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsOpen", typeof(Popover));

    static Popover()
    {
        IsOpenProperty.Changed.AddClassHandler<Control>((layer, e) => _ = ShowAsync(layer, e.NewValue is true));
    }

    public static bool GetIsOpen(Control layer) => layer.GetValue(IsOpenProperty);

    public static void SetIsOpen(Control layer, bool value) => layer.SetValue(IsOpenProperty, value);

    private static Visual? Surface(Control layer) => layer switch
    {
        Decorator { Child: { } child } => child,
        Panel { Children.Count: > 0 } panel => panel.Children[^1],
        _ => null,
    };

    private static async Task ShowAsync(Control layer, bool open)
    {
        Visual? surface = Surface(layer);
        if (open)
        {
            layer.IsVisible = true;
            var board = surface is null ? new Storyboard() : Choreography.PopoverOpen(surface);
            board.Add(v => layer.Opacity = v, 0, 1, MotionEnvironment.ReduceMotion ? MotionTokens.ReducedFade : MotionSpec.Ms(180, MotionTokens.GentleCurve));
            await MotionPlayer.Play(layer, "m6", board).ConfigureAwait(true);
            return;
        }

        if (!layer.IsVisible)
        {
            return;
        }

        var closing = surface is null ? new Storyboard() : Choreography.PopoverClose(surface);
        closing.Add(v => layer.Opacity = v, layer.Opacity, 0, MotionEnvironment.ReduceMotion ? MotionTokens.ReducedFade : MotionSpec.Ms(120, MotionTokens.ExitCurve));
        if (await MotionPlayer.Play(layer, "m6", closing).ConfigureAwait(true) && !GetIsOpen(layer))
        {
            layer.IsVisible = false;
            layer.Opacity = 1;
            if (surface is not null)
            {
                surface.Opacity = 1;
            }
        }
    }
}
