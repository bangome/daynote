using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Daynote.App.Shell.Product;

/// <summary>
/// Attached properties that render <see cref="Text"/> into a <see cref="TextBlock"/>'s inlines with every
/// case-insensitive occurrence of <see cref="Query"/> emphasized (accent + semibold). Used by the search
/// dropdown so a result shows WHERE the keyword matched and highlights it. Setting either property
/// rebuilds the inlines; character-ellipsis trimming still applies to the composed runs.
/// </summary>
public static class SearchHighlight
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(SearchHighlight),
        new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty QueryProperty = DependencyProperty.RegisterAttached(
        "Query", typeof(string), typeof(SearchHighlight),
        new PropertyMetadata(string.Empty, OnChanged));

    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);

    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);

    public static void SetQuery(DependencyObject element, string value) => element.SetValue(QueryProperty, value);

    public static string GetQuery(DependencyObject element) => (string)element.GetValue(QueryProperty);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block)
        {
            return;
        }

        string text = GetText(block) ?? string.Empty;
        string query = (GetQuery(block) ?? string.Empty).Trim();

        block.Inlines.Clear();
        if (text.Length == 0)
        {
            return;
        }

        if (query.Length == 0)
        {
            block.Inlines.Add(new Run(text));
            return;
        }

        var accent = block.TryFindResource("Daynote.Product.Brush.Accent") as System.Windows.Media.Brush;
        int cursor = 0;
        while (cursor < text.Length)
        {
            int match = text.IndexOf(query, cursor, StringComparison.InvariantCultureIgnoreCase);
            if (match < 0)
            {
                block.Inlines.Add(new Run(text[cursor..]));
                break;
            }

            if (match > cursor)
            {
                block.Inlines.Add(new Run(text[cursor..match]));
            }

            var hit = new Run(text.Substring(match, query.Length)) { FontWeight = FontWeights.SemiBold };
            if (accent is not null)
            {
                hit.Foreground = accent;
            }

            block.Inlines.Add(hit);
            cursor = match + query.Length;
        }
    }
}
