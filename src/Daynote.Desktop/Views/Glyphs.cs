using Avalonia.Data.Converters;

namespace Daynote.Desktop.Views;

/// <summary>Bool-to-glyph converters for the few icon toggles the shell has.</summary>
public static class Glyphs
{
    public static readonly IValueConverter Star =
        new FuncValueConverter<bool, string>(favorite => favorite ? "★" : "☆");

    public static readonly IValueConverter Check =
        new FuncValueConverter<bool, string>(checkedState => checkedState ? "✓" : string.Empty);
}
