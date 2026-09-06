using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Daynote.Infrastructure.Sync;

/// <summary>
/// The macOS counterpart of DPAPI: a random 256-bit key lives in the user's login Keychain as a generic
/// password, and the payload is sealed with AES-GCM under that key. The Keychain item is bound to this
/// user on this Mac (and, by default, to the application that created it), so the sealed file alone is
/// worthless elsewhere. Entropy rides along as associated data, mirroring DPAPI's optional entropy.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacKeychainSecretProtector : ISecretProtector
{
    private const byte FormatVersion = 0x01;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private readonly string _service;
    private readonly string _account;

    public MacKeychainSecretProtector(string service = "Daynote", string account = "credentials-key-v1")
    {
        _service = string.IsNullOrWhiteSpace(service) ? throw new ArgumentException("Service required.", nameof(service)) : service;
        _account = string.IsNullOrWhiteSpace(account) ? throw new ArgumentException("Account required.", nameof(account)) : account;
    }

    public byte[] Protect(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> entropy)
    {
        byte[] key = GetOrCreateKey();
        try
        {
            var output = new byte[1 + NonceSize + TagSize + plaintext.Length];
            output[0] = FormatVersion;
            Span<byte> nonce = output.AsSpan(1, NonceSize);
            Span<byte> tag = output.AsSpan(1 + NonceSize, TagSize);
            Span<byte> ciphertext = output.AsSpan(1 + NonceSize + TagSize);
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, entropy);
            return output;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public byte[] Unprotect(ReadOnlySpan<byte> sealedBytes, ReadOnlySpan<byte> entropy)
    {
        if (sealedBytes.Length < 1 + NonceSize + TagSize || sealedBytes[0] != FormatVersion)
        {
            throw new CryptographicException("The sealed blob is not in a recognised format.");
        }

        byte[] key = ReadKey() ?? throw new CryptographicException("No Keychain key exists for this user.");
        try
        {
            ReadOnlySpan<byte> nonce = sealedBytes.Slice(1, NonceSize);
            ReadOnlySpan<byte> tag = sealedBytes.Slice(1 + NonceSize, TagSize);
            ReadOnlySpan<byte> ciphertext = sealedBytes[(1 + NonceSize + TagSize)..];
            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, entropy);
            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Removes the key. Only ever useful to tests and to a full "forget this Mac" reset.</summary>
    public void DeleteKey()
    {
        using var query = new CFDictionary(
            (Keychain.kSecClass, Keychain.kSecClassGenericPassword),
            (Keychain.kSecAttrService, CF.String(_service)),
            (Keychain.kSecAttrAccount, CF.String(_account)));
        int status = Keychain.SecItemDelete(query.Handle);
        if (status != Keychain.errSecSuccess && status != Keychain.errSecItemNotFound)
        {
            throw new CryptographicException($"Keychain delete failed with status {status}.");
        }
    }

    private byte[] GetOrCreateKey()
    {
        if (ReadKey() is { } existing)
        {
            return existing;
        }

        byte[] fresh = RandomNumberGenerator.GetBytes(KeySize);
        using var attributes = new CFDictionary(
            (Keychain.kSecClass, Keychain.kSecClassGenericPassword),
            (Keychain.kSecAttrService, CF.String(_service)),
            (Keychain.kSecAttrAccount, CF.String(_account)),
            (Keychain.kSecValueData, CF.Data(fresh)));
        int status = Keychain.SecItemAdd(attributes.Handle, IntPtr.Zero);
        if (status == Keychain.errSecSuccess)
        {
            return fresh;
        }

        CryptographicOperations.ZeroMemory(fresh);
        if (status == Keychain.errSecDuplicateItem && ReadKey() is { } raced)
        {
            // Two processes created the key at once; the Keychain kept exactly one. Use that one.
            return raced;
        }

        throw new CryptographicException($"Keychain add failed with status {status}.");
    }

    private byte[]? ReadKey()
    {
        using var query = new CFDictionary(
            (Keychain.kSecClass, Keychain.kSecClassGenericPassword),
            (Keychain.kSecAttrService, CF.String(_service)),
            (Keychain.kSecAttrAccount, CF.String(_account)),
            (Keychain.kSecReturnData, CF.kCFBooleanTrue),
            (Keychain.kSecMatchLimit, Keychain.kSecMatchLimitOne));
        int status = Keychain.SecItemCopyMatching(query.Handle, out IntPtr result);
        if (status == Keychain.errSecItemNotFound)
        {
            return null;
        }

        if (status != Keychain.errSecSuccess || result == IntPtr.Zero)
        {
            throw new CryptographicException($"Keychain read failed with status {status}.");
        }

        try
        {
            nint length = CF.CFDataGetLength(result);
            if (length != KeySize)
            {
                throw new CryptographicException("The Keychain key has an unexpected size.");
            }

            var key = new byte[length];
            Marshal.Copy(CF.CFDataGetBytePtr(result), key, 0, (int)length);
            return key;
        }
        finally
        {
            CF.CFRelease(result);
        }
    }

    /// <summary>A CFDictionary that owns (and releases) the value objects created for it.</summary>
    private sealed class CFDictionary : IDisposable
    {
        private readonly IntPtr[] _ownedValues;

        public CFDictionary(params (IntPtr Key, IntPtr Value)[] entries)
        {
            var keys = new IntPtr[entries.Length];
            var values = new IntPtr[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                keys[i] = entries[i].Key;
                values[i] = entries[i].Value;
            }

            // Constants (kSec*, kCFBooleanTrue) are process-global and must not be released; only the
            // strings/data we created are ours. Those are the ones not equal to any known constant.
            _ownedValues = values.Where(v => !Keychain.IsConstant(v) && v != CF.kCFBooleanTrue).ToArray();
            Handle = CF.CFDictionaryCreate(
                IntPtr.Zero, keys, values, entries.Length,
                CF.kCFTypeDictionaryKeyCallBacks, CF.kCFTypeDictionaryValueCallBacks);
            if (Handle == IntPtr.Zero)
            {
                throw new CryptographicException("CFDictionaryCreate failed.");
            }
        }

        public IntPtr Handle { get; }

        public void Dispose()
        {
            // The dictionary retained the values on creation; releasing both our reference and the
            // dictionary's brings every object we made back to zero.
            CF.CFRelease(Handle);
            foreach (IntPtr value in _ownedValues)
            {
                CF.CFRelease(value);
            }
        }
    }

    private static class CF
    {
        private const string Library = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const uint Utf8 = 0x08000100;

        private static readonly IntPtr Handle = NativeLibrary.Load(Library);

        public static readonly IntPtr kCFBooleanTrue = Marshal.ReadIntPtr(NativeLibrary.GetExport(Handle, "kCFBooleanTrue"));
        public static readonly IntPtr kCFTypeDictionaryKeyCallBacks = NativeLibrary.GetExport(Handle, "kCFTypeDictionaryKeyCallBacks");
        public static readonly IntPtr kCFTypeDictionaryValueCallBacks = NativeLibrary.GetExport(Handle, "kCFTypeDictionaryValueCallBacks");

        public static IntPtr String(string value) => CFStringCreateWithCString(IntPtr.Zero, value, Utf8);

        public static IntPtr Data(byte[] bytes) => CFDataCreate(IntPtr.Zero, bytes, bytes.Length);

        [DllImport(Library)]
        public static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys, IntPtr[] values, nint count, IntPtr keyCallBacks, IntPtr valueCallBacks);

        [DllImport(Library)]
        private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, uint encoding);

        [DllImport(Library)]
        private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);

        [DllImport(Library)]
        public static extern nint CFDataGetLength(IntPtr data);

        [DllImport(Library)]
        public static extern IntPtr CFDataGetBytePtr(IntPtr data);

        [DllImport(Library)]
        public static extern void CFRelease(IntPtr handle);
    }

    private static class Keychain
    {
        private const string Library = "/System/Library/Frameworks/Security.framework/Security";

        public const int errSecSuccess = 0;
        public const int errSecItemNotFound = -25300;
        public const int errSecDuplicateItem = -25299;

        private static readonly IntPtr Handle = NativeLibrary.Load(Library);

        public static readonly IntPtr kSecClass = Constant("kSecClass");
        public static readonly IntPtr kSecClassGenericPassword = Constant("kSecClassGenericPassword");
        public static readonly IntPtr kSecAttrService = Constant("kSecAttrService");
        public static readonly IntPtr kSecAttrAccount = Constant("kSecAttrAccount");
        public static readonly IntPtr kSecValueData = Constant("kSecValueData");
        public static readonly IntPtr kSecReturnData = Constant("kSecReturnData");
        public static readonly IntPtr kSecMatchLimit = Constant("kSecMatchLimit");
        public static readonly IntPtr kSecMatchLimitOne = Constant("kSecMatchLimitOne");

        private static readonly HashSet<IntPtr> Constants =
        [
            kSecClass, kSecClassGenericPassword, kSecAttrService, kSecAttrAccount,
            kSecValueData, kSecReturnData, kSecMatchLimit, kSecMatchLimitOne,
        ];

        public static bool IsConstant(IntPtr value) => Constants.Contains(value);

        private static IntPtr Constant(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(Handle, name));

        [DllImport(Library)]
        public static extern int SecItemAdd(IntPtr attributes, IntPtr result);

        [DllImport(Library)]
        public static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

        [DllImport(Library)]
        public static extern int SecItemDelete(IntPtr query);
    }
}
