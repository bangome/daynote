namespace Daynote.Core.Sync;

/// <summary>
/// One local store per account (docs/PROFILES.md): handing a sign-in to its own folder, preparing an
/// account profile on its first start, and the owner check that keeps one account's database from
/// ever syncing as another.
/// </summary>
/// <remarks>
/// Everything here is inert without an <see cref="IProfileHost"/>: a single-root service keeps the
/// behaviour it always had, which is also what the pre-profile layout needs while a failed migration
/// leaves it in place.
/// </remarks>
public sealed partial class AccountService
{
    /// <summary>True when this service runs over per-account profiles.</summary>
    public bool UsesProfiles => profiles is not null;

    /// <summary>
    /// True when the app runs over an account's own folder, so signing out or deleting the account
    /// ends in a switch back to the local profile.
    /// </summary>
    public bool IsAccountProfile => profiles is { IsLocalProfile: false };

    /// <summary>Changes still waiting to be pushed; what sign-out warns about (docs/PROFILES.md §5.4).</summary>
    public ValueTask<int> CountPendingChangesAsync(CancellationToken cancellationToken = default) =>
        store.CountPendingChangesAsync(cancellationToken);

    /// <summary>
    /// Hands a sign-in to its account's folder (docs/PROFILES.md §5.2 step 3): creates or reuses the
    /// folder, writes the session there, records the import when <paramref name="moveLocalNotes"/> is
    /// set, and points the device at that account. The caller then asks the host to switch. Consumes
    /// <paramref name="handOff"/> once it succeeds; after a failure it is still usable, so the same
    /// answer can be tried again without another trip through the browser.
    /// </summary>
    public async ValueTask CompleteHandOffAsync(
        AccountHandOff handOff,
        bool moveLocalNotes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handOff);
        if (profiles is null)
        {
            throw new InvalidOperationException("A hand-off needs per-account profiles.");
        }

        await profiles.HandOffToAccountAsync(
            handOff.UserId,
            handOff.Credentials,
            moveLocalNotes && handOff.AsksToMove,
            cancellationToken).ConfigureAwait(false);
        handOff.Dispose();
    }

    /// <summary>
    /// After <see cref="DeleteAccountAsync"/>: keeps the account's notes as local notes, or not, then
    /// removes the account's folder and points the device back at the local profile (docs/PROFILES.md
    /// §5.5). Returns true when the host has to switch; false without profiles, where the notes were
    /// always left in place.
    /// </summary>
    public async ValueTask<bool> FinishDeletionAsync(
        bool keepNotesAsLocal,
        CancellationToken cancellationToken = default)
    {
        if (!IsAccountProfile)
        {
            return false;
        }

        await profiles!.ReturnToLocalAsync(
            profiles.CurrentProfileId,
            removeAccountData: true,
            keepNotesAsLocal,
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// An account profile's first start (docs/PROFILES.md §5.2 step 4): marks its database signed in
    /// as the account whose folder this is, then runs the import a <i>Move</i> left for it. Cheap and
    /// idempotent, so hosts call it on every start before the notes are first read — which is what
    /// makes moved notes visible without waiting for a sync to announce them. Returns how many notes
    /// the import brought in.
    /// </summary>
    public async ValueTask<int> PrepareProfileAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAccountProfile)
        {
            return 0;
        }

        SyncStateSnapshot state = await store.ReadStateAsync(cancellationToken).ConfigureAwait(false);
        if (!state.IsSignedIn)
        {
            SyncCredentials? credentials = await sessions.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (credentials is null)
            {
                return 0;
            }

            using (credentials)
            {
                if (!string.Equals(credentials.UserId, profiles!.CurrentProfileId, StringComparison.Ordinal))
                {
                    // Someone else's session in this folder. ResumeAsync reports it; nothing is adopted.
                    return 0;
                }

                await store.SignInAsync(credentials.UserId, DataKeyGeneration, cancellationToken).ConfigureAwait(false);
                if (credentials.Protection == KeyProtection.Passphrase && credentials.DataKey is null)
                {
                    // What AdoptSessionAsync records for a locked account signing in where it runs.
                    await store.SetLockedAsync(true, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        else if (!string.Equals(state.UserId, profiles!.CurrentProfileId, StringComparison.Ordinal))
        {
            return 0;
        }

        try
        {
            return await profiles.RunPendingImportAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            // A bad row, or the database busy under the MCP server: the marker stays, so the next start
            // tries again, and nothing about it may stop this account from starting or syncing what it
            // already has. The notes it would have moved are still in the local profile, untouched.
            System.Diagnostics.Trace.TraceWarning($"The pending profile import failed and will be retried: {exception}");
            return 0;
        }
    }

    /// <summary>
    /// True when the open account folder holds a session file, readable or not. Tells an account whose
    /// key this device lost (sign in again) apart from one whose deletion was never finished (§5.5).
    /// </summary>
    public bool HoldsStoredSession => profiles?.HoldsStoredSession ?? false;

    private async ValueTask<SignInResult> BeginHandOffAsync(
        SyncCredentials credentials,
        CancellationToken cancellationToken)
    {
        LocalContent local;
        try
        {
            local = await profiles!.CountLocalContentAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            credentials.Dispose();
            throw;
        }

        return new SignInResult(credentials.Email, new AccountHandOff(credentials, local));
    }

    /// <summary>
    /// The owner check (docs/PROFILES.md §4): the folder's name, the database's <c>sync_state</c> user
    /// and the session's user are one id. An account folder whose database has no user yet is on its
    /// first start and is prepared here. The local profile has no name to check, so there the
    /// database and the session only have to agree — the pre-profile layout a failed migration left.
    /// </summary>
    private async ValueTask<bool> IsOwnedByAsync(SyncCredentials credentials, CancellationToken cancellationToken)
    {
        if (profiles!.IsLocalProfile)
        {
            SyncStateSnapshot local = await store.ReadStateAsync(cancellationToken).ConfigureAwait(false);
            return !local.IsSignedIn || string.Equals(local.UserId, credentials.UserId, StringComparison.Ordinal);
        }

        if (!string.Equals(credentials.UserId, profiles.CurrentProfileId, StringComparison.Ordinal))
        {
            return false;
        }

        await PrepareProfileAsync(cancellationToken).ConfigureAwait(false);
        SyncStateSnapshot state = await store.ReadStateAsync(cancellationToken).ConfigureAwait(false);
        return string.Equals(state.UserId, profiles.CurrentProfileId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Re-authenticating inside an account profile must find that account's database — or one with no
    /// owner yet. A database owned by someone else is refused, never re-labelled (docs/PROFILES.md §4).
    /// </summary>
    private async ValueTask RefuseAnotherOwnerAsync(string userId, CancellationToken cancellationToken)
    {
        SyncStateSnapshot state = await store.ReadStateAsync(cancellationToken).ConfigureAwait(false);
        if (state.IsSignedIn && !string.Equals(state.UserId, userId, StringComparison.Ordinal))
        {
            throw new AccountException(
                AccountFailure.InvalidCredentials,
                "This profile's database belongs to another account; it will not be re-labelled.");
        }
    }
}
