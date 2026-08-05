namespace Daynote.Core.Backup;

/// <summary>Metadata written into a backup archive so a restore can validate compatibility.</summary>
public sealed record BackupManifest(string AppVersion, int SchemaVersion, DateTimeOffset CreatedUtc);

/// <summary>Why a restore staging attempt did or did not succeed.</summary>
public enum RestoreStageStatus
{
    /// <summary>The archive was valid and its contents are staged for the next launch.</summary>
    Staged,

    /// <summary>Not a Daynote backup (missing database/manifest or unreadable zip).</summary>
    InvalidArchive,

    /// <summary>A backup from a newer schema than this build understands — refused.</summary>
    IncompatibleVersion,

    /// <summary>An unexpected I/O failure while staging; nothing was changed.</summary>
    Failed,
}

public sealed record RestoreStageResult(RestoreStageStatus Status)
{
    public bool IsStaged => Status == RestoreStageStatus.Staged;

    public static RestoreStageResult Staged() => new(RestoreStageStatus.Staged);

    public static RestoreStageResult Invalid() => new(RestoreStageStatus.InvalidArchive);

    public static RestoreStageResult Incompatible() => new(RestoreStageStatus.IncompatibleVersion);

    public static RestoreStageResult Failed() => new(RestoreStageStatus.Failed);
}

/// <summary>
/// Creates a portable backup of all Daynote data (the SQLite database plus the content-addressed
/// image/file blobs) as a single <c>.zip</c>, and stages a chosen backup for restore. The database is
/// captured with the SQLite online-backup API, so a backup is consistent without stopping the app.
/// A restore is only STAGED here — it is applied on the next launch, before the database opens, because
/// the live writer connection cannot be reopened in-process (see <c>PendingRestore</c>).
/// </summary>
public interface IBackupService
{
    Task CreateBackupAsync(string destinationZipPath, CancellationToken cancellationToken = default);

    Task<RestoreStageResult> StageRestoreAsync(string sourceZipPath, CancellationToken cancellationToken = default);
}
