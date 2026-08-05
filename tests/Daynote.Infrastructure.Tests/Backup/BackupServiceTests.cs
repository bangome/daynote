using System.IO.Compression;
using Daynote.Core.Backup;
using Daynote.Infrastructure.Backup;
using Daynote.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Infrastructure.Tests.Backup;

[TestClass]
public sealed class BackupServiceTests
{
    private readonly List<string> _roots = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (string root in _roots)
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    [TestMethod]
    public async Task Backup_then_restore_round_trips_database_and_blobs()
    {
        string source = NewRoot();
        SeedDatabase(source, marker: "hello");
        WriteBlob(source, "assets", "ab", "asset1.png", [1, 2, 3]);
        WriteBlob(source, "files", "cd", "file1.txt", [4, 5, 6, 7]);

        string zip = Path.Combine(NewRoot(), "backup.zip");
        await ServiceFor(source).CreateBackupAsync(zip);

        Assert.IsTrue(File.Exists(zip));
        using (ZipArchive archive = ZipFile.OpenRead(zip))
        {
            CollectionAssert.IsSubsetOf(
                new[] { "daynote.db", "manifest.json", "assets/ab/asset1.png", "files/cd/file1.txt" },
                archive.Entries.Select(entry => entry.FullName).ToArray());
        }

        // Restore into a fresh, empty data root and apply as the next-launch swap would.
        string target = NewRoot();
        RestoreStageResult staged = await ServiceFor(target).StageRestoreAsync(zip);
        Assert.AreEqual(RestoreStageStatus.Staged, staged.Status);
        Assert.IsTrue(Directory.Exists(Path.Combine(target, PendingRestore.PendingDirName)));

        Assert.IsTrue(PendingRestore.ApplyIfPresent(target));
        Assert.IsTrue(File.Exists(Path.Combine(target, "daynote.db")));
        Assert.IsTrue(File.Exists(Path.Combine(target, "assets", "ab", "asset1.png")));
        Assert.IsTrue(File.Exists(Path.Combine(target, "files", "cd", "file1.txt")));
        Assert.AreEqual("hello", ReadMarker(target));
        Assert.IsFalse(Directory.Exists(Path.Combine(target, PendingRestore.PendingDirName)), "Staging is removed after apply.");
    }

    [TestMethod]
    public async Task StageRestore_rejects_an_archive_without_a_database()
    {
        string bogusZip = Path.Combine(NewRoot(), "not-a-backup.zip");
        using (ZipArchive archive = ZipFile.Open(bogusZip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("random.txt");
        }

        string target = NewRoot();
        RestoreStageResult result = await ServiceFor(target).StageRestoreAsync(bogusZip);

        Assert.AreEqual(RestoreStageStatus.InvalidArchive, result.Status);
        Assert.IsFalse(Directory.Exists(Path.Combine(target, PendingRestore.PendingDirName)), "A rejected archive leaves no staging.");
    }

    [TestMethod]
    public async Task ApplyIfPresent_preserves_current_data_in_pre_restore_backup()
    {
        string source = NewRoot();
        SeedDatabase(source, marker: "restored");
        string zip = Path.Combine(NewRoot(), "backup.zip");
        await ServiceFor(source).CreateBackupAsync(zip);

        // Target already has its own data; a restore must move it aside, not destroy it.
        string target = NewRoot();
        SeedDatabase(target, marker: "original");

        await ServiceFor(target).StageRestoreAsync(zip);
        Assert.IsTrue(PendingRestore.ApplyIfPresent(target));

        Assert.AreEqual("restored", ReadMarker(target), "Live data is the restored backup.");
        string aside = Path.Combine(target, PendingRestore.PreRestoreBackupDirName);
        Assert.IsTrue(File.Exists(Path.Combine(aside, "daynote.db")));
        Assert.AreEqual("original", ReadMarkerAt(Path.Combine(aside, "daynote.db")), "The pre-restore copy is the previous data.");
    }

    [TestMethod]
    public void ApplyIfPresent_is_a_no_op_without_staging()
    {
        string target = NewRoot();
        SeedDatabase(target, marker: "live");
        Assert.IsFalse(PendingRestore.ApplyIfPresent(target));
        Assert.AreEqual("live", ReadMarker(target));
    }

    private static BackupService ServiceFor(string dataRoot) =>
        new(dataRoot, Path.Combine(dataRoot, "daynote.db"));

    private string NewRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "daynote-backup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _roots.Add(root);
        return root;
    }

    private static void SeedDatabase(string dataRoot, string marker)
    {
        string dbPath = Path.Combine(dataRoot, "daynote.db");
        var database = new SqliteDatabase(new SqliteDatabaseOptions(dbPath));
        database.Initialize();
        database.DisposeAsync().AsTask().GetAwaiter().GetResult();

        using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=false");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO settings(key, value, updated_utc) VALUES('test.marker', $v, '2026-01-01T00:00:00Z');";
        command.Parameters.AddWithValue("$v", marker);
        command.ExecuteNonQuery();
    }

    private static string ReadMarker(string dataRoot) => ReadMarkerAt(Path.Combine(dataRoot, "daynote.db"));

    private static string ReadMarkerAt(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=false");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = 'test.marker';";
        return (string?)command.ExecuteScalar() ?? string.Empty;
    }

    private static void WriteBlob(string dataRoot, string tree, string shard, string name, byte[] bytes)
    {
        string dir = Path.Combine(dataRoot, tree, shard);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, name), bytes);
    }
}
