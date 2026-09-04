using System.Buffers.Text;
using Daynote.Core.Domain;

namespace Daynote.Core.Sync;

/// <summary>
/// The two keys derived from a lock passphrase. <see cref="AuthKey"/> is reserved for a server that
/// wants to prove the passphrase before handing back an envelope; <see cref="Kek"/> must never leave
/// the device — sending it would end the lock's guarantee.
/// </summary>
public sealed class SyncKeySet : IDisposable
{
    public SyncKeySet(KeyMaterial authKey, KeyMaterial kek)
    {
        ArgumentNullException.ThrowIfNull(authKey);
        ArgumentNullException.ThrowIfNull(kek);
        AuthKey = authKey;
        Kek = kek;
    }

    public KeyMaterial AuthKey { get; }

    /// <summary>Unwraps the data key. Sending this anywhere would end the lock's guarantee.</summary>
    public KeyMaterial Kek { get; }

    public string AuthKeyForServer() => Base64Url.EncodeToString(AuthKey.Span);

    public void Dispose()
    {
        AuthKey.Dispose();
        Kek.Dispose();
    }
}

/// <summary>
/// Every cryptographic operation cloud sync performs on this device. See docs/CLOUD_SYNC.md §4.
/// </summary>
/// <remarks>
/// Content encryption is used by every account. Derivation and wrapping are used only by an account
/// with the opt-in lock on (docs/CLOUD_SYNC.md §4.1b): by default the data key is issued by the
/// server, and this device derives nothing. Per-record keys are always derived from the data key, so
/// no two records share a nonce space and a blob written for one note cannot be replayed as another.
/// </remarks>
public interface ISyncCrypto
{
    /// <summary>
    /// Derives the wrapping keys from a lock passphrase. Deliberately expensive — hundreds of
    /// milliseconds — so call it off the UI thread, once per unlock, and cache the result.
    /// </summary>
    SyncKeySet DeriveKeys(string password, string email, KdfParameters parameters);

    /// <summary>Derives the wrapping key for the recovery envelope. Cheap: the input is already random.</summary>
    KeyMaterial DeriveRecoveryKek(RecoveryKey recoveryKey);

    /// <summary>
    /// Wraps the data key under <paramref name="wrappingKey"/> using <paramref name="scope"/> as AAD.
    /// The wrapping key is used directly rather than via a per-record derivation: there is exactly
    /// one data key per wrapping key, so there is no nonce space to separate.
    /// </summary>
    string WrapDataKey(KeyMaterial dataKey, KeyMaterial wrappingKey, CipherScope scope);

    /// <summary>
    /// Fails rather than throws for a wrong passphrase or a tampered envelope, since both are
    /// ordinary user-facing outcomes at the unlock prompt.
    /// </summary>
    DomainResult<KeyMaterial> UnwrapDataKey(string envelope, KeyMaterial wrappingKey, CipherScope scope);

    /// <summary>Encrypts under a key derived from the data key and <paramref name="scope"/>.</summary>
    string Encrypt(string plaintext, KeyMaterial dataKey, CipherScope scope);

    /// <summary>
    /// Decrypts, verifying that the blob was written for exactly this <paramref name="scope"/>.
    /// A failure here is never ignorable: it means tampering, corruption, or the wrong key, and the
    /// caller must surface it rather than skipping the record (docs/CLOUD_SYNC.md §10).
    /// </summary>
    DomainResult<string> Decrypt(string envelope, KeyMaterial dataKey, CipherScope scope);

    /// <summary>
    /// The blinded R2 object key for an attachment (docs/CLOUD_SYNC.md §5.4). Deterministic per
    /// account, so per-user de-duplication still works, while the server cannot test whether a user
    /// holds a file whose plaintext hash it knows.
    /// </summary>
    string BlindAssetKey(KeyMaterial dataKey, string contentHash);
}
