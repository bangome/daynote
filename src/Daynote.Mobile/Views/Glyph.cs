using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Daynote.Mobile.Views;

/// <summary>The line icons the phone draws, one per mark in the design.</summary>
public enum GlyphKind
{
    ChevronDown,
    ChevronLeft,
    ChevronRight,
    ChevronRightLarge,
    Back,
    Star,
    Check,
    Trash,
    TodoBox,
    Search,
    Plus,
    TabToday,
    TabSearch,
    TabLists,
    TabSettings,
}

/// <summary>
/// A line icon drawn from the design's own SVG: the same view box, the same path data and the same
/// stroke width, scaled as a whole to the control's size.
/// </summary>
/// <remarks>
/// A <see cref="Path"/> with <c>Stretch="Uniform"</c> cannot reproduce an SVG icon: it scales the
/// geometry's bounds rather than the view box, so a chevron drawn in the middle of a 10-unit box grew
/// to fill it, and its stroke scaled with whatever factor that happened to be. Drawing here keeps the
/// view box, which is what makes an 11-point chevron here the 11-point chevron in the design.
/// </remarks>
public sealed class Glyph : Control
{
    public static readonly StyledProperty<GlyphKind> KindProperty =
        AvaloniaProperty.Register<Glyph, GlyphKind>(nameof(Kind));

    /// <summary>The stroke colour, and the fill of the marks that are filled.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<Glyph, IBrush?>(nameof(Foreground), Brushes.Black);

    /// <summary>The fill inside a closed mark: the star, and the knobs of the settings sliders.</summary>
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<Glyph, IBrush?>(nameof(Fill));

    static Glyph()
    {
        AffectsRender<Glyph>(KindProperty, ForegroundProperty, FillProperty);
    }

    public GlyphKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        (double box, double stroke, Action<DrawingContext, Pen, IBrush?> draw) = Spec(Kind);
        double scale = Math.Min(Bounds.Width, Bounds.Height) / box;
        if (scale <= 0)
        {
            return;
        }

        var pen = new Pen(Foreground, stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        double dx = (Bounds.Width - (box * scale)) / 2;
        double dy = (Bounds.Height - (box * scale)) / 2;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(dx, dy)))
        {
            draw(context, pen, Fill);
        }
    }

    private static (double Box, double Stroke, Action<DrawingContext, Pen, IBrush?> Draw) Spec(GlyphKind kind) => kind switch
    {
        GlyphKind.ChevronDown => (12, 1.6, (c, p, _) => c.DrawGeometry(null, p, G("M3 4.5 L6 7.5 L9 4.5"))),
        GlyphKind.ChevronLeft => (10, 1.6, (c, p, _) => c.DrawGeometry(null, p, G("M6.5 1 L2.5 5 L6.5 9"))),
        GlyphKind.ChevronRight => (10, 1.6, (c, p, _) => c.DrawGeometry(null, p, G("M3.5 1 L7.5 5 L3.5 9"))),
        GlyphKind.ChevronRightLarge => (13, 1.6, (c, p, _) => c.DrawGeometry(null, p, G("M4 1.5 L9 6.5 L4 11.5"))),
        GlyphKind.Back => (18, 2, (c, p, _) => c.DrawGeometry(null, p, G("M12.5 2 L5.5 9 L12.5 16"))),
        GlyphKind.Star => (12, 0.8, (c, p, fill) => c.DrawGeometry(fill, p, G(StarPath))),
        GlyphKind.Check => (10, 1.8, (c, p, _) => c.DrawGeometry(null, p, G("M1.5 5.5 L4 8 L8.5 2.5"))),
        GlyphKind.Trash => (14, 1.1, (c, p, _) => c.DrawGeometry(null, p, G("M2 3.5 h10 M5.5 3.5 V2 h3 v1.5 M3.5 3.5 l0.7 8.5 h5.6 l0.7 -8.5"))),
        GlyphKind.TodoBox => (14, 1.5, (c, p, _) =>
        {
            c.DrawRectangle(null, p, new RoundedRect(new Rect(1.5, 1.5, 11, 11), 3));
            c.DrawGeometry(null, new Pen(p.Brush, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), G("M4.4 7.2 L6.2 9 L9.6 5"));
        }),
        GlyphKind.Search => (16, 1.6, (c, p, _) =>
        {
            c.DrawEllipse(null, p, new Point(7, 7), 4.8, 4.8);
            c.DrawLine(p, new Point(10.6, 10.6), new Point(14, 14));
        }),
        GlyphKind.Plus => (22, 2.3, (c, p, _) => c.DrawGeometry(null, p, G("M11 3 V19 M3 11 H19"))),
        GlyphKind.TabToday => (24, 1.8, (c, p, _) =>
        {
            c.DrawRectangle(null, p, new RoundedRect(new Rect(3.5, 5, 17, 15), 3.5));
            c.DrawGeometry(null, p, G("M3.5 10 H20.5 M8 3 V6.5 M16 3 V6.5"));
        }),
        GlyphKind.TabSearch => (24, 1.8, (c, p, _) =>
        {
            c.DrawEllipse(null, p, new Point(11, 11), 6.5, 6.5);
            c.DrawGeometry(null, p, G("M16 16 L20.5 20.5"));
        }),
        GlyphKind.TabLists => (24, 1.8, (c, p, _) =>
        {
            c.DrawRectangle(null, p, new RoundedRect(new Rect(3.5, 3.5, 17, 17), 4));
            c.DrawGeometry(null, p, G("M8 12.2 L10.8 15 L16.2 9"));
        }),
        GlyphKind.TabSettings => (24, 1.8, (c, p, fill) =>
        {
            c.DrawGeometry(null, p, G("M4 7 H20 M4 17 H20"));
            c.DrawEllipse(fill, p, new Point(9, 7), 2.4, 2.4);
            c.DrawEllipse(fill, p, new Point(15, 17), 2.4, 2.4);
        }),
        _ => (1, 1, (_, _, _) => { }),
    };

    private const string StarPath = "M6 0.8 L7.5 4.2 L11.2 4.6 L8.4 7 L9.2 10.7 L6 8.8 L2.8 10.7 L3.6 7 L0.8 4.6 L4.5 4.2 Z";

    private static readonly Dictionary<string, Geometry> Cache = [];

    private static Geometry G(string data)
    {
        if (!Cache.TryGetValue(data, out Geometry? geometry))
        {
            geometry = Geometry.Parse(data);
            Cache[data] = geometry;
        }

        return geometry;
    }
}
