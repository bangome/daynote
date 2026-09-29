using Daynote.Core.Sync;
using Daynote.Infrastructure.Sync;

namespace Daynote.Infrastructure.Persistence.Profiles;

/// <summary>
/// <see cref="IProfileHost"/> over <see cref="ProfileStore"/> and <see cref="ProfileImporter"/>: the
/// file-system half of signing in, signing out and deleting an account when every account has its
/// own folder (docs/PROFILES.md §5).
/// </summary>
/// <remarks>
/// <para>
/// Built once per composition, over the folder that composition runs on. It never touches that
/// folder's database except through the instance the app already holds, and it opens another
/// profile's database only briefly, for a count or an import, while nothing else in this process
/// has it open.
/// </para>
/// <para>
/// The session store for another folder has to be the same kind the host uses for its own — DPAPI
/// on Windows, the Keychain-sealed file on macOS, the platform keystore on a phone — so the host
/// supplies a factory rather than this class choosing one.
/// </para>
/// </remarks>
public sealed class ProfileHost : IProfileHost
{
    private readonly ProfileStore _profiles;
    private readonly string _currentFolder;
    private readonly SqliteDatabase _database;
    private readonly Func<string, ISyncSessionStore> _sessionStoreFor;
    private readonly Func<DateTimeOffset>? _utcNow;

    /// <summary>
    /// Serialises the pending import: start-up and the first sync run can both reach it, and two
    /// imports racing would each clear what the other had just moved.
    /// </summary>
    private readonly SemaphoreSlim _importGate = new(1, 1);

    /// <param name="profiles">The device's profiles, over <c>DaynoteAppOptions.BaseRoot</c>.</param>
    /// <param name="currentFolder">The folder this composition runs on (<c>DaynoteAppOptions.DataRoot</c>).</param>
    /// <param name="database">That folder's open database, the one the app writes through.</param>
    /// <param name="sessionStoreFor">Builds the session store that lives in a given profile folder.</param>
    /// <param name="utcNow">Clock for re-ordered imported notes; the system clock when null.</param>
    public ProfileHost(
        ProfileStore profiles,
        string currentFolder,
        SqliteDatabase database,
        Func<string, ISyncSessionStore> sessionStoreFor,
        Func<DateTimeOffset>? utcNow = null)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        ArgumentException.ThrowIfNullOrWhiteSpace(currentFolder);
        _currentFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentFolder));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _sessionStoreFor = sessionStoreFor ?? throw new ArgumentNullException(nameof(sessionStoreFor));
        _utcNow = utcNow;
        CurrentProfileId = ProfileIdOf(_currentFolder);
    }

    public string CurrentProfileId { get; }

    public bool IsLocalProfile => ProfileStore.IsLocal(CurrentProfileId);

    public bool HoldsStoredSession => File.Exists(Path.Combine(_currentFolder, ProtectedFileSyncSessionStore.FileName));

    public async ValueTask<LocalContent> CountLocalContentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsLocalProfile)
        {
            return Convert(ProfileImporter.CountUserContent(_database));
        }

        string path = _profiles.DatabasePathFor(ProfileStore.LocalProfileId);
        if (!File.Exists(path))
        {
            return default;
        }

        var local = new SqliteDatabase(new SqliteDatabaseOptions(path));
        try
        {
            local.Initialize();
            return Convert(ProfileImporter.CountUserContent(local));
        }
        finally
        {
            await local.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask HandOffToAccountAsync(
        string userId,
        SyncCredentials credentials,
        bool moveLocalNotes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        RequireUserId(userId);
        if (string.Equals(userId, CurrentProfileId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The account's own profile is already open; save the session there.");
        }

        if (!string.Equals(credentials.UserId, userId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The session belongs to another account.", nameof(credentials));
        }

        // Settings are seeded from the profile being left: that is the device the user is looking at.
        // Off the caller's thread: creating the database runs every migration, which the UI thread
        // that pressed the button should not sit through.
        string folder = await Task.Run(() => _profiles.CreateAccountProfile(userId, CurrentProfileId), cancellationToken)
            .ConfigureAwait(false);
        await _sessionStoreFor(folder).SaveAsync(credentials, cancellationToken).ConfigureAwait(false);

        if (moveLocalNotes)
        {
            _profiles.RequestImport(userId, ProfileStore.LocalProfileId);
        }
        else
        {
            // A marker from an earlier, abandoned Move must not act on a Keep.
            _profiles.ClearPendingImport(userId);
        }

        // Last: until the pointer moves, a failure above leaves the device on the profile it was on.
        _profiles.SetActiveProfile(userId);
    }

    public async ValueTask ReturnToLocalAsync(
        string userId,
        bool removeAccountData,
        bool keepNotesAsLocal,
        CancellationToken cancellationToken = default)
    {
        RequireUserId(userId);
        string folder = _profiles.AccountFolder(userId);

        if (keepNotesAsLocal && Directory.Exists(folder))
        {
            var local = new SqliteDatabase(
                new SqliteDatabaseOptions(_profiles.DatabasePathFor(ProfileStore.LocalProfileId)));
            try
            {
                await Task.Run(local.Initialize, cancellationToken).ConfigureAwait(false);
                await new ProfileImporter(local, _profiles.BaseRoot, _utcNow)
                    .ImportFromAsync(folder, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                await local.DisposeAsync().ConfigureAwait(false);
            }
        }

        // The session is not this class's business on a sign-out — AccountService cleared it through
        // the store the app holds — but a folder about to be removed must not keep a sealed session
        // around until the removal actually runs.
        if (removeAccountData)
        {
            await _sessionStoreFor(folder).ClearAsync(cancellationToken).ConfigureAwait(false);
            _profiles.MarkForRemoval(userId);
        }

        _profiles.SetActiveProfile(ProfileStore.LocalProfileId);
    }

    public async ValueTask<int> RunPendingImportAsync(CancellationToken cancellationToken = default)
    {
        await _importGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? source = _profiles.ReadPendingImport(CurrentProfileId);
            if (source is null)
            {
                return 0;
            }

            ProfileImportResult result = await new ProfileImporter(_database, _currentFolder, _utcNow)
                .MoveFromAsync(_profiles.FolderFor(source), cancellationToken)
                .ConfigureAwait(false);

            // Last, so a crash before it only repeats an import that finds its notes already here and
            // the source already cleared.
            _profiles.ClearPendingImport(CurrentProfileId);
            return result.NotesImported;
        }
        finally
        {
            _importGate.Release();
        }
    }

    private string ProfileIdOf(string folder)
    {
        if (string.Equals(folder, Path.TrimEndingDirectorySeparator(_profiles.BaseRoot), StringComparison.OrdinalIgnoreCase))
        {
            return ProfileStore.LocalProfileId;
        }

        string? parent = Path.GetDirectoryName(folder);
        string name = Path.GetFileName(folder);
        if (parent is not null
            && string.Equals(parent, Path.TrimEndingDirectorySeparator(_profiles.AccountsRoot), StringComparison.OrdinalIgnoreCase)
            && ProfileStore.IsValidUserId(name))
        {
            return name;
        }

        throw new ArgumentException("The folder is neither the local profile nor an account profile.", nameof(folder));
    }

    private static void RequireUserId(string userId)
    {
        if (!ProfileStore.IsValidUserId(userId))
        {
            // The id comes from the server. One that cannot name a folder is the server's fault, and
            // the user hears it as one rather than as a crash.
            throw new AccountException(AccountFailure.ServerError, "The server sent an account id this device cannot store.");
        }
    }

    private static LocalContent Convert(ProfileContent content) => new(content.Notes, content.Files);
}
