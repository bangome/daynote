namespace Daynote.Core.Agenda;

/// <summary>
/// The colour a list's check rings are drawn in, on a light and on a dark background.
/// </summary>
/// <remarks>
/// The widget and watch designs draw "체크 링 색 = 리스트 색", and lists have no colour of their own
/// yet — docs/TODOS.md §12 defers the column and its menu. Until they do, a list's colour is its
/// place in the sidebar's order, so every surface that draws one agrees without anything being
/// stored: the built-in list first (the brand orange), then the others in the order the user put
/// them. When the column arrives it replaces this lookup and the surfaces keep reading a colour.
/// </remarks>
public static class AgendaListPalette
{
    /// <summary>Light and dark pairs, from the widget design's rings and event bars.</summary>
    private static readonly (string Light, string Dark)[] Colors =
    [
        ("#ee7f35", "#ff9a52"),
        ("#5a64c4", "#9ca1c6"),
        ("#2fa36b", "#3dd68c"),
        ("#c4506b", "#ff8aa0"),
        ("#2a8fa3", "#5cc8dc"),
        ("#a07a1f", "#e8bf5a"),
    ];

    /// <summary>The colour for the list at <paramref name="position"/> in the sidebar's order.</summary>
    public static (string Light, string Dark) ForPosition(int position) =>
        Colors[((position % Colors.Length) + Colors.Length) % Colors.Length];
}
