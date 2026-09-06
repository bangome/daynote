using System.Runtime.Versioning;
using Daynote.Core.Sync;

namespace Daynote.Infrastructure.Sync;

/// <summary>
/// The Windows session store: <see cref="ProtectedFileSyncSessionStore"/> sealed with DPAPI. Kept as a
/// named type so the WPF composition and its tests read the same as before the store went portable.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSyncSessionStore : ISyncSessionStore
{
    internal const string FileName = ProtectedFileSyncSessionStore.FileName;

    private readonly ProtectedFileSyncSessionStore inner;

    public DpapiSyncSessionStore(string dataRoot)
    {
        inner = new ProtectedFileSyncSessionStore(dataRoot, new DpapiSecretProtector());
    }

    public ValueTask<SyncCredentials?> LoadAsync(CancellationToken cancellationToken = default) =>
        inner.LoadAsync(cancellationToken);

    public ValueTask SaveAsync(SyncCredentials credentials, CancellationToken cancellationToken = default) =>
        inner.SaveAsync(credentials, cancellationToken);

    public ValueTask UpdateTokensAsync(
        string accessToken,
        DateTimeOffset accessExpiresUtc,
        string refreshToken,
        CancellationToken cancellationToken = default) =>
        inner.UpdateTokensAsync(accessToken, accessExpiresUtc, refreshToken, cancellationToken);

    public ValueTask ClearAsync(CancellationToken cancellationToken = default) => inner.ClearAsync(cancellationToken);
}
