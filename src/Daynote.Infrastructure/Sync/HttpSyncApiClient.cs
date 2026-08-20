using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daynote.Core.Sync;

namespace Daynote.Infrastructure.Sync;

/// <summary>
/// Supplies the bearer token for a request and refreshes it once on a 401.
/// </summary>
/// <remarks>
/// Token lifecycle lives above this client (with the account view model), so the transport does not
/// have to know about passwords, refresh rotation, or the locked state.
/// </remarks>
public interface ISyncTokenProvider
{
    ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>Exchanges the refresh token. Returns false when the user must sign in again.</summary>
    ValueTask<bool> TryRefreshAsync(CancellationToken cancellationToken = default);
}

public sealed class SyncTransportException : Exception
{
    public SyncTransportException(string message, HttpStatusCode? status = null, Exception? inner = null)
        : base(message, inner)
    {
        Status = status;
    }

    public HttpStatusCode? Status { get; }

    /// <summary>
    /// True when the caller should stop and ask the user to sign in, rather than retrying. Anything
    /// else is treated as "offline for now" and retried on the next cycle.
    /// </summary>
    public bool RequiresSignIn => Status is HttpStatusCode.Unauthorized;
}

public sealed class HttpSyncApiClient : ISyncApiClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient http;
    private readonly ISyncTokenProvider tokens;

    public HttpSyncApiClient(HttpClient http, ISyncTokenProvider tokens)
    {
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        this.tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    public async ValueTask<PushResult> PushAsync(
        PushRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = new PushBody(
            [.. request.Notes.Select(note => new NoteBody(
                note.Id,
                note.Payload,
                SyncTimestamps.ToWire(note.UpdatedUtc)))],
            [.. request.Tombstones.Select(tombstone => new TombstoneBody(
                tombstone.Kind == SyncEntityKind.Note ? "note" : "file",
                tombstone.Id,
                SyncTimestamps.ToWire(tombstone.DeletedUtc)))]);

        PushBodyResponse response = await SendAsync<PushBodyResponse>(
            () => new HttpRequestMessage(HttpMethod.Post, "v1/sync/push")
            {
                Content = JsonContent.Create(body, options: Json),
            },
            cancellationToken).ConfigureAwait(false);

        return new PushResult(
            response.AcceptedNotes ?? [],
            response.RejectedNotes ?? [],
            response.AcceptedTombstones ?? [],
            response.RejectedTombstones ?? [],
            response.Cursor,
            RequireTimestamp(response.ServerUtc));
    }

    public async ValueTask<PullResult> PullAsync(
        long since,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(since);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        string path = string.Create(
            CultureInfo.InvariantCulture,
            $"v1/sync/pull?since={since}&limit={limit}");

        PullBodyResponse response = await SendAsync<PullBodyResponse>(
            () => new HttpRequestMessage(HttpMethod.Get, path),
            cancellationToken).ConfigureAwait(false);

        var changes = new List<PullChange>(response.Changes?.Count ?? 0);
        foreach (ChangeBody change in response.Changes ?? [])
        {
            changes.Add(new PullChange(
                change.Seq,
                change.Entity == "file" ? SyncEntityKind.File : SyncEntityKind.Note,
                change.Id,
                change.Payload,
                RequireTimestamp(change.UpdatedUtc),
                change.DeletedUtc is null ? null : RequireTimestamp(change.DeletedUtc)));
        }

        return new PullResult(
            changes,
            response.Cursor,
            response.HasMore,
            RequireTimestamp(response.ServerUtc));
    }

    /// <summary>
    /// Sends with the current token, and on a 401 refreshes and retries exactly once. Retrying more
    /// than once would turn a revoked session into a refresh loop.
    /// </summary>
    private async ValueTask<T> SendAsync<T>(
        Func<HttpRequestMessage> factory,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 2; attempt += 1)
        {
            using HttpRequestMessage message = factory();
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                await tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false));

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                && !cancellationToken.IsCancellationRequested)
            {
                // Offline, DNS failure, or a timeout. Not a protocol error: the caller retries later.
                throw new SyncTransportException("The sync service could not be reached.", null, exception);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    if (!await tokens.TryRefreshAsync(cancellationToken).ConfigureAwait(false))
                    {
                        throw new SyncTransportException(
                            "The session expired and could not be renewed.",
                            HttpStatusCode.Unauthorized);
                    }

                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new SyncTransportException(
                        $"The sync service rejected the request ({(int)response.StatusCode}).",
                        response.StatusCode);
                }

                T? parsed = await response.Content
                    .ReadFromJsonAsync<T>(Json, cancellationToken)
                    .ConfigureAwait(false);
                return parsed ?? throw new SyncTransportException("The sync service returned no body.");
            }
        }

        throw new SyncTransportException(
            "The session expired and could not be renewed.",
            HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A timestamp we cannot parse is a protocol mismatch, not a note-level problem: accepting a
    /// default would silently corrupt every last-write-wins comparison that follows.
    /// </summary>
    private static DateTimeOffset RequireTimestamp(string? value)
    {
        var parsed = SyncTimestamps.ParseWire(value);
        return parsed.IsSuccess
            ? parsed.Value
            : throw new SyncTransportException($"The sync service sent an unreadable timestamp: '{value}'.");
    }

    private sealed record PushBody(IReadOnlyList<NoteBody> Notes, IReadOnlyList<TombstoneBody> Tombstones);

    private sealed record NoteBody(string Id, string Payload, string UpdatedUtc);

    private sealed record TombstoneBody(string Entity, string Id, string DeletedUtc);

    private sealed record PushBodyResponse(
        IReadOnlyList<string>? AcceptedNotes,
        IReadOnlyList<string>? RejectedNotes,
        IReadOnlyList<string>? AcceptedTombstones,
        IReadOnlyList<string>? RejectedTombstones,
        long Cursor,
        string? ServerUtc);

    private sealed record PullBodyResponse(
        IReadOnlyList<ChangeBody>? Changes,
        long Cursor,
        bool HasMore,
        string? ServerUtc);

    private sealed record ChangeBody(
        long Seq,
        string Entity,
        string Id,
        string? Payload,
        string UpdatedUtc,
        string? DeletedUtc);
}
