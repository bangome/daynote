using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Daynote.Core.Sync;

namespace Daynote.Infrastructure.Sync;

/// <summary>
/// Stores the session in <c>credentials.dat</c> under the Daynote data root, sealed by an
/// <see cref="ISecretProtector"/> so only this OS user on this machine can read it back.
/// </summary>
/// <remarks>
/// Not the <c>settings</c> table, on purpose: the database is copied verbatim into the plaintext
/// backup zip, so a key kept there would be exported with every backup. <c>BackupService</c> packs
/// only the database and the asset trees, which keeps this file out — pinned by a test, because the
/// day someone changes it to "zip the whole data root" it would start shipping the data key.
/// <para>
/// The protector ties the blob to this user on this machine (DPAPI on Windows, a Keychain-held key on
/// macOS). Copying the file to another computer yields nothing, and a failure to decrypt is treated as
/// "signed out" rather than an error: that is what a restored profile or a new machine looks like.
/// </para>
/// </remarks>
public sealed class ProtectedFileSyncSessionStore : ISyncSessionStore
{
    internal const string FileName = "credentials.dat";

    /// <summary>
    /// Extra entropy mixed into the seal so another application running as the same user cannot decrypt
    /// this file just by pointing the same OS facility at it.
    /// </summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("daynote-v1-credentials");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly string path;
    private readonly ISecretProtector protector;
    private readonly SemaphoreSlim gate = new(1, 1);

    public ProtectedFileSyncSessionStore(string dataRoot, ISecretProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        this.protector = protector ?? throw new ArgumentNullException(nameof(protector));
        path = Path.Combine(Path.GetFullPath(dataRoot), FileName);
    }

    public async ValueTask<SyncCredentials?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return ReadUnsynchronized();
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask SaveAsync(
        SyncCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WriteUnsynchronized(new Persisted(
                credentials.UserId,
                credentials.Email,
                credentials.AccessToken,
                credentials.AccessExpiresUtc,
                credentials.RefreshToken,
                credentials.DekGeneration,
                // Null while the account is locked: the session must still persist, so the record
                // carries no key rather than not existing.
                credentials.DataKey is { } key ? Base64Url.EncodeToString(key.Span) : null,
                credentials.Protection));
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask UpdateTokensAsync(
        string accessToken,
        DateTimeOffset accessExpiresUtc,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Persisted? existing = ReadPersistedUnsynchronized();
            if (existing is null)
            {
                // Signed out between the refresh starting and finishing. Writing a token-only record
                // would create a session with no data key, which nothing downstream could use.
                return;
            }

            WriteUnsynchronized(existing with
            {
                AccessToken = accessToken,
                AccessExpiresUtc = accessExpiresUtc,
                RefreshToken = refreshToken,
            });
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private SyncCredentials? ReadUnsynchronized()
    {
        Persisted? persisted = ReadPersistedUnsynchronized();
        if (persisted is null)
        {
            return null;
        }

        KeyMaterial? dataKey = null;
        if (persisted.DataKey is { } encoded)
        {
            byte[]? key = Base64Url.IsValid(encoded) ? Base64Url.DecodeFromChars(encoded) : null;
            if (key is null || key.Length != KeyMaterial.Length)
            {
                // A stored key of the wrong shape is corruption, not a locked account. Treating it as
                // "signed out" is safer than handing a malformed key to the crypto layer.
                CryptographicOperations.ZeroMemory(key ?? []);
                return null;
            }

            dataKey = KeyMaterial.Adopt(key);
        }

        return new SyncCredentials(
            persisted.UserId,
            persisted.Email,
            persisted.AccessToken,
            persisted.AccessExpiresUtc,
            persisted.RefreshToken,
            persisted.DekGeneration,
            dataKey,
            persisted.Protection);
    }

    private Persisted? ReadPersistedUnsynchronized()
    {
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] plaintext;
        try
        {
            plaintext = protector.Unprotect(File.ReadAllBytes(path), Entropy);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException)
        {
            // A different OS user, a different machine, or a truncated file. All of these mean
            // "not signed in here", which is a state the app already handles.
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Persisted>(plaintext, Json);
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private void WriteUnsynchronized(Persisted record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(record, Json);
        try
        {
            byte[] sealedBytes = protector.Protect(plaintext, Entropy);

            // Write then rename: a crash mid-write must not leave a half-file that reads as signed
            // out while the account is still live on the server.
            string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, sealedBytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>
    /// The stored shape. <see cref="Protection"/> defaults to
    /// <see cref="KeyProtection.Server"/> so a file written before the opt-in lock existed still
    /// reads back as the account it describes.
    /// </summary>
    private sealed record Persisted(
        string UserId,
        string Email,
        string AccessToken,
        DateTimeOffset AccessExpiresUtc,
        string RefreshToken,
        int DekGeneration,
        string? DataKey,
        KeyProtection Protection = KeyProtection.Server);
}
