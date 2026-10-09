using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Daynote.Motion;

/// <summary>
/// A still of a control as it looks now, for the half of a cross-fade whose content is already
/// gone: M5's day content and week strip are rebuilt the moment the date changes, so what leaves
/// has to be a picture of what was there.
/// </summary>
public static class MotionSnapshot
{
    /// <summary>
    /// Paints <paramref name="control"/> into <paramref name="ghost"/> at its current size and puts
    /// the ghost where the control is; null-safe and does nothing when motion is instant or reduced
    /// to nothing to see.
    /// </summary>
    /// <param name="maxHeight">
    /// The most of a tall control worth painting, in points. A day's content can run to thousands of
    /// points of which a screen shows a fraction, and a still of all of it would cost tens of
    /// megabytes for a quarter of a second.
    /// </param>
    /// <returns>True when the ghost now shows the control.</returns>
    public static bool Into(Control control, Image ghost, double maxHeight = 1600)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(ghost);
        if (MotionEnvironment.Instant || TopLevel.GetTopLevel(control) is not { } top ||
            control.Bounds.Width < 1 || control.Bounds.Height < 1)
        {
            return false;
        }

        double scale = Math.Min(top.RenderScaling, 2);
        double width = control.Bounds.Width;
        double height = Math.Min(control.Bounds.Height, maxHeight);
        var size = new PixelSize((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale));
        var still = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        still.Render(control);

        (ghost.Source as IDisposable)?.Dispose();
        ghost.Source = still;
        ghost.Width = width;
        ghost.Height = height;
        ghost.Opacity = 1;
        ghost.IsVisible = true;
        return true;
    }

    /// <summary>Hides the ghost and lets its picture go.</summary>
    public static void Clear(Image ghost)
    {
        ArgumentNullException.ThrowIfNull(ghost);
        ghost.IsVisible = false;
        (ghost.Source as IDisposable)?.Dispose();
        ghost.Source = null;
        MotionTransform.For(ghost).Reset();
    }
}
