namespace Daynote.Core.Sync;

/// <summary>
/// The per-account stores on this device (docs/PROFILES.md), as the account flows need them: which
/// profile the app is running over, and the two moves between profiles — handing a fresh sign-in to
/// its own account folder, and going back to the local profile on sign-out or deletion.
/// </summary>
/// <remarks>
/// <para>
/// A port rather than a class because the folders, the marker files and the importer are
/// infrastructure, while the decision of <i>when</i> to move belongs to <see cref="AccountService"/>.
/// Nothing here switches the running app: every service holding the database is a singleton, so a
/// move only rewrites <c>profile.json</c> (and prepares the target), and the host rebuilds its
/// composition over the new folder afterwards (§8).
/// </para>
/// <para>
/// A profile id is <see cref="LocalProfileId"/> or the server's <c>user_id</c>.
/// </para>
/// </remarks>
public interface IProfileHost
{
    /// <summary>The id of the profile that never syncs and lives in the base folder.</summary>
    public const string LocalProfileId = "local";

    /// <summary>The profile this process was composed over. Fixed for the life of the composition.</summary>
    string CurrentProfileId { get; }

    /// <summary>True when <see cref="CurrentProfileId"/> is the local profile.</summary>
    bool IsLocalProfile { get; }

    /// <summary>
    /// True when the current profile's folder holds a session file, whether or not this device can
    /// still open it. A file that will not open (a lost keystore key) is a sign-in to repeat, not a
    /// signed-out account.
    /// </summary>
    bool HoldsStoredSession { get; }

    /// <summary>
    /// What the user wrote in the local profile — the untouched first-run sample does not count — for
    /// the "move N notes to this account?" question (§5.2 step 2). Read without switching.
    /// </summary>
    ValueTask<LocalContent> CountLocalContentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates (or reuses) the account's folder, writes <paramref name="credentials"/> into that
    /// folder's session store, records a pending import of the local profile's notes when
    /// <paramref name="moveLocalNotes"/> is set, and points <c>profile.json</c> at the account (§5.2
    /// step 3). The caller then asks the host to switch.
    /// </summary>
    ValueTask HandOffToAccountAsync(
        string userId,
        SyncCredentials credentials,
        bool moveLocalNotes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Points <c>profile.json</c> back at the local profile after a sign-out (§5.4) or a deletion
    /// (§5.5). <paramref name="keepNotesAsLocal"/> first copies the account's notes into the local
    /// profile; <paramref name="removeAccountData"/> then marks the account's folder for removal,
    /// which happens once no database has it open. The caller then asks the host to switch.
    /// </summary>
    ValueTask ReturnToLocalAsync(
        string userId,
        bool removeAccountData,
        bool keepNotesAsLocal,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the import a <i>Move</i> left for this profile, if there is one, and clears what it moved
    /// from the local profile (§5.2 step 4). Returns how many notes arrived; zero when nothing was
    /// pending. Safe to call on every start.
    /// </summary>
    ValueTask<int> RunPendingImportAsync(CancellationToken cancellationToken = default);
}

/// <summary>What the user wrote in a profile: notes, and attachments on their days.</summary>
public readonly record struct LocalContent(int Notes, int Files)
{
    public bool IsEmpty => Notes == 0 && Files == 0;
}

/// <summary>
/// A sign-in that belongs in another profile than the one open (§5.2, §5.3): the session has been
/// issued but saved nowhere yet. The caller asks <i>Move</i> or <i>Keep</i> when
/// <see cref="AsksToMove"/>, then hands it to <see cref="AccountService.CompleteHandOffAsync"/>.
/// </summary>
/// <remarks>
/// Holds the data key until it is handed off or disposed, so an abandoned question leaves nothing
/// behind but a server session that expires on its own.
/// </remarks>
public sealed class AccountHandOff : IDisposable
{
    internal AccountHandOff(SyncCredentials credentials, LocalContent localContent)
    {
        Credentials = credentials;
        LocalContent = localContent;
    }

    public string UserId => Credentials.UserId;

    public string Email => Credentials.Email;

    /// <summary>What the local profile holds that could come along.</summary>
    public LocalContent LocalContent { get; }

    /// <summary>False when the local profile holds nothing the user wrote: then there is no question.</summary>
    public bool AsksToMove => !LocalContent.IsEmpty;

    internal SyncCredentials Credentials { get; }

    public void Dispose() => Credentials.Dispose();
}

/// <summary>
/// What a sign-in produced. <see cref="HandOff"/> is null when the session was saved into the open
/// profile — the account whose folder this is — and non-null when it belongs in another one.
/// </summary>
public sealed record SignInResult(string Email, AccountHandOff? HandOff = null)
{
    public bool NeedsHandOff => HandOff is not null;
}
