using Daynote.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Infrastructure.Portable.Tests.Persistence;

/// <summary>
/// <see cref="SqliteDatabase.TryOpenCurrent"/>: the open a reader beside the app uses (the Android
/// widgets and reminder receiver), which must never migrate.
/// </summary>
[TestClass]
public sealed class TryOpenCurrentTests
{
    [TestMethod]
    public async Task A_current_database_opens_for_reading_and_writing()
    {
        using var folder = new TempDirectory();
        string path = Path.Combine(folder.Path, "daynote.db");
        await using (var app = new SqliteDatabase(new SqliteDatabaseOptions(path)))
        {
            app.Initialize();
        }

        await using var beside = new SqliteDatabase(new SqliteDatabaseOptions(path));

        Assert.IsTrue(beside.TryOpenCurrent());
        int written = await beside.WriteAsync(static (c, t, _) => Execute(
            c, t, "INSERT INTO settings(key, value, updated_utc) VALUES ('t', 'v', '2026-10-09T00:00:00Z');"));
        Assert.AreEqual(1, written);
        SqliteConnection.ClearAllPools();
    }

    [TestMethod]
    public async Task A_database_behind_this_build_is_left_alone()
    {
        using var folder = new TempDirectory();
        string path = Path.Combine(folder.Path, "daynote.db");
        long latest;
        await using (var app = new SqliteDatabase(new SqliteDatabaseOptions(path)))
        {
            app.Initialize();
            await app.WriteAsync(static (c, t, _) => Execute(
                c, t, "DELETE FROM schema_versions WHERE version = (SELECT MAX(version) FROM schema_versions);"));
            latest = MaxVersion(app);
        }

        await using var beside = new SqliteDatabase(new SqliteDatabaseOptions(path));

        Assert.IsFalse(beside.TryOpenCurrent());
        Assert.ThrowsExactly<InvalidOperationException>(() => beside.OpenReadConnection());

        await using var after = new SqliteDatabase(new SqliteDatabaseOptions(path));
        after.Initialize();
        Assert.IsTrue(MaxVersion(after) > latest, "The app's own open still migrates; the reader beside it did not.");
        SqliteConnection.ClearAllPools();
    }

    private static int Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    private static long MaxVersion(SqliteDatabase database)
    {
        using SqliteConnection connection = database.OpenReadConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(version) FROM schema_versions;";
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
