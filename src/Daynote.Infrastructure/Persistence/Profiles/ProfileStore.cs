using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Daynote.Infrastructure.Backup;
using Daynote.Infrastructure.Sync;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Persistence.Profiles;

/// <summary>
/// One local store per account (docs/PROFILES.md): which profile is active, where each one lives, and
/// the one-time move of a signed-in legacy root into its account folder.
/// </summary>
/// <remarks>
/// <para>
/// The base root stays the <b>local</b> profile, so a user who never signed in keeps every file where
/// it has always been. Each account ever signed in on this device gets <c>accounts/&lt;userId&gt;/</c>,
/// holding everything <c>DaynoteAppOptions.DataRoot</c> addresses — database, attachments, conflict
/// copies and that account's <c>credentials.dat</c>. Notes of two accounts therefore never share a
/// database, which is the whole point: no query, sync run or bug can mix what is not in one file.
/// </para>
/// <para>
/// Everything here is plain file-system work over folders no database has open yet, so it is
/// synchronous and runs at start-up, before the composition is built. The exception is creating an
/// account profile, which a running app does at sign-in; it only reads the profile being left.
/// </para>
/// <para>
/// A profile is named by a string: <see cref="LocalProfileId"/>, or the server's <c>user_id</c>. The
/// user id becomes a folder name, so it is accepted only as a canonical lower-case UUID — never as a
/// path, and never in a second spelling that a case-insensitive file system would fold onto the first.
/// </para>
/// </remarks>
public sealed class ProfileStore
{
    /// <summary>The profile that lives directly in the base root and never syncs.</summary>
    public const string LocalProfileId = "local";

    public const string PointerFileName = "profile.json";

    public const string AccountsDirectoryName = "accounts";

    public const string DatabaseFileName = "daynote.db";

    /// <summary>
    /// Marker in an account folder saying "import the notes of profile X on your next start"
    /// (docs/PROFILES.md §5.2 step 4). A file rather than a settings row so it can be written before
    /// that profile's database is ever opened.
    /// </summary>
    public const string PendingImportFileName = "pending-import.json";

    /// <summary>
    /// Marker in an account folder saying "remove me" (docs/PROFILES.md §5.4 <i>Remove</i>, §5.5). The
    /// running app still has that folder's database open when the user asks, and Windows will not
    /// rename a folder holding an open file, so the removal waits for the next start — or, on a
    /// phone, for the composition to be disposed. A marked folder is treated as gone from the moment
    /// the marker is written.
    /// </summary>
    public const string RemovalPendingFileName = "remove-pending";

    /// <summary>
    /// Where the migration assembles <c>accounts/</c> before renaming it into place. A sibling of the
    /// real folder, so the final rename stays on one volume and is atomic: <c>accounts/</c> either does
    /// not exist or is complete, and its existence is what marks the migration as done.
    /// </summary>
    public const string MigrationStagingDirectoryName = "accounts.migrating";

    private const string CreatingPrefix = ".creating-";
    private const string RemovingPrefix = ".removing-";
    private const string CustomTitlePrefix = "note.custom-title.";
    private const int FormatVersion = 1;

    /// <summary>
    /// What a signed-in legacy root moves into its account folder. <c>restore-pending</c> goes too: a
    /// restore staged before the update was staged against this account's database, and left behind it
    /// would be applied to the new, empty local profile instead.
    /// </summary>
    private static readonly string[] LegacyFiles =
        [DatabaseFileName, DatabaseFileName + "-wal", DatabaseFileName + "-shm", ProtectedFileSyncSessionStore.FileName];

    private static readonly string[] LegacyDirectories =
        [BackupService.FilesDirName, BackupService.AssetsDirName, FileSystemConflictSink.DirectoryName, PendingRestore.PendingDirName];

    public ProfileStore(string baseRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseRoot);
        BaseRoot = Path.GetFullPath(baseRoot);
        AccountsRoot = Path.Combine(BaseRoot, AccountsDirectoryName);
    }

    /// <summary>The data root every process resolves (<see cref="DaynoteDataRoot"/>); also the local profile.</summary>
    public string BaseRoot { get; }

    public string AccountsRoot { get; }

    private string PointerPath => Path.Combine(BaseRoot, PointerFileName);

    /// <summary>
    /// True for a canonical lower-case UUID, the only shape the server issues and the only one that is
    /// safe to use as a folder name.
    /// </summary>
    public static bool IsValidUserId([NotNullWhen(true)] string? userId) =>
        userId is { Length: 36 }
        && Guid.TryParseExact(userId, "D", out Guid parsed)
        && string.Equals(parsed.ToString("D"), userId, StringComparison.Ordinal);

    public static bool IsLocal(string profileId) =>
        string.Equals(profileId, LocalProfileId, StringComparison.Ordinal);

    /// <summary>
    /// The profile <c>profile.json</c> names, or <see cref="LocalProfileId"/> when it is missing,
    /// unreadable, names something that is not a user id, or names an account whose folder is gone.
    /// </summary>
    /// <remarks>
    /// Every doubt resolves to local on purpose: the local profile is always there, holds no
    /// credentials, and never syncs, so falling back to it can show the wrong notes but can never send
    /// one account's notes to another.
    /// </remarks>
    public string ReadActiveProfileId()
    {
        string? named = ReadJsonString(PointerPath, "active");
        return IsValidUserId(named) && Directory.Exists(Path.Combine(AccountsRoot, named)) && !IsMarkedForRemoval(named)
            ? named
            : LocalProfileId;
    }

    /// <summary>The active profile's folder: what <c>DaynoteAppOptions.DataRoot</c> becomes.</summary>
    public string ResolveActiveFolder() => FolderFor(ReadActiveProfileId());

    /// <summary>The base root for <see cref="LocalProfileId"/>, else <see cref="AccountFolder"/>.</summary>
    public string FolderFor(string profileId)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        return IsLocal(profileId) ? BaseRoot : AccountFolder(profileId);
    }

    /// <summary>
    /// <c>accounts/&lt;userId&gt;</c>, whether or not it exists yet. It is also the root to give
    /// <c>ProtectedFileSyncSessionStore</c> (or <c>DpapiSyncSessionStore</c>) to write that account's
    /// session into, which is how a sign-in hands off to a profile it is not running in.
    /// </summary>
    public string AccountFolder(string userId)
    {
        if (!IsValidUserId(userId))
        {
            throw new ArgumentException(
                "An account profile is named by the server's user id, a lower-case UUID.",
                nameof(userId));
        }

        return Path.Combine(AccountsRoot, userId);
    }

    public string DatabasePathFor(string profileId) => Path.Combine(FolderFor(profileId), DatabaseFileName);

    /// <summary>
    /// Points <c>profile.json</c> at a profile, atomically. The composition reads it only at start-up,
    /// so the switch takes effect when the host rebuilds (docs/PROFILES.md §8).
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">The account has no folder yet; create it first.</exception>
    public void SetActiveProfile(string profileId)
    {
        string folder = FolderFor(profileId);
        if (!IsLocal(profileId) && !Directory.Exists(folder))
        {
            // Writing it anyway would silently resolve to local on the next start, which is the kind
            // of surprise this class exists to rule out.
            throw new DirectoryNotFoundException("The account profile has not been created.");
        }

        WritePointer(profileId);
    }

    /// <summary>Accounts that still have a folder on this device, in ordinal order.</summary>
    public IReadOnlyList<string> ListAccounts()
    {
        if (!Directory.Exists(AccountsRoot))
        {
            return [];
        }

        var accounts = new List<string>();
        foreach (string directory in Directory.EnumerateDirectories(AccountsRoot))
        {
            // Skips the half-made and half-removed folders below, which are never valid user ids.
            string name = Path.GetFileName(directory);
            if (IsValidUserId(name) && !IsMarkedForRemoval(name))
            {
                accounts.Add(name);
            }
        }

        accounts.Sort(StringComparer.Ordinal);
        return accounts;
    }

    /// <summary>
    /// Creates <c>accounts/&lt;userId&gt;/</c> with a fresh database whose device-local settings are copied
    /// from <paramref name="seedSettingsFromProfileId"/> (docs/PROFILES.md §6). An existing folder is
    /// reused untouched — signing back in must find its cursor, outbox and notes where it left them,
    /// and nothing is ever copied back over them. Returns the folder.
    /// </summary>
    /// <remarks>
    /// Built under a dot-named sibling and renamed into place, so a crash half-way leaves nothing that
    /// <see cref="ListAccounts"/> or <see cref="ReadActiveProfileId"/> would take for an account.
    /// </remarks>
    public string CreateAccountProfile(string userId, string seedSettingsFromProfileId)
    {
        string folder = AccountFolder(userId);
        string seedDatabase = DatabasePathFor(seedSettingsFromProfileId);
        if (Directory.Exists(folder) && IsMarkedForRemoval(userId))
        {
            // The user asked for this copy to go and it has not gone yet. Signing in again must not
            // bring back what they removed, so it goes now; if something still holds it open, the
            // account starts over under its own name once that is released (the marker stays).
            RemoveAccount(userId);
            if (Directory.Exists(folder))
            {
                throw new IOException("The account's previous folder is still waiting to be removed.");
            }
        }

        if (Directory.Exists(folder))
        {
            return folder;
        }

        Directory.CreateDirectory(AccountsRoot);
        string staging = Path.Combine(AccountsRoot, CreatingPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            CreateSeededDatabase(Path.Combine(staging, DatabaseFileName), seedDatabase);
            Directory.Move(staging, folder);
        }
        catch (IOException) when (Directory.Exists(folder))
        {
            // Someone else created it in the meantime. Theirs is as good as ours.
            TryDeleteDirectory(staging);
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }

        return folder;
    }

    /// <summary>
    /// Deletes an account's folder — its notes, outbox, attachments and credentials — from this device.
    /// If <c>profile.json</c> named it, it is pointed at local first, so a crash mid-delete never leaves
    /// the pointer on a half-deleted profile. Returns false when there was no folder.
    /// </summary>
    /// <remarks>
    /// The caller must have closed that profile's database first: on Windows an open file makes the
    /// rename fail, which is the right outcome — nothing is deleted and the exception says why.
    /// </remarks>
    public bool RemoveAccount(string userId)
    {
        string folder = AccountFolder(userId);
        if (string.Equals(ReadJsonString(PointerPath, "active"), userId, StringComparison.Ordinal))
        {
            WritePointer(LocalProfileId);
        }

        if (!Directory.Exists(folder))
        {
            return false;
        }

        // Rename first, delete second: the rename is atomic, so the account is gone in one step, and
        // whatever a failed recursive delete leaves behind is a dot folder swept on a later start.
        string doomed = Path.Combine(AccountsRoot, RemovingPrefix + Guid.NewGuid().ToString("N"));
        Directory.Move(folder, doomed);
        TryDeleteDirectory(doomed);
        return true;
    }

    /// <summary>
    /// Marks an account's folder for removal and, if <c>profile.json</c> named it, points it at local.
    /// The folder is deleted by <see cref="RemoveMarkedAccounts"/> once nothing has it open; until then
    /// it is invisible to <see cref="ReadActiveProfileId"/> and <see cref="ListAccounts"/>.
    /// </summary>
    public void MarkForRemoval(string userId)
    {
        string folder = AccountFolder(userId);
        if (string.Equals(ReadJsonString(PointerPath, "active"), userId, StringComparison.Ordinal))
        {
            WritePointer(LocalProfileId);
        }

        if (Directory.Exists(folder))
        {
            WriteBytesAtomically(Path.Combine(folder, RemovalPendingFileName), []);
        }
    }

    public bool IsMarkedForRemoval(string userId) =>
        File.Exists(Path.Combine(AccountFolder(userId), RemovalPendingFileName));

    /// <summary>
    /// Deletes every account folder marked for removal. Runs at start-up, before any database is
    /// opened, and on a phone after the old composition is disposed. A folder something still holds
    /// open stays marked for the next attempt. Returns how many went.
    /// </summary>
    public int RemoveMarkedAccounts()
    {
        if (!Directory.Exists(AccountsRoot))
        {
            return 0;
        }

        int removed = 0;
        foreach (string directory in Directory.EnumerateDirectories(AccountsRoot))
        {
            string name = Path.GetFileName(directory);
            if (!IsValidUserId(name) || !IsMarkedForRemoval(name))
            {
                continue;
            }

            try
            {
                removed += RemoveAccount(name) ? 1 : 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }

    /// <summary>
    /// Records that <paramref name="targetProfileId"/> should import the notes of
    /// <paramref name="sourceProfileId"/> on its next start (docs/PROFILES.md §5.2, §5.5).
    /// </summary>
    public void RequestImport(string targetProfileId, string sourceProfileId)
    {
        string target = FolderFor(targetProfileId);
        _ = FolderFor(sourceProfileId);
        if (string.Equals(targetProfileId, sourceProfileId, StringComparison.Ordinal))
        {
            throw new ArgumentException("A profile cannot import from itself.", nameof(sourceProfileId));
        }

        if (!Directory.Exists(target))
        {
            throw new DirectoryNotFoundException("The profile to import into has not been created.");
        }

        WriteJsonAtomically(Path.Combine(target, PendingImportFileName), "source", sourceProfileId);
    }

    /// <summary>The profile a pending import reads from, or null when none is pending (or the marker is unreadable).</summary>
    public string? ReadPendingImport(string profileId)
    {
        string? source = ReadJsonString(Path.Combine(FolderFor(profileId), PendingImportFileName), "source");
        return source is not null
            && (IsLocal(source) || IsValidUserId(source))
            && !string.Equals(source, profileId, StringComparison.Ordinal)
                ? source
                : null;
    }

    public void ClearPendingImport(string profileId)
    {
        string marker = Path.Combine(FolderFor(profileId), PendingImportFileName);
        if (File.Exists(marker))
        {
            File.Delete(marker);
        }
    }

    /// <summary>
    /// The one-time move to this layout (docs/PROFILES.md §7). Runs on every start, before any database
    /// is opened, and does nothing once <c>accounts/</c> exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A base database signed in as U moves — database, attachments, conflict copies, credentials —
    /// into <c>accounts/U/</c>, a fresh local database gets the device settings, and the pointer names
    /// U: the user stays signed in with the same notes, cursor and outbox. Anything else stays where it
    /// is and the pointer says local.
    /// </para>
    /// <para>
    /// Moves go into a staging folder that is renamed into place last. A failure before that rename is
    /// rolled back and reported as <see cref="ProfileMigrationOutcome.Failed"/>, leaving the legacy root
    /// as it was, so the app runs exactly as the previous version did and the next start retries. A
    /// process killed mid-move leaves the staging folder, which the next run moves back first.
    /// </para>
    /// </remarks>
    public ProfileMigrationResult MigrateLegacyLayout()
    {
        SweepLeftovers();
        RemoveMarkedAccounts();
        if (Directory.Exists(AccountsRoot))
        {
            return new ProfileMigrationResult(ProfileMigrationOutcome.AlreadyMigrated);
        }

        string userId;
        try
        {
            RecoverInterruptedMigration();
            string? signedIn = ReadSignedInUser(Path.Combine(BaseRoot, DatabaseFileName));
            if (!IsValidUserId(signedIn))
            {
                // A user id that is not a UUID cannot name a folder, so it also stays put: the base
                // keeps working exactly as it did, signed in, as the local profile.
                if (!File.Exists(PointerPath))
                {
                    WritePointer(LocalProfileId);
                }

                return new ProfileMigrationResult(ProfileMigrationOutcome.StayedLocal);
            }

            userId = signedIn;
            MoveBaseIntoAccount(userId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException)
        {
            return new ProfileMigrationResult(ProfileMigrationOutcome.Failed, Error: exception);
        }

        try
        {
            // After the commit, so a failure here costs only the local profile its settings: it would be
            // created empty on first use anyway, and the account — the user's actual data — is in place.
            CreateSeededDatabase(Path.Combine(BaseRoot, DatabaseFileName), DatabasePathFor(userId));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException)
        {
        }

        return new ProfileMigrationResult(ProfileMigrationOutcome.MovedToAccount, userId);
    }

    private void MoveBaseIntoAccount(string userId)
    {
        string databasePath = Path.Combine(BaseRoot, DatabaseFileName);
        Checkpoint(databasePath);

        string staging = Path.Combine(BaseRoot, MigrationStagingDirectoryName);
        string stagedAccount = Path.Combine(staging, userId);
        byte[]? previousPointer = File.Exists(PointerPath) ? File.ReadAllBytes(PointerPath) : null;
        var moved = new List<(string From, string To)>();
        try
        {
            Directory.CreateDirectory(stagedAccount);
            foreach (string name in LegacyFiles)
            {
                string from = Path.Combine(BaseRoot, name);
                if (File.Exists(from))
                {
                    string to = Path.Combine(stagedAccount, name);
                    File.Move(from, to);
                    moved.Add((from, to));
                }
            }

            foreach (string name in LegacyDirectories)
            {
                string from = Path.Combine(BaseRoot, name);
                if (Directory.Exists(from))
                {
                    string to = Path.Combine(stagedAccount, name);
                    Directory.Move(from, to);
                    moved.Add((from, to));
                }
            }

            // Before the commit, and harmless there: a pointer naming a folder that does not exist yet
            // still resolves to local. Written after, a failure between the two would leave the data
            // in accounts/U with a pointer that never looks there.
            WritePointer(userId);

            Directory.Move(staging, AccountsRoot);
        }
        catch
        {
            bool restored = true;
            for (int index = moved.Count - 1; index >= 0; index--)
            {
                (string from, string to) = moved[index];
                try
                {
                    if (File.Exists(to))
                    {
                        File.Move(to, from);
                    }
                    else if (Directory.Exists(to))
                    {
                        Directory.Move(to, from);
                    }
                }
                catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException)
                {
                    restored = false;
                }
            }

            try
            {
                if (previousPointer is null)
                {
                    File.Delete(PointerPath);
                }
                else
                {
                    WriteBytesAtomically(PointerPath, previousPointer);
                }
            }
            catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException)
            {
            }

            // Only an empty staging folder may go. One still holding data is exactly what
            // RecoverInterruptedMigration puts back on the next start.
            if (restored)
            {
                TryDeleteDirectory(staging);
            }

            throw;
        }
    }

    /// <summary>
    /// Puts back whatever a killed migration left in the staging folder. Refuses — throws, so the
    /// migration reports <see cref="ProfileMigrationOutcome.Failed"/> — when the base already has an
    /// item of the same name: which copy is the user's is then not something to guess.
    /// </summary>
    private void RecoverInterruptedMigration()
    {
        string staging = Path.Combine(BaseRoot, MigrationStagingDirectoryName);
        if (!Directory.Exists(staging))
        {
            return;
        }

        foreach (string stagedAccount in Directory.EnumerateDirectories(staging))
        {
            foreach (string name in LegacyFiles)
            {
                string from = Path.Combine(stagedAccount, name);
                string to = Path.Combine(BaseRoot, name);
                if (File.Exists(from))
                {
                    if (File.Exists(to))
                    {
                        throw new IOException($"An interrupted profile migration left '{name}' in two places.");
                    }

                    File.Move(from, to);
                }
            }

            foreach (string name in LegacyDirectories)
            {
                string from = Path.Combine(stagedAccount, name);
                string to = Path.Combine(BaseRoot, name);
                if (Directory.Exists(from))
                {
                    if (Directory.Exists(to))
                    {
                        throw new IOException($"An interrupted profile migration left '{name}' in two places.");
                    }

                    Directory.Move(from, to);
                }
            }
        }

        Directory.Delete(staging, recursive: true);
    }

    private void SweepLeftovers()
    {
        if (!Directory.Exists(AccountsRoot))
        {
            return;
        }

        try
        {
            foreach (string directory in Directory.EnumerateDirectories(AccountsRoot))
            {
                string name = Path.GetFileName(directory);
                if (name.StartsWith(CreatingPrefix, StringComparison.Ordinal) ||
                    name.StartsWith(RemovingPrefix, StringComparison.Ordinal))
                {
                    TryDeleteDirectory(directory);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// The account the legacy database is signed in as, read without writing a byte: a signed-out root
    /// has to be left exactly as it was. Null when there is no database, no <c>sync_state</c> (a
    /// database from before migration 004), or no account.
    /// </summary>
    private static string? ReadSignedInUser(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return null;
        }

        // Even a read-only connection leaves a -shm file behind on a WAL database. With no WAL content
        // to honour, the file is opened immutable instead, which creates nothing. A WAL that does hold
        // pages (the app did not shut down cleanly) may hold the sign-in itself, so it gets a normal
        // read; its -shm is then already there.
        string wal = databasePath + "-wal";
        bool walHasPages = File.Exists(wal) && new FileInfo(wal).Length > 0;
        using SqliteConnection connection = walHasPages
            ? new SqliteConnectionFactory(new SqliteDatabaseOptions(databasePath)).OpenReadConnection()
            : OpenImmutable(databasePath);
        using (SqliteCommand probe = connection.CreateCommand())
        {
            probe.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name='sync_state');";
            if (Convert.ToInt32(probe.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 0)
            {
                return null;
            }
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT user_id FROM sync_state WHERE id=1;";
        return command.ExecuteScalar() as string;
    }

    private static SqliteConnection OpenImmutable(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            // A URI filename, the only way to pass immutable; AbsoluteUri escapes the spaces in
            // "Application Support".
            DataSource = new Uri(databasePath).AbsoluteUri + "?immutable=1",
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Folds the WAL into the database file so the move carries one self-contained file. The
    /// <c>-wal</c>/<c>-shm</c> that remain (if a reader held on) move alongside, so either way the
    /// account's database arrives whole.
    /// </summary>
    private static void Checkpoint(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return;
        }

        using SqliteConnection connection =
            new SqliteConnectionFactory(new SqliteDatabaseOptions(databasePath)).OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Creates a database with the full schema and copies the source's device-local settings into it:
    /// every <c>settings</c> row except the per-note <c>note.custom-title.*</c> flags, which are content
    /// and travel only with an import (docs/PROFILES.md §6).
    /// </summary>
    private static void CreateSeededDatabase(string databasePath, string settingsSourcePath)
    {
        var database = new SqliteDatabase(new SqliteDatabaseOptions(databasePath));
        try
        {
            database.Initialize();
        }
        finally
        {
            // Start-up code with no caller to await it; the writer's shutdown never captures a context.
            database.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (!File.Exists(settingsSourcePath))
        {
            return;
        }

        var rows = new List<(string Key, string Value, string UpdatedUtc)>();
        using (SqliteConnection source =
            new SqliteConnectionFactory(new SqliteDatabaseOptions(settingsSourcePath)).OpenReadConnection())
        using (SqliteCommand read = source.CreateCommand())
        {
            // substr rather than LIKE: LIKE folds ASCII case, and the prefix is matched exactly.
            read.CommandText = "SELECT key,value,updated_utc FROM settings WHERE substr(key,1,$length)<>$prefix;";
            read.Parameters.AddWithValue("$length", CustomTitlePrefix.Length);
            read.Parameters.AddWithValue("$prefix", CustomTitlePrefix);
            using SqliteDataReader reader = read.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        using SqliteConnection connection =
            new SqliteConnectionFactory(new SqliteDatabaseOptions(databasePath)).OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();
        foreach ((string key, string value, string updatedUtc) in rows)
        {
            using SqliteCommand write = connection.CreateCommand();
            write.Transaction = transaction;
            write.CommandText =
                "INSERT INTO settings(key,value,updated_utc) VALUES($key,$value,$utc) " +
                "ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_utc=excluded.updated_utc;";
            write.Parameters.AddWithValue("$key", key);
            write.Parameters.AddWithValue("$value", value);
            write.Parameters.AddWithValue("$utc", updatedUtc);
            write.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private void WritePointer(string profileId) => WriteJsonAtomically(PointerPath, "active", profileId);

    /// <summary>
    /// <c>{"version":1,"&lt;name&gt;":"&lt;value&gt;"}</c>, written by hand rather than through a serializer
    /// so it needs no reflection: the phone heads are trimmed.
    /// </summary>
    private static void WriteJsonAtomically(string path, string name, string value)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", FormatVersion);
            writer.WriteString(name, value);
            writer.WriteEndObject();
        }

        WriteBytesAtomically(path, buffer.ToArray());
    }

    /// <summary>Temp file in the same folder, flushed, then renamed over the target.</summary>
    private static void WriteBytesAtomically(string path, byte[] content)
    {
        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The path has no parent directory.");
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }

            throw;
        }
    }

    /// <summary>
    /// One string property of a version-1 marker file, or null for anything else: missing, unreadable,
    /// not JSON, another version, or the property absent.
    /// </summary>
    private static string? ReadJsonString(string path, string name)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("version", out JsonElement version)
                && version.ValueKind == JsonValueKind.Number
                && version.TryGetInt32(out int number)
                && number == FormatVersion
                && root.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
