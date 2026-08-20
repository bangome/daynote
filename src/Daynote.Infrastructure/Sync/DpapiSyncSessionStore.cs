using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Daynote.Core.Sync;

namespace Daynote.Infrastructure.Sync;

/// <summary>
/// Stores the session in <c>credentials.dat</c> under the Daynote data root, encrypted with Windows
/// DPAPI for the current user.
/// </summary>
/// <remarks>
/// Not the <c>settings</c> table, on purpose: the database is copied verbatim into the plaintext
/// backup zip, so a key kept there would be exported with every backup. <c>BackupService</c> packs
/// only the database and the asset trees, which keeps this file out — pinned by a test, because the
/// day someone changes it to "zip the whole data root" it would start shipping the data key.
/// <para>
/// DPAPI ties the blob to this Windows user on this machine. Copying the file to another PC yields
/// nothing, and a failure to decrypt is treated as "signed out" rather than an error: that is what a
/// restored profile or a new machine looks like.
/// </para>
/// </remarks>
public sealed class DpapiSyncSessionStore : ISyncSessionStore
{
    internal const string FileName = "credentials.dat";

    /// <summary>
    /// Extra entropy mixed into DPAPI so another application running as the same user cannot decrypt
    /// this file just by pointing ProtectedData at it.
    /// </summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("daynote-v1-credentials");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);

    public DpapiSyncSessionStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
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
                Base64Url.EncodeToString(credentials.DataKey.Span)));
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

        byte[]? key = Base64Url.IsValid(persisted.DataKey)
            ? Base64Url.DecodeFromChars(persisted.DataKey)
            : null;
        if (key is null || key.Length != KeyMaterial.Length)
        {
            CryptographicOperations.ZeroMemory(key ?? []);
            return null;
        }

        return new SyncCredentials(
            persisted.UserId,
            persisted.Email,
            persisted.AccessToken,
            persisted.AccessExpiresUtc,
            persisted.RefreshToken,
            persisted.DekGeneration,
            KeyMaterial.Adopt(key));
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
            plaintext = ProtectedData.Unprotect(
                File.ReadAllBytes(path),
                Entropy,
                DataProtectionScope.CurrentUser);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException)
        {
            // A different Windows user, a different machine, or a truncated file. All of these mean
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
            byte[] sealedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

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

    private sealed record Persisted(
        string UserId,
        string Email,
        string AccessToken,
        DateTimeOffset AccessExpiresUtc,
        string RefreshToken,
        int DekGeneration,
        string DataKey);
}
