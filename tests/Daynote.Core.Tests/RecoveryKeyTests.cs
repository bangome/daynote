using Daynote.Core.Domain;
using Daynote.Core.Sync;

namespace Daynote.Core.Tests;

[TestClass]
public sealed class RecoveryKeyTests
{
    [TestMethod]
    public void GeneratedKeyRoundTripsThroughItsDisplayForm()
    {
        RecoveryKey generated = RecoveryKey.Generate();

        DomainResult<RecoveryKey> parsed = RecoveryKey.Parse(generated.ToDisplayString());

        Assert.IsTrue(parsed.IsSuccess);
        Assert.AreEqual(generated, parsed.Value);
    }

    [TestMethod]
    public void DisplayFormIsTwentySixCharactersInGroupsOfFour()
    {
        string display = RecoveryKey.Generate().ToDisplayString();

        // 26 characters is six full groups plus a pair, so 32 characters including separators.
        Assert.AreEqual(32, display.Length);
        Assert.AreEqual(
            "4-4-4-4-4-4-2",
            string.Join('-', display.Split('-').Select(part => part.Length)));
    }

    [TestMethod]
    public void GeneratedKeysDiffer()
    {
        // A constant "random" key would pass every other test here while making every account's
        // recovery envelope openable with the same string.
        HashSet<string> seen = [];
        for (int i = 0; i < 50; i += 1)
        {
            Assert.IsTrue(seen.Add(RecoveryKey.Generate().ToDisplayString()));
        }
    }

    [TestMethod]
    public void SeparatorsAndCaseAreIgnoredWhenParsing()
    {
        RecoveryKey generated = RecoveryKey.Generate();
        string display = generated.ToDisplayString();

        foreach (string variant in new[]
        {
            display.ToLowerInvariant(),
            display.Replace("-", string.Empty, StringComparison.Ordinal),
            display.Replace("-", " ", StringComparison.Ordinal),
            $"  {display}\t",
        })
        {
            DomainResult<RecoveryKey> parsed = RecoveryKey.Parse(variant);
            Assert.IsTrue(parsed.IsSuccess, variant);
            Assert.AreEqual(generated, parsed.Value, variant);
        }
    }

    [TestMethod]
    public void ConfusableCharactersAreMappedRatherThanRejected()
    {
        // Someone reading a key off paper should not be defeated by writing O for 0 or l for 1.
        RecoveryKey generated = RecoveryKey.Generate();
        string mistranscribed = generated.ToDisplayString()
            .Replace('0', 'O')
            .Replace('1', 'l')
            .ToLowerInvariant();

        DomainResult<RecoveryKey> parsed = RecoveryKey.Parse(mistranscribed);

        Assert.IsTrue(parsed.IsSuccess, mistranscribed);
        Assert.AreEqual(generated, parsed.Value);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("K7QM-3XPV-9ZTR-4BHN-6WYD")]
    [DataRow("K7QM-3XPV-9ZTR-4BHN-6WYD-2FF")]
    [DataRow("K7QM-3XPV-9ZTR-4BHN-6WYD-2!")]
    public void MalformedKeyIsRejected(string? value)
    {
        DomainResult<RecoveryKey> parsed = RecoveryKey.Parse(value);

        Assert.IsFalse(parsed.IsSuccess);
        Assert.AreEqual(DomainErrorCode.InvalidRecoveryKey, parsed.Error.Code);
    }

    [TestMethod]
    public void KeyWithNonZeroPaddingBitsIsRejected()
    {
        // 128 bits is not a multiple of 5, so the final character carries two padding bits. If those
        // are not required to be zero, 4 different strings decode to the same key and a mistyped
        // last character is silently accepted, surfacing much later as a decrypt failure.
        string valid = RecoveryKey.Generate().ToDisplayString();
        char last = valid[^1];
        const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        int rejected = 0;
        foreach (char candidate in Alphabet)
        {
            if (candidate == last)
            {
                continue;
            }

            if (!RecoveryKey.Parse(valid[..^1] + candidate).IsSuccess)
            {
                rejected += 1;
            }
        }

        // Of the 32 possible final characters only the 8 whose low two bits are zero can be valid,
        // so 24 of the other 31 must be rejected.
        Assert.AreEqual(24, rejected);
    }

    [TestMethod]
    public void UninitialisedKeyReportsInvalidAndRefusesToExposeBytes()
    {
        RecoveryKey empty = default;

        Assert.IsFalse(empty.IsValid);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = empty.Span.Length);
    }

    [TestMethod]
    public void ToStringDoesNotLeakTheKey()
    {
        RecoveryKey key = RecoveryKey.Generate();
        string display = key.ToDisplayString();

        // A recovery key reaching a log or an exception message is a full compromise of the cloud
        // copy, so the default rendering must not be the real thing.
        Assert.AreEqual("RecoveryKey(redacted)", key.ToString());
        Assert.IsFalse(key.ToString().Contains(display[..4], StringComparison.Ordinal));
    }
}
