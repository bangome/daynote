using System.Security.Cryptography;
using Daynote.Core.Domain;

namespace Daynote.Core.Sync;

/// <summary>
/// The one-time recovery key shown at registration, which unwraps the data key when the password is
/// no longer known (docs/CLOUD_SYNC.md §4.6 and §4.8a).
/// </summary>
/// <remarks>
/// 128 bits of true randomness, so no stretching is needed before use as key material. Rendered in
/// Crockford base32, which excludes I, L, O, and U: the excluded letters are exactly the ones people
/// mistranscribe, and <see cref="Parse"/> maps the confusable characters back rather than rejecting
/// them — someone reading a key off paper should not be defeated by writing O for 0.
/// </remarks>
public readonly struct RecoveryKey : IEquatable<RecoveryKey>
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int KeyBytes = 16;
    private const int KeyChars = 26;
    private const int GroupSize = 4;

    private readonly byte[]? bytes;

    private RecoveryKey(byte[] bytes)
    {
        this.bytes = bytes;
    }

    public static RecoveryKey Generate() => new(RandomNumberGenerator.GetBytes(KeyBytes));

    public bool IsValid => bytes is not null;

    /// <summary>The raw key, for HKDF. Not the display form.</summary>
    public ReadOnlySpan<byte> Span => bytes ?? throw new InvalidOperationException(
        "An uninitialised recovery key has no value.");

    /// <summary>
    /// The display form: 26 characters in seven groups of four (the last holding two), e.g.
    /// <c>K7QM-3XPV-9ZTR-4BHN-6WYD-2FGH-J0</c>.
    /// </summary>
    public string ToDisplayString()
    {
        Span<char> raw = stackalloc char[KeyChars];
        Encode(Span, raw);

        int groups = (KeyChars + GroupSize - 1) / GroupSize;
        Span<char> formatted = stackalloc char[KeyChars + groups - 1];
        int target = 0;
        for (int i = 0; i < KeyChars; i += 1)
        {
            if (i > 0 && i % GroupSize == 0)
            {
                formatted[target] = '-';
                target += 1;
            }

            formatted[target] = raw[i];
            target += 1;
        }

        return new string(formatted);
    }

    /// <summary>
    /// Accepts the display form in any case, with or without separators. Rejects anything whose
    /// length or characters could not have come from <see cref="ToDisplayString"/>.
    /// </summary>
    public static DomainResult<RecoveryKey> Parse(string? text)
    {
        if (text is null)
        {
            return Invalid();
        }

        Span<char> normalized = stackalloc char[KeyChars];
        int count = 0;
        foreach (char character in text)
        {
            if (character is '-' or ' ' or '\t')
            {
                continue;
            }

            if (count == KeyChars)
            {
                return Invalid();
            }

            char upper = char.ToUpperInvariant(character);
            // Crockford's confusable mappings, applied before the alphabet lookup.
            upper = upper switch
            {
                'I' or 'L' => '1',
                'O' => '0',
                'U' => 'V',
                _ => upper,
            };

            if (!Alphabet.Contains(upper, StringComparison.Ordinal))
            {
                return Invalid();
            }

            normalized[count] = upper;
            count += 1;
        }

        if (count != KeyChars)
        {
            return Invalid();
        }

        byte[] decoded = new byte[KeyBytes];
        if (!TryDecode(normalized, decoded))
        {
            return Invalid();
        }

        return DomainResult<RecoveryKey>.Success(new RecoveryKey(decoded));
    }

    private static void Encode(ReadOnlySpan<byte> source, Span<char> destination)
    {
        int buffer = 0;
        int bits = 0;
        int written = 0;

        foreach (byte value in source)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                destination[written] = Alphabet[(buffer >> bits) & 0x1F];
                written += 1;
            }
        }

        if (bits > 0)
        {
            // 128 bits is not a multiple of 5: after 25 characters three bits remain, so the final
            // character holds them left-shifted, leaving two zero padding bits. TryDecode requires
            // those padding bits to still be zero.
            destination[written] = Alphabet[(buffer << (5 - bits)) & 0x1F];
        }
    }

    private static bool TryDecode(ReadOnlySpan<char> source, Span<byte> destination)
    {
        int buffer = 0;
        int bits = 0;
        int written = 0;

        foreach (char character in source)
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(character, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                destination[written] = (byte)((buffer >> bits) & 0xFF);
                written += 1;
            }
        }

        // Reject a key whose two trailing bits are not the zeros the encoder emits: without this
        // check 4 distinct strings decode to the same key, so a mistyped last character is accepted
        // silently and then fails much later at decrypt time with no explanation.
        return written == KeyBytes && (buffer & ((1 << bits) - 1)) == 0;
    }

    private static DomainResult<RecoveryKey> Invalid() =>
        DomainResult<RecoveryKey>.Failure(
            DomainErrorCode.InvalidRecoveryKey,
            "A recovery key is 26 letters and digits, usually shown in groups of four.");

    public bool Equals(RecoveryKey other) =>
        bytes is null
            ? other.bytes is null
            : other.bytes is not null && CryptographicOperations.FixedTimeEquals(bytes, other.bytes);

    public override bool Equals(object? obj) => obj is RecoveryKey other && Equals(other);

    /// <summary>Deliberately constant: a hash of key bytes in a dump or log is still a key leak.</summary>
    public override int GetHashCode() => 0;

    public static bool operator ==(RecoveryKey left, RecoveryKey right) => left.Equals(right);

    public static bool operator !=(RecoveryKey left, RecoveryKey right) => !left.Equals(right);

    public override string ToString() => $"{nameof(RecoveryKey)}(redacted)";
}
