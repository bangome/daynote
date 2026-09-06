namespace Daynote.Infrastructure.Sync;

/// <summary>
/// Seals a small secret so that only this OS user on this machine can open it again. The Windows
/// implementation is DPAPI; macOS keeps an AES key in the login Keychain. Either way, copying the sealed
/// bytes to another account or machine yields nothing, and a failure to open reads as "signed out".
/// </summary>
public interface ISecretProtector
{
    /// <param name="entropy">Application-specific bytes mixed in so another app running as the same user cannot open the blob.</param>
    byte[] Protect(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> entropy);

    /// <exception cref="System.Security.Cryptography.CryptographicException">The blob was sealed by another user/machine, or is corrupt.</exception>
    byte[] Unprotect(ReadOnlySpan<byte> sealedBytes, ReadOnlySpan<byte> entropy);
}
