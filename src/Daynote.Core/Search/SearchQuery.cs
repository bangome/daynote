using System.Text;

namespace Daynote.Core.Search;

public enum SearchStrategy
{
    None = 0,
    LiteralSubstring = 1,
    Trigram = 2,
}

public sealed record SearchQuery
{
    private SearchQuery(string normalizedText, string foldedText, int unicodeScalarCount, SearchStrategy strategy)
    {
        NormalizedText = normalizedText;
        FoldedText = foldedText;
        UnicodeScalarCount = unicodeScalarCount;
        Strategy = strategy;
    }

    public string NormalizedText { get; }
    public string FoldedText { get; }
    public int UnicodeScalarCount { get; }
    public SearchStrategy Strategy { get; }
    public bool IsEmpty => Strategy == SearchStrategy.None;

    public static SearchQuery Create(string? text)
    {
        string normalized = (text ?? string.Empty).Normalize(NormalizationForm.FormC);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new SearchQuery(normalized, normalized, 0, SearchStrategy.None);
        }

        int count = normalized.EnumerateRunes().Count();
        return new SearchQuery(
            normalized,
            normalized.ToUpperInvariant().Normalize(NormalizationForm.FormC),
            count,
            count >= 3 ? SearchStrategy.Trigram : SearchStrategy.LiteralSubstring);
    }
}
