namespace Daynote.Core.Sync;

/// <summary>Which wrapping key an envelope was sealed under, so the two cannot be interchanged.</summary>
public enum DataKeyPurpose
{
    /// <summary>Wrapped under the key derived from the lock passphrase.</summary>
    Passphrase,

    /// <summary>Wrapped under the key derived from the recovery key.</summary>
    Recovery,
}

/// <summary>
/// Identifies the one slot a ciphertext is allowed to occupy. Rendered into the AES-GCM additional
/// authenticated data, so a blob decrypts only where it was written.
/// </summary>
/// <remarks>
/// Without this binding, a hostile or buggy server could copy note A's ciphertext into note B's row
/// and the client would accept it: same key, valid tag, wrong note. The AAD is authenticated but not
/// encrypted, so it must contain nothing sensitive — ids are random UUIDs, and the email is already
/// known to the server.
/// </remarks>
public readonly record struct CipherScope
{
    private const string Version = "daynote-v1";

    private CipherScope(string descriptor)
    {
        Descriptor = descriptor;
    }

    /// <summary>The exact string authenticated as AAD. Stable forever: changing it breaks decryption.</summary>
    public string Descriptor { get; }

    public static CipherScope Note(string userId, string noteId) => Entity("note", userId, noteId);

    public static CipherScope File(string userId, string fileId) => Entity("file", userId, fileId);

    public static CipherScope Asset(string userId, string contentHash) =>
        Entity("asset", userId, contentHash);

    /// <summary>
    /// The slot a wrapped data key occupies. Keyed on the account's address rather than its id only
    /// because the address is what the passphrase derivation is already salted with, so the two
    /// cannot disagree.
    /// </summary>
    public static CipherScope DataKey(DataKeyPurpose purpose, string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        string name = purpose == DataKeyPurpose.Passphrase ? "password" : "recovery";
        return new CipherScope($"{Version}|dek|{name}|{NormalizeEmail(email)}");
    }

    private static CipherScope Entity(string kind, string userId, string entityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        return new CipherScope($"{Version}|{kind}|{userId}|{entityId}");
    }

    /// <summary>
    /// The label mixed into HKDF to give each record its own encryption key, so no two records share
    /// a nonce space even if a nonce ever repeats.
    /// </summary>
    public string KeyDerivationInfo => $"{Version}|key|{Descriptor}";

    /// <summary>
    /// Must match the server's normalisation (<c>cloud/worker/src/validate.ts</c>) or the AAD will
    /// differ between the account that registered and the account that logs in.
    /// </summary>
    public static string NormalizeEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim().ToLowerInvariant();
    }

    public override string ToString() => Descriptor;
}
