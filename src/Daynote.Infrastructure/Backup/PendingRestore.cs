using System.IO;

namespace Daynote.Infrastructure.Backup;

/// <summary>
/// Applies a staged restore at startup — BEFORE the SQLite database opens — because the live writer
/// connection cannot be reopened in-process. If a valid staged database exists under
/// <see cref="PendingDirName"/>, the current data (db + blob trees) is moved aside into
/// <see cref="PreRestoreBackupDirName"/> (kept as a rollback), the staged data is moved into place, and
/// the staging folder is removed. All moves are within the same data root (same volume), so they are
/// atomic renames; a mid-way failure rolls back from the aside copy.
/// </summary>
public static class PendingRestore
{
    public const string PendingDirName = "restore-pending";
    public const string PreRestoreBackupDirName = "pre-restore-backup";

    private static readonly string[] DatabaseFiles = ["daynote.db", "daynote.db-wal", "daynote.db-shm"];
    private static readonly string[] BlobDirs = ["assets", "files"];

    /// <summary>Returns true when a staged restore was found and applied.</summary>
    public static bool ApplyIfPresent(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        string staging = Path.Combine(dataRoot, PendingDirName);
        if (!Directory.Exists(staging) || !File.Exists(Path.Combine(staging, "daynote.db")))
        {
            return false;
        }

        string aside = Path.Combine(dataRoot, PreRestoreBackupDirName);
        var moved = new List<(string From, string To)>();
        try
        {
            if (Directory.Exists(aside))
            {
                Directory.Delete(aside, recursive: true);
            }

            Directory.CreateDirectory(aside);

            // Move the current live data aside (rollback copy).
            foreach (string name in DatabaseFiles)
            {
                string live = Path.Combine(dataRoot, name);
                if (File.Exists(live))
                {
                    string dest = Path.Combine(aside, name);
                    File.Move(live, dest);
                    moved.Add((live, dest));
                }
            }

            foreach (string name in BlobDirs)
            {
                string live = Path.Combine(dataRoot, name);
                if (Directory.Exists(live))
                {
                    string dest = Path.Combine(aside, name);
                    Directory.Move(live, dest);
                    moved.Add((live, dest));
                }
            }

            // Move the staged data into place. Old WAL/SHM are intentionally NOT restored (the snapshot
            // is a single self-contained db), so only what the archive carried lands here.
            foreach (string name in DatabaseFiles)
            {
                string staged = Path.Combine(staging, name);
                if (File.Exists(staged))
                {
                    File.Move(staged, Path.Combine(dataRoot, name));
                }
            }

            foreach (string name in BlobDirs)
            {
                string staged = Path.Combine(staging, name);
                if (Directory.Exists(staged))
                {
                    Directory.Move(staged, Path.Combine(dataRoot, name));
                }
            }

            Directory.Delete(staging, recursive: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort rollback: put the live data back so the app still opens on the pre-restore state.
            foreach ((string from, string to) in moved)
            {
                try
                {
                    if (File.Exists(to))
                    {
                        if (File.Exists(from))
                        {
                            File.Delete(from);
                        }

                        File.Move(to, from);
                    }
                    else if (Directory.Exists(to) && !Directory.Exists(from))
                    {
                        Directory.Move(to, from);
                    }
                }
                catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException)
                {
                }
            }

            return false;
        }
    }
}
