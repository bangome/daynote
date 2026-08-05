using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Daynote.Core.Backup;
using Daynote.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Backup;

/// <summary>
/// Zip-based backup/restore over the Daynote data root. Backup snapshots <c>daynote.db</c> with the
/// SQLite online-backup API (WAL-consistent, no app stop) and packs it with the <c>assets</c>/<c>files</c>
/// blob trees plus a <c>manifest.json</c>. Restore only validates and stages into
/// <see cref="PendingRestore.PendingDirName"/>; <see cref="PendingRestore.ApplyIfPresent"/> swaps it in
/// at the next launch before the database opens.
/// </summary>
public sealed class BackupService : IBackupService
{
    internal const string DatabaseEntryName = "daynote.db";
    internal const string ManifestEntryName = "manifest.json";
    internal const string AssetsDirName = "assets";
    internal const string FilesDirName = "files";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _dataRoot;
    private readonly string _databasePath;

    public BackupService(string dataRoot, string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _dataRoot = dataRoot;
        _databasePath = databasePath;
    }

    public async Task CreateBackupAsync(string destinationZipPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationZipPath);

        string snapshotPath = Path.Combine(Path.GetTempPath(), $"daynote-backup-{Guid.NewGuid():N}.db");
        string tempZipPath = destinationZipPath + ".tmp";
        try
        {
            int schemaVersion = SnapshotDatabase(snapshotPath);

            string? destDir = Path.GetDirectoryName(destinationZipPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            if (File.Exists(tempZipPath))
            {
                File.Delete(tempZipPath);
            }

            using (FileStream zipStream = File.Create(tempZipPath))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(snapshotPath, DatabaseEntryName, CompressionLevel.Optimal);

                var manifest = new BackupManifest(
                    Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0",
                    schemaVersion,
                    DateTimeOffset.UtcNow);
                ZipArchiveEntry manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                await using (Stream manifestStream = manifestEntry.Open())
                {
                    await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
                }

                AddDirectory(archive, Path.Combine(_dataRoot, AssetsDirName), AssetsDirName);
                AddDirectory(archive, Path.Combine(_dataRoot, FilesDirName), FilesDirName);
            }

            if (File.Exists(destinationZipPath))
            {
                File.Delete(destinationZipPath);
            }

            File.Move(tempZipPath, destinationZipPath);
        }
        finally
        {
            TryDeleteFile(snapshotPath);
            TryDeleteFile(tempZipPath);
        }
    }

    public Task<RestoreStageResult> StageRestoreAsync(string sourceZipPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceZipPath);
        string staging = Path.Combine(_dataRoot, PendingRestore.PendingDirName);

        try
        {
            if (!File.Exists(sourceZipPath))
            {
                return Task.FromResult(RestoreStageResult.Invalid());
            }

            using (ZipArchive archive = ZipFile.OpenRead(sourceZipPath))
            {
                ZipArchiveEntry? dbEntry = archive.GetEntry(DatabaseEntryName);
                ZipArchiveEntry? manifestEntry = archive.GetEntry(ManifestEntryName);
                if (dbEntry is null || manifestEntry is null)
                {
                    return Task.FromResult(RestoreStageResult.Invalid());
                }

                if (!IsCompatible(manifestEntry))
                {
                    return Task.FromResult(RestoreStageResult.Incompatible());
                }

                PrepareEmptyDirectory(staging);
                ExtractInto(archive, staging);
            }

            // A staged restore must carry a database; otherwise ApplyIfPresent would no-op silently.
            if (!File.Exists(Path.Combine(staging, DatabaseEntryName)))
            {
                TryDeleteDirectory(staging);
                return Task.FromResult(RestoreStageResult.Invalid());
            }

            return Task.FromResult(RestoreStageResult.Staged());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            TryDeleteDirectory(staging);
            return Task.FromResult(
                exception is InvalidDataException ? RestoreStageResult.Invalid() : RestoreStageResult.Failed());
        }
    }

    /// <summary>Copies the live database into <paramref name="snapshotPath"/> and returns its schema version.</summary>
    private int SnapshotDatabase(string snapshotPath)
    {
        TryDeleteFile(snapshotPath);
        string sourceConn = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString();
        string destConn = new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString();

        using var source = new SqliteConnection(sourceConn);
        source.Open();
        using (var dest = new SqliteConnection(destConn))
        {
            dest.Open();
            source.BackupDatabase(dest);
        }

        using var versionCommand = source.CreateCommand();
        versionCommand.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_versions;";
        return Convert.ToInt32(versionCommand.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool IsCompatible(ZipArchiveEntry manifestEntry)
    {
        try
        {
            using Stream stream = manifestEntry.Open();
            BackupManifest? manifest = JsonSerializer.Deserialize<BackupManifest>(stream, JsonOptions);
            if (manifest is null)
            {
                return false;
            }

            // A backup from an older/equal schema upgrades cleanly on open; a newer one is refused.
            return manifest.SchemaVersion <= MigrationRunner.FromEmbeddedResources().LatestVersion;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void ExtractInto(ZipArchive archive, string destinationRoot)
    {
        string fullRoot = Path.GetFullPath(destinationRoot);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/'))
            {
                continue; // directory marker
            }

            string target = Path.GetFullPath(Path.Combine(fullRoot, entry.FullName));
            // Guard against zip-slip: never write outside the staging root.
            if (!target.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !string.Equals(target, fullRoot, StringComparison.Ordinal))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static void AddDirectory(ZipArchive archive, string sourceDir, string entryPrefix)
    {
        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                continue; // in-flight store temp; never part of a backup
            }

            string relative = Path.GetRelativePath(sourceDir, file).Replace(Path.DirectorySeparatorChar, '/');
            archive.CreateEntryFromFile(file, $"{entryPrefix}/{relative}", CompressionLevel.Optimal);
        }
    }

    private static void PrepareEmptyDirectory(string path)
    {
        TryDeleteDirectory(path);
        Directory.CreateDirectory(path);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
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
