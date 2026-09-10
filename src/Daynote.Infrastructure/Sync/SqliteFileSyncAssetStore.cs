using Daynote.Core.Files;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Sync;

/// <summary>
/// The sync engine's view of this device's attachment bytes (docs/CLOUD_SYNC.md §5).
/// </summary>
/// <remarks>
/// A thin join of the two things that already exist: <c>file_assets</c>, which maps a content hash
/// to a path, and <see cref="IFileAssetStore"/>, which owns the bytes at that path. The engine
/// speaks only in content hashes, so this is where a hash becomes a file.
/// </remarks>
public sealed class SqliteFileSyncAssetStore : ISyncAssetStore
{
    private readonly SqliteDatabase database;
    private readonly IFileAssetStore files;

    public SqliteFileSyncAssetStore(SqliteDatabase database, IFileAssetStore files)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.files = files ?? throw new ArgumentNullException(nameof(files));
    }

    public async ValueTask<bool> ContainsAsync(
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        string? path = ReadPath(contentHash);
        return path is not null
            && await files.ExistsAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<byte[]?> ReadAsync(
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        string? path = ReadPath(contentHash);
        return path is null
            ? null
            : await files.ReadAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes downloaded bytes, refusing anything that does not hash to the name it arrived under.
    /// </summary>
    /// <remarks>
    /// The check costs nothing to write because <see cref="IFileAssetStore.PrepareAsync"/> already
    /// hashes what it is given and files it by that hash — so a substituted object lands under its
    /// own name, not the one we asked for, and comparing the two is the whole verification. The
    /// misfiled copy is then deleted rather than left as an orphan for reconciliation to find.
    /// </remarks>
    public async ValueTask<bool> SaveAsync(
        string contentHash,
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        // The extension the merge chose for this hash, so the downloaded copy lands where the
        // `file_assets` row already says it will.
        string extension = Path.GetExtension(ReadPath(contentHash) ?? string.Empty);

        using var stream = new MemoryStream(plaintext.ToArray(), writable: false);
        PreparedFileAsset prepared = await files
            .PrepareAsync(stream, extension, cancellationToken)
            .ConfigureAwait(false);

        if (string.Equals(prepared.Hash, contentHash, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        await files.DeleteAsync(prepared.RelativePath, cancellationToken).ConfigureAwait(false);
        return false;
    }

    private string? ReadPath(string contentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        using SqliteConnection connection = database.OpenReadConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT relative_path FROM file_assets WHERE hash = $hash;";
        command.Parameters.AddWithValue("$hash", contentHash);
        return command.ExecuteScalar() as string;
    }
}
