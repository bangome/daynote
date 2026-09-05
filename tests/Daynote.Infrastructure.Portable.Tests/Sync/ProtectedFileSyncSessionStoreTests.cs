using System.Security.Cryptography;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Sync;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Infrastructure.Portable.Tests.Sync;

[TestClass]
public sealed class ProtectedFileSyncSessionStoreTests
{
    /// <summary>XOR with a fixed pad plus an entropy check: enough to prove the store round-trips through the protector.</summary>
    private sealed class FakeProtector : ISecretProtector
    {
        public int Protects;
        public int Unprotects;
        public bool FailUnprotect;

        public byte[] Protect(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> entropy)
        {
            Protects++;
            var output = new byte[plaintext.Length + 1];
            output[0] = (byte)entropy.Length;
            for (int i = 0; i < plaintext.Length; i++)
            {
                output[i + 1] = (byte)(plaintext[i] ^ 0x5A);
            }

            return output;
        }

        public byte[] Unprotect(ReadOnlySpan<byte> sealedBytes, ReadOnlySpan<byte> entropy)
        {
            Unprotects++;
            if (FailUnprotect || sealedBytes.Length == 0 || sealedBytes[0] != entropy.Length)
            {
                throw new CryptographicException("wrong user");
            }

            var output = new byte[sealedBytes.Length - 1];
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = (byte)(sealedBytes[i + 1] ^ 0x5A);
            }

            return output;
        }
    }

    private static SyncCredentials Credentials(byte[]? key = null) => new(
        "user-1", "a@b.c", "access", DateTimeOffset.UnixEpoch.AddHours(1), "refresh", 3,
        key is null ? null : KeyMaterial.Adopt(key));

    [TestMethod]
    public async Task Save_then_load_round_trips_every_field_through_the_protector()
    {
        using var root = new TempDirectory();
        var protector = new FakeProtector();
        var store = new ProtectedFileSyncSessionStore(root.Path, protector);
        byte[] key = RandomNumberGenerator.GetBytes(KeyMaterial.Length);

        await store.SaveAsync(Credentials(key));
        using SyncCredentials? loaded = await store.LoadAsync();

        Assert.IsNotNull(loaded);
        Assert.AreEqual("user-1", loaded.UserId);
        Assert.AreEqual("a@b.c", loaded.Email);
        Assert.AreEqual("access", loaded.AccessToken);
        Assert.AreEqual("refresh", loaded.RefreshToken);
        Assert.AreEqual(3, loaded.DekGeneration);
        Assert.IsTrue(loaded.CanDecrypt);
        CollectionAssert.AreEqual(key, loaded.DataKey!.Span.ToArray());
        Assert.AreEqual(1, protector.Protects);
        Assert.AreEqual(1, protector.Unprotects);

        string file = Path.Combine(root.Path, "credentials.dat");
        Assert.IsTrue(File.Exists(file));
        Assert.IsFalse(File.ReadAllText(file).Contains("access", StringComparison.Ordinal), "never plaintext on disk");
    }

    [TestMethod]
    public async Task Unprotect_failure_reads_as_signed_out_not_as_an_error()
    {
        using var root = new TempDirectory();
        var protector = new FakeProtector();
        var store = new ProtectedFileSyncSessionStore(root.Path, protector);
        await store.SaveAsync(Credentials());

        protector.FailUnprotect = true;
        Assert.IsNull(await store.LoadAsync());
    }

    [TestMethod]
    public async Task UpdateTokens_keeps_the_key_and_is_a_no_op_when_signed_out()
    {
        using var root = new TempDirectory();
        var store = new ProtectedFileSyncSessionStore(root.Path, new FakeProtector());

        await store.UpdateTokensAsync("a2", DateTimeOffset.UnixEpoch, "r2");
        Assert.IsNull(await store.LoadAsync(), "no record is created from tokens alone");

        byte[] key = RandomNumberGenerator.GetBytes(KeyMaterial.Length);
        await store.SaveAsync(Credentials(key));
        await store.UpdateTokensAsync("a2", DateTimeOffset.UnixEpoch.AddDays(1), "r2");

        using SyncCredentials? loaded = await store.LoadAsync();
        Assert.IsNotNull(loaded);
        Assert.AreEqual("a2", loaded.AccessToken);
        Assert.AreEqual("r2", loaded.RefreshToken);
        CollectionAssert.AreEqual(key, loaded.DataKey!.Span.ToArray());

        await store.ClearAsync();
        Assert.IsNull(await store.LoadAsync());
        Assert.IsFalse(File.Exists(Path.Combine(root.Path, "credentials.dat")));
    }
}
