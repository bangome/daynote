using System.Buffers.Text;
using System.Security.Cryptography;
using Konscious.Security.Cryptography;
using System.Text;
using Daynote.Core.Domain;
using Daynote.Core.Sync;

namespace Daynote.Infrastructure.Sync;

/// <summary>
/// The client-side crypto for cloud sync: HKDF-SHA256 for per-record keys and AES-256-GCM for
/// everything stored. See docs/CLOUD_SYNC.md §4.
/// </summary>
/// <remarks>
/// Two halves. Content encryption is used by every account. The derivation and wrapping half is used
/// only by an account that has switched cloud sync to the opt-in lock (docs/CLOUD_SYNC.md §4.1b):
/// there the data key is wrapped under a passphrase and a recovery key instead of being held by the
/// server, so this device has to derive those wrapping keys itself.
/// <para>
/// All primitives come from the BCL except Argon2id, which is a pure-managed package
/// (<c>Konscious.Security.Cryptography.Argon2</c>) chosen over a native build so the MSIX payload
/// stays architecture-independent.
/// </para>
/// </remarks>
public sealed class AesGcmSyncCrypto : ISyncCrypto
{
    private const string EnvelopePrefix = "v1";
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    private const string AuthInfo = "daynote-v1-auth";
    private const string KekInfo = "daynote-v1-kek";
    private const string RecoveryInfo = "daynote-v1-rkek";
    private const string AssetKeyIdInfo = "daynote-v1-asset-keyid";
    private const string SaltPrefix = "daynote-v1:";

    public SyncKeySet DeriveKeys(string password, string email, KdfParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        // The salt is derived from the email, not random: the client must derive this key at the
        // sign-in screen, before it has authenticated and can be told anything by the server. That
        // makes the salt public, which is fine — the KDF's cost is what protects the password, and
        // the per-account salt still defeats a single rainbow table covering every user.
        byte[] salt = SHA256.HashData(
            Encoding.UTF8.GetBytes(SaltPrefix + CipherScope.NormalizeEmail(email)));

        byte[] masterKey = parameters.Algorithm == KdfAlgorithm.Argon2id
            ? DeriveArgon2id(password, salt, parameters)
            : Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                parameters.Iterations,
                HashAlgorithmName.SHA256,
                KeyMaterial.Length);

        try
        {
            return new SyncKeySet(
                KeyMaterial.Adopt(Expand(masterKey, AuthInfo)),
                KeyMaterial.Adopt(Expand(masterKey, KekInfo)));
        }
        finally
        {
            // The master key itself is never needed again; only its two children are.
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    public KeyMaterial DeriveRecoveryKek(RecoveryKey recoveryKey)
    {
        if (!recoveryKey.IsValid)
        {
            throw new ArgumentException("The recovery key is not initialised.", nameof(recoveryKey));
        }

        // No stretching: a recovery key is already 128 bits of uniform randomness, so a slow KDF
        // would add cost without adding strength.
        return KeyMaterial.Adopt(Expand(recoveryKey.Span, RecoveryInfo));
    }

    public string WrapDataKey(KeyMaterial dataKey, KeyMaterial wrappingKey, CipherScope scope)
    {
        ArgumentNullException.ThrowIfNull(dataKey);
        ArgumentNullException.ThrowIfNull(wrappingKey);
        return Seal(dataKey.Span, wrappingKey.Span, scope.Descriptor);
    }

    public DomainResult<KeyMaterial> UnwrapDataKey(
        string envelope,
        KeyMaterial wrappingKey,
        CipherScope scope)
    {
        ArgumentNullException.ThrowIfNull(wrappingKey);

        DomainResult<byte[]> opened = Open(envelope, wrappingKey.Span, scope.Descriptor);
        if (!opened.IsSuccess)
        {
            return DomainResult<KeyMaterial>.Failure(opened.Error.Code, opened.Error.Message);
        }

        byte[] plaintext = opened.Value;
        // Unreachable through this API today — WrapDataKey only accepts a KeyMaterial, which is
        // always 32 bytes — but a v2 envelope format or a second wrapping purpose could change that,
        // and a key of the wrong length must fail here rather than deeper in the stack.
        if (plaintext.Length != KeyMaterial.Length)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            return DomainResult<KeyMaterial>.Failure(
                DomainErrorCode.MalformedCiphertext,
                "The envelope did not contain a 256-bit key.");
        }

        return DomainResult<KeyMaterial>.Success(KeyMaterial.Adopt(plaintext));
    }

    public string Encrypt(string plaintext, KeyMaterial dataKey, CipherScope scope)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(dataKey);

        byte[] recordKey = Expand(dataKey.Span, scope.KeyDerivationInfo);
        try
        {
            return Seal(Encoding.UTF8.GetBytes(plaintext), recordKey, scope.Descriptor);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(recordKey);
        }
    }

    public DomainResult<string> Decrypt(string envelope, KeyMaterial dataKey, CipherScope scope)
    {
        ArgumentNullException.ThrowIfNull(dataKey);

        byte[] recordKey = Expand(dataKey.Span, scope.KeyDerivationInfo);
        try
        {
            DomainResult<byte[]> opened = Open(envelope, recordKey, scope.Descriptor);
            if (!opened.IsSuccess)
            {
                return DomainResult<string>.Failure(opened.Error.Code, opened.Error.Message);
            }

            byte[] plaintext = opened.Value;
            try
            {
                return DomainResult<string>.Success(Encoding.UTF8.GetString(plaintext));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(recordKey);
        }
    }

    public string BlindAssetKey(KeyMaterial dataKey, string contentHash)
    {
        ArgumentNullException.ThrowIfNull(dataKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        byte[] keyId = Expand(dataKey.Span, AssetKeyIdInfo);
        try
        {
            byte[] blinded = HMACSHA256.HashData(
                keyId,
                Encoding.UTF8.GetBytes(contentHash.ToLowerInvariant()));
            return Convert.ToHexStringLower(blinded);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyId);
        }
    }

    private static byte[] DeriveArgon2id(string password, byte[] salt, KdfParameters parameters)
    {
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                MemorySize = parameters.MemoryKib,
                Iterations = parameters.Iterations,
                DegreeOfParallelism = parameters.Parallelism,
            };

            return argon2.GetBytes(KeyMaterial.Length);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private static byte[] Expand(ReadOnlySpan<byte> secret, string info)
    {
        byte[] derived = new byte[KeyMaterial.Length];
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            secret,
            derived,
            salt: default,
            info: Encoding.UTF8.GetBytes(info));
        return derived;
    }

    /// <summary>Produces <c>v1.&lt;nonce&gt;.&lt;ciphertext||tag&gt;</c>, both parts base64url.</summary>
    private static string Seal(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, string aad)
    {
        Span<byte> nonce = stackalloc byte[NonceBytes];
        RandomNumberGenerator.Fill(nonce);

        // Tag stored adjacent to the ciphertext so the envelope carries one opaque blob, matching the
        // WebCrypto layout the Worker-side tooling expects.
        byte[] sealedBytes = new byte[plaintext.Length + TagBytes];
        Span<byte> ciphertext = sealedBytes.AsSpan(0, plaintext.Length);
        Span<byte> tag = sealedBytes.AsSpan(plaintext.Length, TagBytes);

        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(aad));

        return string.Concat(
            EnvelopePrefix,
            ".",
            Base64Url.EncodeToString(nonce),
            ".",
            Base64Url.EncodeToString(sealedBytes));
    }

    private static DomainResult<byte[]> Open(string? envelope, ReadOnlySpan<byte> key, string aad)
    {
        if (envelope is null)
        {
            return Malformed("The envelope is missing.");
        }

        // Three parts exactly: a longer split would mean an unknown format, not a recoverable one.
        string[] parts = envelope.Split('.');
        if (parts.Length != 3 || !string.Equals(parts[0], EnvelopePrefix, StringComparison.Ordinal))
        {
            return Malformed("The envelope is not a v1 AES-GCM envelope.");
        }

        if (!TryDecodeBase64Url(parts[1], out byte[]? nonce) ||
            !TryDecodeBase64Url(parts[2], out byte[]? sealedBytes))
        {
            return Malformed("The envelope is not valid base64url.");
        }

        if (nonce!.Length != NonceBytes || sealedBytes!.Length < TagBytes)
        {
            return Malformed("The envelope has the wrong nonce or ciphertext length.");
        }

        byte[] plaintext = new byte[sealedBytes.Length - TagBytes];
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(
                nonce,
                sealedBytes.AsSpan(0, plaintext.Length),
                sealedBytes.AsSpan(plaintext.Length, TagBytes),
                plaintext,
                Encoding.UTF8.GetBytes(aad));
        }
        catch (AuthenticationTagMismatchException)
        {
            // Wrong key, tampered ciphertext, or a blob written for a different scope. The three are
            // indistinguishable by design, and none of them may be treated as "skip this record".
            CryptographicOperations.ZeroMemory(plaintext);
            return DomainResult<byte[]>.Failure(
                DomainErrorCode.CiphertextAuthenticationFailed,
                "The data could not be decrypted with this key.");
        }

        return DomainResult<byte[]>.Success(plaintext);
    }

    private static bool TryDecodeBase64Url(string value, out byte[]? decoded)
    {
        // Base64Url.DecodeFromChars would accept a shorter-than-expected buffer silently if we sized
        // it optimistically, so ask the type for the exact length first.
        try
        {
            decoded = Base64Url.DecodeFromChars(value);
            return true;
        }
        catch (FormatException)
        {
            decoded = null;
            return false;
        }
    }

    private static DomainResult<byte[]> Malformed(string message) =>
        DomainResult<byte[]>.Failure(DomainErrorCode.MalformedCiphertext, message);
}
