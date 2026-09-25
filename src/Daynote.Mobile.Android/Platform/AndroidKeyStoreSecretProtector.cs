using Android.Security.Keystore;
using Daynote.Infrastructure.Sync;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace Daynote.Mobile.Android.Platform;

/// <summary>
/// The Android counterpart of DPAPI and the macOS Keychain: an AES-256 key generated inside the
/// AndroidKeyStore seals the payload with AES-GCM.
/// </summary>
/// <remarks>
/// <para>
/// The key never leaves the keystore — on a device with a secure element it never exists in the
/// application's address space at all — and it is bound to this app's signing identity, so the sealed
/// file is worthless to any other app and to the same file copied off the device. Uninstalling the
/// app destroys the key, which is the behaviour we want: the sealed session goes with it.
/// </para>
/// <para>
/// The nonce comes from the cipher rather than from us: AndroidKeyStore refuses a caller-supplied IV
/// for GCM (it is what stops a caller from reusing one), so it is generated on encrypt and read back
/// out. Entropy rides along as AAD, mirroring DPAPI's optional entropy.
/// </para>
/// </remarks>
public sealed class AndroidKeyStoreSecretProtector : ISecretProtector
{
    private const string Provider = "AndroidKeyStore";
    private const string Transformation = "AES/GCM/NoPadding";
    private const string Alias = "daynote-credentials-key-v1";
    private const byte FormatVersion = 0x01;
    private const int NonceSize = 12;
    private const int TagBits = 128;

    public byte[] Protect(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> entropy)
    {
        using Cipher cipher = Cipher.GetInstance(Transformation)!;
        cipher.Init(CipherMode.EncryptMode, GetOrCreateKey());
        if (entropy.Length > 0)
        {
            cipher.UpdateAAD(entropy.ToArray());
        }

        byte[] nonce = cipher.GetIV() ?? throw new CryptographicMismatchException("The cipher produced no IV.");
        byte[] sealedBytes = cipher.DoFinal(plaintext.ToArray())
            ?? throw new CryptographicMismatchException("The cipher produced no output.");

        var output = new byte[1 + 1 + nonce.Length + sealedBytes.Length];
        output[0] = FormatVersion;
        output[1] = (byte)nonce.Length;
        nonce.CopyTo(output, 2);
        sealedBytes.CopyTo(output, 2 + nonce.Length);
        return output;
    }

    public byte[] Unprotect(ReadOnlySpan<byte> sealedBytes, ReadOnlySpan<byte> entropy)
    {
        if (sealedBytes.Length < 2 || sealedBytes[0] != FormatVersion)
        {
            throw new System.Security.Cryptography.CryptographicException("The sealed blob is not in a recognised format.");
        }

        int nonceLength = sealedBytes[1];
        if (nonceLength is < 1 or > 16 || sealedBytes.Length < 2 + nonceLength)
        {
            throw new System.Security.Cryptography.CryptographicException("The sealed blob is truncated.");
        }

        byte[] nonce = sealedBytes.Slice(2, nonceLength).ToArray();
        byte[] ciphertext = sealedBytes[(2 + nonceLength)..].ToArray();

        IKey key = ReadKey()
            ?? throw new System.Security.Cryptography.CryptographicException("No keystore key exists for this install.");

        try
        {
            using Cipher cipher = Cipher.GetInstance(Transformation)!;
            cipher.Init(CipherMode.DecryptMode, key, new GCMParameterSpec(TagBits, nonce));
            if (entropy.Length > 0)
            {
                cipher.UpdateAAD(entropy.ToArray());
            }

            return cipher.DoFinal(ciphertext)
                ?? throw new System.Security.Cryptography.CryptographicException("The cipher produced no output.");
        }
        catch (Java.Lang.Exception exception)
        {
            // A wrong key, a tampered blob or a key the OS invalidated all arrive here. The caller
            // reads any of them as "signed out", which is the right outcome for all three.
            throw new System.Security.Cryptography.CryptographicException(
                "The sealed session could not be opened on this device.", new Exception(exception.Message));
        }
    }

    /// <summary>Removes the key. Only ever useful to a full "forget this device" reset.</summary>
    public void DeleteKey()
    {
        KeyStore store = Load();
        if (store.ContainsAlias(Alias))
        {
            store.DeleteEntry(Alias);
        }
    }

    private static IKey GetOrCreateKey() => ReadKey() ?? CreateKey();

    private static IKey? ReadKey() => Load().GetKey(Alias, null);

    private static IKey CreateKey()
    {
        using KeyGenerator generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, Provider)!;
        using var spec = new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)!
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)!
            .SetKeySize(256)!
            // Deliberately NOT SetUserAuthenticationRequired: the session has to be readable on a
            // cold start so sync can run before the user has unlocked anything inside the app. The
            // note lock is the feature that asks for a passphrase, and it is separate from this.
            .Build()!;
        generator.Init(spec);
        return generator.GenerateKey()!;
    }

    private static KeyStore Load()
    {
        KeyStore store = KeyStore.GetInstance(Provider)!;
        store.Load(null);
        return store;
    }

    /// <summary>Raised when the platform cipher returns something the contract says it cannot.</summary>
    private sealed class CryptographicMismatchException(string message)
        : System.Security.Cryptography.CryptographicException(message);
}
