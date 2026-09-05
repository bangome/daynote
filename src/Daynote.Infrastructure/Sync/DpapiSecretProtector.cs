using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Daynote.Infrastructure.Sync;

/// <summary>Windows DPAPI, scoped to the current user.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    public byte[] Protect(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> entropy) =>
        ProtectedData.Protect(plaintext.ToArray(), entropy.ToArray(), DataProtectionScope.CurrentUser);

    public byte[] Unprotect(ReadOnlySpan<byte> sealedBytes, ReadOnlySpan<byte> entropy) =>
        ProtectedData.Unprotect(sealedBytes.ToArray(), entropy.ToArray(), DataProtectionScope.CurrentUser);
}
