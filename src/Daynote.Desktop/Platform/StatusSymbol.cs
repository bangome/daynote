using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Daynote.Desktop.Platform;

/// <summary>
/// The app symbol as the status item and the tray draw it (menu bar design §01).
/// </summary>
/// <remarks>
/// Drawn rather than shipped as a file, from the symbol's own outlines (a calendar page with a
/// rising sun, 1254 units square): the Mac wants a one-colour template that AppKit inks to suit
/// the bar, and Windows wants the coloured mark with the count stamped on it, which no fixed
/// image can carry.
/// </remarks>
public static class StatusSymbol
{
    private const double ViewBox = 1254;

    private static readonly Geometry Page = Geometry.Parse(
        "M242 179H1008c67 0 120 53 120 120v553l-291 293H248c-68 0-116-48-116-116V299c0-67 46-120 110-120Z");

    private static readonly Geometry Rings = Geometry.Parse("M384 82v154M870 82v154");

    private static readonly Geometry Rule = Geometry.Parse("M132 779h996");

    private static readonly Geometry Fold = Geometry.Parse("M837 1145V947c0-52 33-85 85-85h206");

    private static readonly Geometry Rays = Geometry.Parse("M626 390v126M423 469l73 99M305 613l112 62M829 469l-73 99M947 613l-112 62");

    /// <summary>The sun: the upper half of a disc standing on the page's centre rule.</summary>
    private static readonly Geometry Sun = Geometry.Parse("M421 779A205 205 0 0 1 831 779Z");

    /// <summary>
    /// The menu bar's template image, as PNG: black on transparent, <paramref name="pixels"/>
    /// square. Strokes are heavier than the full-size mark's, because at 18 points the original
    /// hairlines would vanish into the bar.
    /// </summary>
    public static byte[] TemplatePng(int pixels = 36)
    {
        IBrush ink = Brushes.Black;
        using var bitmap = new RenderTargetBitmap(new PixelSize(pixels, pixels), new Vector(96, 96));
        using (DrawingContext context = bitmap.CreateDrawingContext())
        using (context.PushTransform(Matrix.CreateScale(pixels / ViewBox, pixels / ViewBox)))
        {
            context.DrawGeometry(null, Pen(ink, 96), Page);
            context.DrawGeometry(null, Pen(ink, 110), Rings);
            context.DrawGeometry(null, Pen(ink, 60), Rule);
            context.DrawGeometry(null, Pen(ink, 80), Fold);
            context.DrawGeometry(null, Pen(ink, 70), Rays);
            context.DrawGeometry(ink, null, Sun);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    /// <summary>
    /// The tray icon: the coloured mark with the count in an orange badge at its corner (B6/B7),
    /// or the plain mark when nothing is left.
    /// </summary>
    public static WindowIcon TrayIcon(int count, int pixels = 32)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(pixels, pixels), new Vector(96, 96));
        using (DrawingContext context = bitmap.CreateDrawingContext())
        {
            using (Stream mark = AssetLoader.Open(new Uri("avares://Daynote.Desktop/Assets/daynote-favicon-v1.png")))
            using (var source = new Bitmap(mark))
            {
                context.DrawImage(source, new Rect(0, 0, pixels, pixels));
            }

            if (count > 0)
            {
                string text = count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);
                var label = new FormattedText(
                    text,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.ExtraBold),
                    pixels * 0.42,
                    Brushes.White);
                double height = pixels * 0.56;
                double width = Math.Max(height, label.Width + (pixels * 0.2));
                var badge = new Rect(pixels - width, 0, width, height);
                context.DrawRectangle(new SolidColorBrush(Color.Parse("#FFEE7F35")), null, badge, height / 2, height / 2);
                context.DrawText(label, new Point(badge.X + ((width - label.Width) / 2), (height - label.Height) / 2));
            }
        }

        var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        stream.Position = 0;
        return new WindowIcon(stream);
    }

    private static Pen Pen(IBrush brush, double thickness) =>
        new(brush, thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
}
