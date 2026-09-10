using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Daynote.Core.Sync;

namespace Daynote.Infrastructure.Sync;

/// <summary>
/// The attachment calls (docs/CLOUD_SYNC.md §5, Phase 7).
/// </summary>
/// <remarks>
/// The two byte-moving calls do not go through <c>SendAsync&lt;T&gt;</c>: it parses every response
/// as JSON, and an attachment is neither JSON nor small enough to want buffered twice. They repeat
/// its 401-refresh-once rule instead, which is the one behaviour they do need.
/// </remarks>
public sealed partial class HttpSyncApiClient
{
    public async ValueTask<FilePushResult> PushFilesAsync(
        FilePushRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = new FilePushBody(
            [.. request.Files.Select(file => new FileBody(
                file.Id,
                file.Payload,
                file.BlindedKey,
                file.StoredBytes,
                SyncTimestamps.ToWire(file.UpdatedUtc)))],
            [.. request.Tombstones.Select(tombstone => new TombstoneBody(
                tombstone.Kind == SyncEntityKind.Note ? "note" : "file",
                tombstone.Id,
                SyncTimestamps.ToWire(tombstone.DeletedUtc)))]);

        FilePushBodyResponse response = await SendAsync<FilePushBodyResponse>(
            () => new HttpRequestMessage(HttpMethod.Post, "v1/files/push")
            {
                Content = JsonContent.Create(body, options: Json),
            },
            cancellationToken).ConfigureAwait(false);

        return new FilePushResult(
            response.AcceptedFiles ?? [],
            response.RejectedFiles ?? [],
            response.AcceptedTombstones ?? [],
            response.RejectedTombstones ?? [],
            response.FilesBlocked,
            RequireTimestamp(response.ServerUtc));
    }

    public async ValueTask UploadAssetAsync(
        string blindedKey,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blindedKey);

        using HttpResponseMessage response = await SendBytesAsync(
            () =>
            {
                var content = new ReadOnlyMemoryContent(body);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                return new HttpRequestMessage(HttpMethod.Put, $"v1/assets/{blindedKey}") { Content = content };
            },
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new SyncTransportException(
                $"The attachment could not be stored ({(int)response.StatusCode}).",
                (int)response.StatusCode);
        }
    }

    public async ValueTask<byte[]?> DownloadAssetAsync(
        string blindedKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blindedKey);

        using HttpResponseMessage response = await SendBytesAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"v1/assets/{blindedKey}"),
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Not an error. The metadata and the bytes are two calls, so another device's file row
            // can reach us before its object does; the caller retries on the next run.
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new SyncTransportException(
                $"The attachment could not be fetched ({(int)response.StatusCode}).",
                (int)response.StatusCode);
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a request whose response is bytes, refreshing the token once on a 401.
    /// </summary>
    /// <remarks>
    /// Returns the response rather than throwing on a bad status, because the two callers disagree
    /// about what a bad status means: a 404 is a failure for an upload and an ordinary answer for
    /// a download.
    /// </remarks>
    private async ValueTask<HttpResponseMessage> SendBytesAsync(
        Func<HttpRequestMessage> factory,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 2; attempt += 1)
        {
            using HttpRequestMessage message = factory();
            message.Headers.Authorization = new AuthenticationHeaderValue(
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
                throw new SyncTransportException("The sync service could not be reached.", null, exception);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                response.Dispose();
                if (!await tokens.TryRefreshAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new SyncTransportException(
                        "The session expired and could not be renewed.",
                        (int)HttpStatusCode.Unauthorized);
                }

                continue;
            }

            return response;
        }

        throw new SyncTransportException(
            "The session expired and could not be renewed.",
            (int)HttpStatusCode.Unauthorized);
    }

    private sealed record FilePushBody(
        IReadOnlyList<FileBody> Files,
        IReadOnlyList<TombstoneBody> Tombstones);

    private sealed record FileBody(
        string Id,
        string Payload,
        string BlindedKey,
        long StoredBytes,
        string UpdatedUtc);

    private sealed record FilePushBodyResponse(
        IReadOnlyList<string>? AcceptedFiles,
        IReadOnlyList<string>? RejectedFiles,
        IReadOnlyList<string>? AcceptedTombstones,
        IReadOnlyList<string>? RejectedTombstones,
        bool FilesBlocked,
        string? ServerUtc);
}
