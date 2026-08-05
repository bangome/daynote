using System.ComponentModel;
using System.Text.Json;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Daynote.Core.Search;
using ModelContextProtocol.Server;

namespace Daynote.Mcp;

/// <summary>
/// MCP tools exposing the user's local Daynote daily notes. Read tools query the SQLite store the WPF
/// app owns; write tools go through the same <see cref="INoteRepository"/> save/create/delete path so the
/// running app's revision and projection rules are honoured. Results are compact JSON strings.
/// </summary>
[McpServerToolType]
public static class NoteTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    [McpServerTool(Name = "search_notes", ReadOnly = true), Description(
        "Full-text search across the user's Daynote notes, clipboard, and files. Returns the top matches with date, source id, title, and snippet.")]
    public static async Task<string> SearchNotes(
        SearchService search,
        [Description("Search text.")] string query,
        [Description("Maximum matches to return (default 20).")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "Error: a non-empty query is required.";
        }

        SearchPage page = await search.SearchAsync(query, pageNumber: 0, cancellationToken).ConfigureAwait(false);
        var matches = page.Results
            .Take(Math.Max(0, limit))
            .Select(result => new
            {
                date = result.LocalDate.ToString(),
                id = result.SourceId,
                source = result.SourceType.ToString(),
                title = result.Title,
                snippet = result.Snippet,
            });
        return JsonSerializer.Serialize(matches, JsonOptions);
    }

    [McpServerTool(Name = "get_notes_for_date", ReadOnly = true), Description(
        "List the real notes stored on a single date. Date is an ISO calendar date (yyyy-MM-dd).")]
    public static async Task<string> GetNotesForDate(
        INoteRepository repository,
        [Description("Date in ISO yyyy-MM-dd form.")] string date,
        CancellationToken cancellationToken = default)
    {
        DomainResult<LocalDate> parsed = LocalDate.Parse(date);
        if (!parsed.IsSuccess)
        {
            return $"Error: {parsed.Error.Message}";
        }

        DayWorkspace workspace = await repository
            .GetDayWorkspaceStateAsync(parsed.Value, cancellationToken).ConfigureAwait(false);
        var notes = workspace.Notes.Notes
            .Where(note => !note.IsProjection)
            .Select(note => new
            {
                id = note.Id?.Value,
                title = note.Title,
                body = note.Body,
                isFavorite = note.IsFavorite,
            });
        return JsonSerializer.Serialize(notes, JsonOptions);
    }

    [McpServerTool(Name = "list_recent_notes", ReadOnly = true), Description(
        "List the most recent notes across all dates, newest date first.")]
    public static async Task<string> ListRecentNotes(
        INoteRepository repository,
        [Description("Maximum notes to return (default 20).")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NoteSummary> all = await repository
            .GetAllNotesAsync(cancellationToken).ConfigureAwait(false);
        var notes = all
            .Take(Math.Max(0, limit))
            .Select(summary => new
            {
                id = summary.Id,
                date = summary.LocalDate.ToString(),
                title = summary.Title,
                snippet = Snippet(summary.Body),
                isFavorite = summary.IsFavorite,
            });
        return JsonSerializer.Serialize(notes, JsonOptions);
    }

    [McpServerTool(Name = "create_note", Idempotent = false, Destructive = false), Description(
        "Create a new note on a date. Date is ISO yyyy-MM-dd. Returns the new note id and date.")]
    public static async Task<string> CreateNote(
        INoteRepository repository,
        [Description("Date in ISO yyyy-MM-dd form.")] string date,
        [Description("Note body text.")] string body,
        [Description("Optional note title.")] string? title = null,
        CancellationToken cancellationToken = default)
    {
        DomainResult<LocalDate> parsed = LocalDate.Parse(date);
        if (!parsed.IsSuccess)
        {
            return $"Error: {parsed.Error.Message}";
        }

        LocalDate localDate = parsed.Value;
        NoteId newId = NoteId.Create(Guid.NewGuid()).Value;

        try
        {
            DayWorkspace workspace = await repository
                .CreateNoteAsync(localDate, default, newId, cancellationToken).ConfigureAwait(false);
            await repository.SaveNoteAsync(
                new NoteSaveRequest(
                    newId,
                    localDate,
                    title ?? string.Empty,
                    body ?? string.Empty,
                    workspace.RevisionOf(newId),
                    IsNew: false,
                    HasCustomTitle: !string.IsNullOrWhiteSpace(title)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (RecoverableNoteException ex)
        {
            return $"Error: {ex.Message}";
        }

        return JsonSerializer.Serialize(
            new { id = newId.Value, date = localDate.ToString() }, JsonOptions);
    }

    [McpServerTool(Name = "update_note", Idempotent = false, Destructive = false), Description(
        "Update an existing note's body and/or title. Omitted fields are left unchanged. Returns the note id.")]
    public static async Task<string> UpdateNote(
        INoteRepository repository,
        [Description("Note id (GUID).")] string noteId,
        [Description("New body text. Omit to keep the current body.")] string? body = null,
        [Description("New title. Omit to keep the current title.")] string? title = null,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(noteId, out Guid guid) || !NoteId.Create(guid).IsSuccess)
        {
            return "Error: a valid note id (GUID) is required.";
        }

        NoteId id = NoteId.Create(guid).Value;
        IReadOnlyList<NoteSummary> all = await repository
            .GetAllNotesAsync(cancellationToken).ConfigureAwait(false);
        NoteSummary? located = all.Cast<NoteSummary?>().FirstOrDefault(s => s!.Value.Id == guid);
        if (located is not { } summary)
        {
            return "Error: the note was not found.";
        }

        try
        {
            DayWorkspace workspace = await repository
                .GetDayWorkspaceStateAsync(summary.LocalDate, cancellationToken).ConfigureAwait(false);
            Note? current = workspace.Notes.Notes.FirstOrDefault(note => note.Id == id);
            bool currentHasCustomTitle = current?.HasCustomTitle ?? false;

            string newTitle = title ?? summary.Title;
            string newBody = body ?? summary.Body;
            bool hasCustomTitle = title != null || currentHasCustomTitle;

            await repository.SaveNoteAsync(
                new NoteSaveRequest(
                    id,
                    summary.LocalDate,
                    newTitle,
                    newBody,
                    workspace.RevisionOf(id),
                    IsNew: false,
                    HasCustomTitle: hasCustomTitle),
                cancellationToken).ConfigureAwait(false);
        }
        catch (RecoverableNoteException ex)
        {
            return $"Error: {ex.Message}";
        }

        return JsonSerializer.Serialize(new { id = guid }, JsonOptions);
    }

    [McpServerTool(Name = "delete_note", Idempotent = true, Destructive = true), Description(
        "Delete a note by id (GUID). Returns a confirmation.")]
    public static async Task<string> DeleteNote(
        INoteRepository repository,
        [Description("Note id (GUID).")] string noteId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(noteId, out Guid guid) || !NoteId.Create(guid).IsSuccess)
        {
            return "Error: a valid note id (GUID) is required.";
        }

        NoteId id = NoteId.Create(guid).Value;
        IReadOnlyList<NoteSummary> all = await repository
            .GetAllNotesAsync(cancellationToken).ConfigureAwait(false);
        NoteSummary? located = all.Cast<NoteSummary?>().FirstOrDefault(s => s!.Value.Id == guid);
        if (located is not { } summary)
        {
            return "Error: the note was not found.";
        }

        try
        {
            await repository.DeleteNoteAsync(summary.LocalDate, id, cancellationToken).ConfigureAwait(false);
        }
        catch (RecoverableNoteException ex)
        {
            return $"Error: {ex.Message}";
        }

        return JsonSerializer.Serialize(
            new { deleted = guid, date = summary.LocalDate.ToString() }, JsonOptions);
    }

    private static string Snippet(string body)
    {
        const int max = 200;
        if (string.IsNullOrEmpty(body))
        {
            return string.Empty;
        }

        string collapsed = body.ReplaceLineEndings(" ").Trim();
        return collapsed.Length <= max ? collapsed : collapsed[..max] + "…";
    }
}
