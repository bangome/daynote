using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Daynote.Infrastructure.Sync;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Infrastructure.Portable.Tests.Sync;

/// <summary>
/// Talks to the real login Keychain, so it runs only on macOS and uses a throwaway service name it
/// deletes afterwards. On any other OS the tests are inconclusive rather than failing.
/// </summary>
// The attribute satisfies the platform analyzer for the lambdas below; the runtime guard in each test
// is what actually keeps them off other operating systems.
[TestClass]
[SupportedOSPlatform("macos")]
public sealed class MacKeychainSecretProtectorTests
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("daynote-tests");

    [TestMethod]
    public void Round_trips_and_rejects_a_tampered_blob_or_different_entropy()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("Keychain is macOS-only.");
            return;
        }

        var protector = new MacKeychainSecretProtector("Daynote.Tests", $"key-{Guid.NewGuid():N}");
        try
        {
            byte[] plaintext = Encoding.UTF8.GetBytes("{\"token\":\"secret\"}");
            byte[] sealedBytes = protector.Protect(plaintext, Entropy);
            Assert.IsFalse(sealedBytes.AsSpan().IndexOf("secret"u8) >= 0, "ciphertext must not contain the plaintext");

            CollectionAssert.AreEqual(plaintext, protector.Unprotect(sealedBytes, Entropy));

            byte[] again = protector.Protect(plaintext, Entropy);
            CollectionAssert.AreNotEqual(sealedBytes, again, "fresh nonce every time");
            CollectionAssert.AreEqual(plaintext, protector.Unprotect(again, Entropy), "same Keychain key opens both");

            Assert.Throws<CryptographicException>(() => protector.Unprotect(sealedBytes, "other-app"u8));

            sealedBytes[^1] ^= 0xFF;
            Assert.Throws<CryptographicException>(() => protector.Unprotect(sealedBytes, Entropy));
        }
        finally
        {
            protector.DeleteKey();
        }
    }

    [TestMethod]
    public void A_blob_sealed_under_a_deleted_key_can_no_longer_be_opened()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("Keychain is macOS-only.");
            return;
        }

        var protector = new MacKeychainSecretProtector("Daynote.Tests", $"key-{Guid.NewGuid():N}");
        byte[] sealedBytes = protector.Protect("x"u8, Entropy);
        protector.DeleteKey();
        Assert.Throws<CryptographicException>(() => protector.Unprotect(sealedBytes, Entropy));
        protector.DeleteKey();
    }
}
