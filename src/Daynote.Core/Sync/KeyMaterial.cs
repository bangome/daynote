using System.Security.Cryptography;

namespace Daynote.Core.Sync;

/// <summary>
/// A fixed-size secret key held in memory, zeroed on disposal.
/// </summary>
/// <remarks>
/// Zeroing is best-effort: the CLR may have copied the bytes during a GC compaction, and Windows may
/// have paged them out. It still removes the obvious failure — a key sitting in a heap block that is
/// freed but not overwritten, then read back by whatever allocates next, or captured in a crash dump.
/// Use-after-dispose throws rather than silently reading zeros, because a key of all zeros would
/// "work" on both sides of an encrypt/decrypt pair and hide the bug.
/// </remarks>
public sealed class KeyMaterial : IDisposable
{
    /// <summary>Every key in the sync protocol is a 256-bit key; nothing here is variable-length.</summary>
    public const int Length = 32;

    private readonly byte[] bytes;
    private bool disposed;

    private KeyMaterial(byte[] bytes)
    {
        this.bytes = bytes;
    }

    /// <summary>Wraps a caller-owned buffer. The caller must not retain or reuse it.</summary>
    public static KeyMaterial Adopt(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != Length)
        {
            throw new ArgumentException($"A key must be exactly {Length} bytes.", nameof(value));
        }

        return new KeyMaterial(value);
    }

    public static KeyMaterial CopyFrom(ReadOnlySpan<byte> value)
    {
        if (value.Length != Length)
        {
            throw new ArgumentException($"A key must be exactly {Length} bytes.", nameof(value));
        }

        byte[] copy = new byte[Length];
        value.CopyTo(copy);
        return new KeyMaterial(copy);
    }

    public static KeyMaterial Random() => new(RandomNumberGenerator.GetBytes(Length));

    public ReadOnlySpan<byte> Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return bytes;
        }
    }

    /// <summary>Constant-time equality, for tests and for verifying a re-derivation matches.</summary>
    public bool Equals(KeyMaterial? other) =>
        other is not null && CryptographicOperations.FixedTimeEquals(Span, other.Span);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(bytes);
        disposed = true;
    }

    /// <summary>Never render key bytes, not even truncated: logs and exception messages leak.</summary>
    public override string ToString() => $"{nameof(KeyMaterial)}(redacted)";
}
