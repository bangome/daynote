using Daynote.Core.Domain;
using Daynote.Core.Search;
using Daynote.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Search;

public sealed class SqliteSearchRepository(SqliteDatabase database) : ISearchRepository
{
    private const string Projection =
        "SELECT d.source_type,d.source_id,d.local_date,d.title,d.body,";
    private const string FtsSql = Projection +
        "bm25(search_fts,8.0,1.0,8.0,1.0) AS score FROM search_fts " +
        "JOIN search_documents d ON d.rowid=search_fts.rowid " +
        "WHERE search_fts MATCH $query " +
        "ORDER BY score ASC,d.local_date DESC,d.source_type ASC,d.source_id ASC LIMIT $limit OFFSET $offset;";
    private const string SubstringSql = Projection +
        "CASE WHEN d.title_folded LIKE $pattern ESCAPE '\\' THEN 0.0 ELSE 1.0 END AS score " +
        "FROM search_documents d WHERE d.title_folded LIKE $pattern ESCAPE '\\' " +
        "OR d.body_folded LIKE $pattern ESCAPE '\\' " +
        "ORDER BY score ASC,d.local_date DESC,d.source_type ASC,d.source_id ASC LIMIT $limit OFFSET $offset;";

    private readonly SqliteDatabase database = database ?? throw new ArgumentNullException(nameof(database));

    public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        SearchQuery query,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        cancellationToken.ThrowIfCancellationRequested();
        if (query.IsEmpty) return ValueTask.FromResult<IReadOnlyList<SearchResult>>([]);

        using SqliteConnection connection = database.OpenReadConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = query.Strategy == SearchStrategy.Trigram ? FtsSql : SubstringSql;
        object parameter = query.Strategy == SearchStrategy.Trigram
            ? $"\"{query.FoldedText.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : $"%{EscapeLike(query.FoldedText)}%";
        command.Parameters.AddWithValue(
            query.Strategy == SearchStrategy.Trigram ? "$query" : "$pattern", parameter);
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);

        using SqliteDataReader reader = command.ExecuteReader();
        var results = new List<SearchResult>();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(ReadResult(reader, query.NormalizedText));
        }
        return ValueTask.FromResult<IReadOnlyList<SearchResult>>(results);
    }

    private static SearchResult ReadResult(SqliteDataReader reader, string term)
    {
        SearchSourceType type = reader.GetString(0) switch
        {
            "note" => SearchSourceType.Note,
            "clipboard" => SearchSourceType.ClipboardText,
            "file" => SearchSourceType.File,
            _ => throw new InvalidDataException("Search document source type is invalid."),
        };
        if (!Guid.TryParse(reader.GetString(1), out Guid id) || id == Guid.Empty)
            throw new InvalidDataException("Search document source ID is invalid.");
        DomainResult<LocalDate> date = LocalDate.Parse(reader.GetString(2));
        if (!date.IsSuccess) throw new InvalidDataException("Search document date is invalid.");
        string title = reader.GetString(3);
        string body = reader.GetString(4);
        return new SearchResult(type, id, date.Value, title, Snippet(body.Length == 0 ? title : body, term), reader.GetDouble(5));
    }

    private static string EscapeLike(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    private const int SnippetWindow = 160;
    private const int SnippetLead = 32;

    /// <summary>
    /// A one-line preview windowed around the first case-insensitive occurrence of <paramref name="term"/>,
    /// so the result shows the matched text (with leading/trailing ellipses when it is clipped). Falls back
    /// to the start of the value when the term is empty or not present (e.g. an FTS trigram-only match).
    /// </summary>
    private static string Snippet(string value, string term)
    {
        string singleLine = value.Replace('\r', ' ').Replace('\n', ' ');
        if (singleLine.Length == 0)
        {
            return string.Empty;
        }

        int match = term.Length == 0
            ? -1
            : singleLine.IndexOf(term, StringComparison.InvariantCultureIgnoreCase);
        if (match < 0)
        {
            return singleLine.Length <= SnippetWindow
                ? singleLine
                : string.Concat(singleLine.AsSpan(0, SnippetWindow - 1), "…");
        }

        int start = Math.Max(0, match - SnippetLead);
        int length = Math.Min(SnippetWindow, singleLine.Length - start);
        string slice = singleLine.Substring(start, length);
        string prefix = start > 0 ? "…" : string.Empty;
        string suffix = start + length < singleLine.Length ? "…" : string.Empty;
        return string.Concat(prefix, slice, suffix);
    }
}
