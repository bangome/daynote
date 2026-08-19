using System.Buffers.Text;
using Daynote.Core.Domain;

namespace Daynote.Core.Sync;

/// <summary>
/// The two keys derived from the password. <see cref="AuthKey"/> is sent to the server;
/// <see cref="Kek"/> must never leave the device.
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

    /// <summary>Proves identity to the server. Useless for decrypting anything.</summary>
    public KeyMaterial AuthKey { get; }

    /// <summary>Unwraps the data key. Sending this anywhere would end the end-to-end guarantee.</summary>
    public KeyMaterial Kek { get; }

    /// <summary>The wire encoding the auth endpoints expect for <c>auth_key</c>.</summary>
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
/// The whole end-to-end property rests on what is absent from this interface: nothing here returns
/// the password, the KEK, the recovery key, or an unwrapped data key in a form intended for
/// transmission. The only method producing something meant for the server is
/// <see cref="SyncKeySet.AuthKeyForServer"/>.
/// </remarks>
public interface ISyncCrypto
{
    /// <summary>
    /// Derives the auth key and KEK from a password. Deliberately expensive — hundreds of
    /// milliseconds — so call it off the UI thread, once per sign-in, and cache the result.
    /// </summary>
    SyncKeySet DeriveKeys(string password, string email, KdfParameters parameters);

    /// <summary>Derives the wrapping key for the recovery envelope. Cheap: the input is already random.</summary>
    KeyMaterial DeriveRecoveryKek(RecoveryKey recoveryKey);

    /// <summary>A fresh data key. Generated once per account and never derived from anything.</summary>
    KeyMaterial GenerateDataKey();

    /// <summary>
    /// Wraps the data key under <paramref name="wrappingKey"/> (a KEK or recovery KEK) using
    /// <paramref name="scope"/> as AAD. Unlike <see cref="Encrypt"/>, the wrapping key is used
    /// directly rather than via a per-record derivation: there is exactly one data key per wrapping
    /// key, so there is no nonce space to separate.
    /// </summary>
    string WrapDataKey(KeyMaterial dataKey, KeyMaterial wrappingKey, CipherScope scope);

    /// <summary>
    /// Fails rather than throws for a wrong password or a tampered envelope, since both are ordinary
    /// user-facing outcomes at the sign-in and unlock screens.
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
