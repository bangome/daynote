using Avalonia.Data.Converters;

namespace Daynote.Desktop.Views;

/// <summary>
/// Bool-to-glyph converters. Only the todo checkmark is left: the favourite star used to swap ★ for
/// ☆ here, and it is now a drawn <c>Daynote.Geo.Star</c> that fills instead, so it matches the marks
/// beside it in weight and size.
/// </summary>
public static class Glyphs
{
    public static readonly IValueConverter Check =
        new FuncValueConverter<bool, string>(checkedState => checkedState ? "✓" : string.Empty);
}
