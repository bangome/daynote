using System.Buffers.Text;
using Daynote.Core.Domain;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Sync;

namespace Daynote.Infrastructure.Tests.Sync;

[TestClass]
public sealed class AesGcmSyncCryptoTests
{
    private const string Email = "alice@example.test";
    private const string UserId = "11111111-1111-4111-8111-111111111111";
    private const string NoteId = "22222222-2222-4222-8222-222222222222";

    private static readonly AesGcmSyncCrypto Crypto = new();

    /// <summary>
    /// The real profile costs 64 MiB and three passes per call, which is correct for a once-per-login
    /// derivation but would make this suite crawl. Tests that only need *a* key pair use the cheapest
    /// parameters the validator accepts; <see cref="ShippingArgon2idProfileDerivesAKey"/> covers the
    /// profile that actually ships.
    /// </summary>
    private static KdfParameters CheapArgon2id =>
        KdfParameters.Parse("""{"kdf":"argon2id","m":8192,"t":1,"p":1,"v":1}""").Value;

    private static KeyMaterial NewDataKey() => Crypto.GenerateDataKey();

    private static CipherScope NoteScope => CipherScope.Note(UserId, NoteId);

    [TestMethod]
    public void ShippingArgon2idProfileDerivesAKey()
    {
        using SyncKeySet keys = Crypto.DeriveKeys("correct horse battery staple", Email, KdfParameters.Argon2idDefault);

        Assert.AreEqual(KeyMaterial.Length, keys.AuthKey.Span.Length);
        Assert.AreEqual(KeyMaterial.Length, keys.Kek.Span.Length);
    }

    [TestMethod]
    public void DerivationIsDeterministicForTheSamePasswordAndEmail()
    {
        using SyncKeySet first = Crypto.DeriveKeys("pw", Email, CheapArgon2id);
        using SyncKeySet second = Crypto.DeriveKeys("pw", Email, CheapArgon2id);

        // Sign-in on a second device must reach the same keys with no server-held salt.
        Assert.IsTrue(first.AuthKey.Equals(second.AuthKey));
        Assert.IsTrue(first.Kek.Equals(second.Kek));
    }

    [TestMethod]
    public void TheAuthKeySentToTheServerCannotUnwrapAnything()
    {
        // This is the whole end-to-end property: the server holds a verifier over auth_key, so if
        // auth_key could also open the envelope, the operator could read every note.
        using SyncKeySet keys = Crypto.DeriveKeys("pw", Email, CheapArgon2id);
        using KeyMaterial dataKey = NewDataKey();
        CipherScope scope = CipherScope.DataKey(DataKeyPurpose.Password, Email);
        string envelope = Crypto.WrapDataKey(dataKey, keys.Kek, scope);

        Assert.IsFalse(keys.AuthKey.Equals(keys.Kek));

        DomainResult<KeyMaterial> opened = Crypto.UnwrapDataKey(envelope, keys.AuthKey, scope);
        Assert.IsFalse(opened.IsSuccess);
        Assert.AreEqual(DomainErrorCode.CiphertextAuthenticationFailed, opened.Error.Code);
    }

    [TestMethod]
    public void EmailIsNormalisedSoCaseAndPaddingDoNotChangeTheKeys()
    {
        // The server lowercases and trims; if the client did not, an account registered as
        // "Alice@..." could never be unlocked after signing in as "alice@...".
        using SyncKeySet canonical = Crypto.DeriveKeys("pw", Email, CheapArgon2id);
        using SyncKeySet messy = Crypto.DeriveKeys("pw", "  ALICE@Example.TEST ", CheapArgon2id);

        Assert.IsTrue(canonical.Kek.Equals(messy.Kek));
    }

    [TestMethod]
    public void DifferentEmailsGiveDifferentKeysForTheSamePassword()
    {
        using SyncKeySet alice = Crypto.DeriveKeys("shared", Email, CheapArgon2id);
        using SyncKeySet bob = Crypto.DeriveKeys("shared", "bob@example.test", CheapArgon2id);

        Assert.IsFalse(alice.Kek.Equals(bob.Kek));
    }

    [TestMethod]
    public void WrongPasswordFailsToUnwrapTheDataKey()
    {
        using SyncKeySet real = Crypto.DeriveKeys("right", Email, CheapArgon2id);
        using SyncKeySet wrong = Crypto.DeriveKeys("wrong", Email, CheapArgon2id);
        using KeyMaterial dataKey = NewDataKey();
        CipherScope scope = CipherScope.DataKey(DataKeyPurpose.Password, Email);

        string envelope = Crypto.WrapDataKey(dataKey, real.Kek, scope);

        Assert.IsFalse(Crypto.UnwrapDataKey(envelope, wrong.Kek, scope).IsSuccess);

        DomainResult<KeyMaterial> opened = Crypto.UnwrapDataKey(envelope, real.Kek, scope);
        Assert.IsTrue(opened.IsSuccess);
        using KeyMaterial recovered = opened.Value;
        Assert.IsTrue(dataKey.Equals(recovered));
    }

    [TestMethod]
    public void TheRecoveryKeyOpensTheSameDataKeyAsThePassword()
    {
        // The §4.8a promise: a user who lost their password but kept the recovery key gets their
        // notes back, because both envelopes wrap one data key.
        using SyncKeySet keys = Crypto.DeriveKeys("pw", Email, CheapArgon2id);
        using KeyMaterial dataKey = NewDataKey();
        RecoveryKey recoveryKey = RecoveryKey.Generate();
        using KeyMaterial recoveryKek = Crypto.DeriveRecoveryKek(recoveryKey);

        string byPassword = Crypto.WrapDataKey(
            dataKey, keys.Kek, CipherScope.DataKey(DataKeyPurpose.Password, Email));
        string byRecovery = Crypto.WrapDataKey(
            dataKey, recoveryKek, CipherScope.DataKey(DataKeyPurpose.Recovery, Email));

        using KeyMaterial viaPassword = Crypto
            .UnwrapDataKey(byPassword, keys.Kek, CipherScope.DataKey(DataKeyPurpose.Password, Email))
            .Value;
        using KeyMaterial viaRecovery = Crypto
            .UnwrapDataKey(byRecovery, recoveryKek, CipherScope.DataKey(DataKeyPurpose.Recovery, Email))
            .Value;

        Assert.IsTrue(viaPassword.Equals(viaRecovery));
        Assert.IsTrue(viaPassword.Equals(dataKey));
    }

    [TestMethod]
    public void RecoveryKeyParsedFromItsDisplayFormStillOpensTheEnvelope()
    {
        // The user retypes the key from paper, so the round trip through the display form is the
        // path that actually matters, not the in-memory key.
        using KeyMaterial dataKey = NewDataKey();
        RecoveryKey generated = RecoveryKey.Generate();
        CipherScope scope = CipherScope.DataKey(DataKeyPurpose.Recovery, Email);

        string envelope;
        using (KeyMaterial kek = Crypto.DeriveRecoveryKek(generated))
        {
            envelope = Crypto.WrapDataKey(dataKey, kek, scope);
        }

        RecoveryKey retyped = RecoveryKey.Parse(generated.ToDisplayString()).Value;
        using KeyMaterial retypedKek = Crypto.DeriveRecoveryKek(retyped);

        using KeyMaterial opened = Crypto.UnwrapDataKey(envelope, retypedKek, scope).Value;
        Assert.IsTrue(dataKey.Equals(opened));
    }

    [TestMethod]
    public void ADifferentRecoveryKeyDoesNotOpenTheEnvelope()
    {
        using KeyMaterial dataKey = NewDataKey();
        CipherScope scope = CipherScope.DataKey(DataKeyPurpose.Recovery, Email);
        using KeyMaterial mine = Crypto.DeriveRecoveryKek(RecoveryKey.Generate());
        using KeyMaterial theirs = Crypto.DeriveRecoveryKek(RecoveryKey.Generate());

        string envelope = Crypto.WrapDataKey(dataKey, mine, scope);

        Assert.IsFalse(Crypto.UnwrapDataKey(envelope, theirs, scope).IsSuccess);
    }

    [TestMethod]
    public void ThePasswordAndRecoveryEnvelopesAreNotInterchangeable()
    {
        // Distinct scopes mean the server cannot serve the recovery envelope where the password
        // envelope belongs and have the client silently accept it.
        using KeyMaterial dataKey = NewDataKey();
        using SyncKeySet keys = Crypto.DeriveKeys("pw", Email, CheapArgon2id);
        CipherScope passwordScope = CipherScope.DataKey(DataKeyPurpose.Password, Email);
        CipherScope recoveryScope = CipherScope.DataKey(DataKeyPurpose.Recovery, Email);

        string envelope = Crypto.WrapDataKey(dataKey, keys.Kek, passwordScope);

        Assert.IsFalse(Crypto.UnwrapDataKey(envelope, keys.Kek, recoveryScope).IsSuccess);
    }

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
    public void TheAuthKeyIsSentAsThirtyTwoBase64UrlBytes()
    {
        using SyncKeySet keys = Crypto.DeriveKeys("pw", Email, CheapArgon2id);

        string wire = keys.AuthKeyForServer();

        // The Worker rejects anything that is not exactly 32 base64url bytes
        // (cloud/worker/src/validate.ts).
        Assert.AreEqual(43, wire.Length);
        Assert.AreEqual(KeyMaterial.Length, Base64Url.DecodeFromChars(wire).Length);
    }

    [TestMethod]
    public void Pbkdf2FallbackProducesUsableKeys()
    {
        // The fallback exists for a build that cannot take the Argon2id dependency; it must produce
        // a working, distinct key set rather than silently degrading to the Argon2id path.
        KdfParameters cheapPbkdf2 = KdfParameters.Parse("""{"kdf":"pbkdf2-sha256","i":100000,"v":1}""").Value;
        using SyncKeySet viaPbkdf2 = Crypto.DeriveKeys("pw", Email, cheapPbkdf2);
        using SyncKeySet viaArgon2 = Crypto.DeriveKeys("pw", Email, CheapArgon2id);
        using KeyMaterial dataKey = NewDataKey();
        CipherScope scope = CipherScope.DataKey(DataKeyPurpose.Password, Email);

        string envelope = Crypto.WrapDataKey(dataKey, viaPbkdf2.Kek, scope);

        Assert.IsFalse(viaPbkdf2.Kek.Equals(viaArgon2.Kek));
        Assert.IsTrue(Crypto.UnwrapDataKey(envelope, viaPbkdf2.Kek, scope).IsSuccess);
        Assert.IsFalse(Crypto.UnwrapDataKey(envelope, viaArgon2.Kek, scope).IsSuccess);
    }

    [TestMethod]
    public void KdfParametersRoundTripThroughTheWireFormat()
    {
        foreach (KdfParameters original in new[] { KdfParameters.Argon2idDefault, KdfParameters.Pbkdf2Default })
        {
            DomainResult<KdfParameters> parsed = KdfParameters.Parse(original.ToJson());

            Assert.IsTrue(parsed.IsSuccess, original.ToString());
            Assert.AreEqual(original, parsed.Value);
        }
    }

    [TestMethod]
    [DataRow(null, DisplayName = "null")]
    [DataRow("", DisplayName = "empty")]
    [DataRow("not json", DisplayName = "not json")]
    [DataRow("[]", DisplayName = "array")]
    [DataRow("""{"kdf":"bcrypt"}""", DisplayName = "unsupported kdf")]
    [DataRow("""{"kdf":"argon2id","t":3,"p":4}""", DisplayName = "missing memory")]
    [DataRow("""{"kdf":"argon2id","m":"lots","t":3,"p":4}""", DisplayName = "non-numeric memory")]
    [DataRow("""{"kdf":"argon2id","m":1024,"t":3,"p":4}""", DisplayName = "memory below the floor")]
    [DataRow("""{"kdf":"argon2id","m":4194304,"t":3,"p":4}""", DisplayName = "memory above the ceiling")]
    [DataRow("""{"kdf":"argon2id","m":65536,"t":0,"p":4}""", DisplayName = "zero iterations")]
    [DataRow("""{"kdf":"pbkdf2-sha256","i":1000}""", DisplayName = "too few pbkdf2 iterations")]
    public void HostileKdfParametersAreRejected(string? json)
    {
        // A compromised server that could dictate m=8 or t=0 would reduce the password derivation to
        // nothing; one that could dictate m=8 GiB would wedge the app at the sign-in screen.
        DomainResult<KdfParameters> parsed = KdfParameters.Parse(json);

        Assert.IsFalse(parsed.IsSuccess);
        Assert.AreEqual(DomainErrorCode.InvalidKdfParameters, parsed.Error.Code);
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
