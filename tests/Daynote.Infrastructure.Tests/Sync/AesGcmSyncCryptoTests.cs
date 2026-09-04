using System.Buffers.Text;
using Daynote.Core.Domain;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Sync;

namespace Daynote.Infrastructure.Tests.Sync;

/// <summary>
/// Content encryption. The derivation, wrapping, and recovery-key cases went with the password
/// model: the data key is now issued by the server, so this device derives nothing. What is still
/// asserted is the part that did not change — a blob decrypts in exactly the slot it was written
/// for, and any tampering is detected rather than skipped.
/// </summary>
[TestClass]
public sealed class AesGcmSyncCryptoTests
{
    private const string UserId = "11111111-1111-4111-8111-111111111111";
    private const string NoteId = "22222222-2222-4222-8222-222222222222";

    private static readonly AesGcmSyncCrypto Crypto = new();

    private static KeyMaterial NewDataKey() => KeyMaterial.Random();

    private static CipherScope NoteScope => CipherScope.Note(UserId, NoteId);

    [TestMethod]
    public void ContentRoundTrips()
    {
        using KeyMaterial dataKey = NewDataKey();
        const string Plaintext = """
            오늘 회의 메모 #프로젝트
            - emoji 🎯, tabs\tand "quotes"
            """;

        string envelope = Crypto.Encrypt(Plaintext, dataKey, NoteScope);

        Assert.AreEqual(Plaintext, Crypto.Decrypt(envelope, dataKey, NoteScope).Value);
    }

    [TestMethod]
    public void AnEmptyStringRoundTrips()
    {
        // An empty note body is ordinary; a zero-length plaintext must not take a different path.
        using KeyMaterial dataKey = NewDataKey();

        string envelope = Crypto.Encrypt(string.Empty, dataKey, NoteScope);

        Assert.AreEqual(string.Empty, Crypto.Decrypt(envelope, dataKey, NoteScope).Value);
    }

    [TestMethod]
    public void TheEnvelopeDoesNotContainThePlaintext()
    {
        using KeyMaterial dataKey = NewDataKey();

        string envelope = Crypto.Encrypt("VERY-SECRET-PHRASE", dataKey, NoteScope);

        Assert.IsFalse(envelope.Contains("VERY-SECRET", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(envelope.StartsWith("v1.", StringComparison.Ordinal));
        Assert.AreEqual(3, envelope.Split('.').Length);
    }

    [TestMethod]
    public void EncryptingTwiceProducesDifferentCiphertext()
    {
        // A fresh nonce per call. Identical output would tell the server which notes are unchanged
        // and, with AES-GCM, reusing a nonce under one key is catastrophic.
        using KeyMaterial dataKey = NewDataKey();

        string first = Crypto.Encrypt("same", dataKey, NoteScope);
        string second = Crypto.Encrypt("same", dataKey, NoteScope);

        Assert.AreNotEqual(first, second);
        Assert.AreEqual("same", Crypto.Decrypt(second, dataKey, NoteScope).Value);
    }

    [TestMethod]
    public void ABlobFromAnotherNoteIsRejected()
    {
        // The swapped-slot case: same account, same data key, wrong note. Without AAD binding the
        // client would accept note A's body as note B's and silently corrupt the user's data.
        using KeyMaterial dataKey = NewDataKey();
        CipherScope other = CipherScope.Note(UserId, "33333333-3333-4333-8333-333333333333");

        string envelope = Crypto.Encrypt("belongs to the first note", dataKey, NoteScope);

        DomainResult<string> opened = Crypto.Decrypt(envelope, dataKey, other);
        Assert.IsFalse(opened.IsSuccess);
        Assert.AreEqual(DomainErrorCode.CiphertextAuthenticationFailed, opened.Error.Code);
    }

    [TestMethod]
    public void ABlobFromAnotherUserIsRejected()
    {
        using KeyMaterial dataKey = NewDataKey();
        CipherScope otherUser = CipherScope.Note("44444444-4444-4444-8444-444444444444", NoteId);

        string envelope = Crypto.Encrypt("mine", dataKey, NoteScope);

        Assert.IsFalse(Crypto.Decrypt(envelope, dataKey, otherUser).IsSuccess);
    }

    [TestMethod]
    public void ANoteBlobIsNotAcceptedAsAFileBlob()
    {
        using KeyMaterial dataKey = NewDataKey();

        string envelope = Crypto.Encrypt("note body", dataKey, NoteScope);

        Assert.IsFalse(Crypto.Decrypt(envelope, dataKey, CipherScope.File(UserId, NoteId)).IsSuccess);
    }

    [TestMethod]
    public void ADifferentDataKeyIsRejected()
    {
        using KeyMaterial mine = NewDataKey();
        using KeyMaterial theirs = NewDataKey();

        string envelope = Crypto.Encrypt("mine", mine, NoteScope);

        Assert.IsFalse(Crypto.Decrypt(envelope, theirs, NoteScope).IsSuccess);
    }

    [TestMethod]
    public void FlippingASingleCiphertextBitIsDetected()
    {
        using KeyMaterial dataKey = NewDataKey();
        string envelope = Crypto.Encrypt("a body worth protecting", dataKey, NoteScope);

        string[] parts = envelope.Split('.');
        byte[] sealedBytes = Base64Url.DecodeFromChars(parts[2]);
        sealedBytes[0] ^= 0x01;
        string tampered = $"{parts[0]}.{parts[1]}.{Base64Url.EncodeToString(sealedBytes)}";

        DomainResult<string> opened = Crypto.Decrypt(tampered, dataKey, NoteScope);
        Assert.IsFalse(opened.IsSuccess);
        Assert.AreEqual(DomainErrorCode.CiphertextAuthenticationFailed, opened.Error.Code);
    }

    [TestMethod]
    public void FlippingASingleTagBitIsDetected()
    {
        using KeyMaterial dataKey = NewDataKey();
        string envelope = Crypto.Encrypt("a body worth protecting", dataKey, NoteScope);

        string[] parts = envelope.Split('.');
        byte[] sealedBytes = Base64Url.DecodeFromChars(parts[2]);
        sealedBytes[^1] ^= 0x01;
        string tampered = $"{parts[0]}.{parts[1]}.{Base64Url.EncodeToString(sealedBytes)}";

        Assert.IsFalse(Crypto.Decrypt(tampered, dataKey, NoteScope).IsSuccess);
    }

    [TestMethod]
    public void SubstitutingTheNonceIsDetected()
    {
        using KeyMaterial dataKey = NewDataKey();
        string first = Crypto.Encrypt("first", dataKey, NoteScope);
        string second = Crypto.Encrypt("second", dataKey, NoteScope);

        string[] firstParts = first.Split('.');
        string[] secondParts = second.Split('.');
        string spliced = $"{firstParts[0]}.{secondParts[1]}.{firstParts[2]}";

        Assert.IsFalse(Crypto.Decrypt(spliced, dataKey, NoteScope).IsSuccess);
    }

    [TestMethod]
    [DataRow(null, DisplayName = "null")]
    [DataRow("", DisplayName = "empty")]
    [DataRow("not-an-envelope", DisplayName = "no version prefix")]
    [DataRow("v2.AAAAAAAAAAAAAAAA.AAAAAAAAAAAAAAAAAAAAAAAA", DisplayName = "unknown version")]
    [DataRow("v1.AAAAAAAAAAAAAAAA", DisplayName = "two parts")]
    [DataRow("v1.AAAAAAAAAAAAAAAA.AAAA.AAAA", DisplayName = "four parts")]
    [DataRow("v1.not base64url!.AAAAAAAAAAAAAAAAAAAAAAAA", DisplayName = "bad base64url")]
    [DataRow("v1.AAAA.AAAAAAAAAAAAAAAAAAAAAAAA", DisplayName = "short nonce")]
    [DataRow("v1.AAAAAAAAAAAAAAAA.AAAA", DisplayName = "ciphertext shorter than the tag")]
    public void MalformedEnvelopeIsReportedRatherThanThrown(string? envelope)
    {
        // These arrive from the network, so a malformed value must be a domain failure the sync
        // engine can log and skip past — never an exception that takes down a background sync.
        using KeyMaterial dataKey = NewDataKey();

        DomainResult<string> opened = Crypto.Decrypt(envelope!, dataKey, NoteScope);

        Assert.IsFalse(opened.IsSuccess);
        Assert.AreEqual(DomainErrorCode.MalformedCiphertext, opened.Error.Code);
    }

    [TestMethod]
    public void BlindedAssetKeysAreStableForAnAccountAndDifferAcrossAccounts()
    {
        const string ContentHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        using KeyMaterial mine = NewDataKey();
        using KeyMaterial theirs = NewDataKey();

        string first = Crypto.BlindAssetKey(mine, ContentHash);

        // Stable, so per-account de-duplication still works...
        Assert.AreEqual(first, Crypto.BlindAssetKey(mine, ContentHash));
        // ...but not correlatable across accounts, and not the plaintext hash the server could guess.
        Assert.AreNotEqual(first, Crypto.BlindAssetKey(theirs, ContentHash));
        Assert.AreNotEqual(ContentHash, first);
        Assert.AreEqual(64, first.Length);
    }

    [TestMethod]
    public void BlindedAssetKeysIgnoreHashCasing()
    {
        const string Lower = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        using KeyMaterial dataKey = NewDataKey();

        Assert.AreEqual(
            Crypto.BlindAssetKey(dataKey, Lower),
            Crypto.BlindAssetKey(dataKey, Lower.ToUpperInvariant()));
    }

    [TestMethod]
    public void DifferentContentGetsDifferentBlindedKeys()
    {
        using KeyMaterial dataKey = NewDataKey();

        Assert.AreNotEqual(
            Crypto.BlindAssetKey(dataKey, new string('a', 64)),
            Crypto.BlindAssetKey(dataKey, new string('b', 64)));
    }

    [TestMethod]
    public void DisposedKeyMaterialCannotBeRead()
    {
        // Reading zeros after disposal would "work" on both sides of an encrypt/decrypt pair and
        // hide the bug, so use-after-dispose has to throw.
        KeyMaterial key = NewDataKey();
        key.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = key.Span.Length);
    }

    [TestMethod]
    public void KeyMaterialDoesNotRenderItselfInAString()
    {
        using KeyMaterial key = NewDataKey();

        Assert.AreEqual("KeyMaterial(redacted)", key.ToString());
    }
}
