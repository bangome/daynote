using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Persistence;

public sealed class MigrationRunner
{
    private static readonly Regex ResourcePattern = new(
        @"\.Migrations\.(?<version>\d{3})_(?<name>[A-Za-z0-9_]+)\.sql$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private readonly IReadOnlyList<SqliteMigration> _migrations;

    public MigrationRunner(IEnumerable<SqliteMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        _migrations = migrations.OrderBy(static migration => migration.Version).ToArray();
        if (_migrations.Count == 0)
        {
            throw new ArgumentException("At least one migration is required.", nameof(migrations));
        }

        if (_migrations.Select(static migration => migration.Version).Distinct().Count() != _migrations.Count)
        {
            throw new ArgumentException("Migration versions must be unique.", nameof(migrations));
        }
    }

    /// <summary>The highest migration version this build ships — the schema version a fresh db reaches.</summary>
    public int LatestVersion => _migrations[^1].Version;

    public static MigrationRunner FromEmbeddedResources()
    {
        var assembly = typeof(MigrationRunner).Assembly;
        var migrations = new List<SqliteMigration>();
        foreach (var resourceName in assembly.GetManifestResourceNames().Order(StringComparer.Ordinal))
        {
            var match = ResourcePattern.Match(resourceName);
            if (!match.Success)
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException("Embedded migration resource is missing.");
            using var reader = new StreamReader(stream);
            migrations.Add(
                new SqliteMigration(
                    int.Parse(match.Groups["version"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    match.Groups["name"].Value,
                    reader.ReadToEnd()));
        }

        return new MigrationRunner(migrations);
    }

    public int Apply(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var applied = ReadAppliedVersions(connection);
        ValidateAppliedPrefix(applied);

        foreach (var migration in _migrations)
        {
            if (applied.Contains(migration.Version))
            {
                continue;
            }

            ApplyOne(connection, migration);
            applied.Add(migration.Version);
        }

        return applied.Max();
    }

    private static HashSet<int> ReadAppliedVersions(SqliteConnection connection)
    {
        using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        exists.Parameters.AddWithValue("$name", "schema_versions");
        if (Convert.ToInt64(exists.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 0L)
        {
            return new HashSet<int>();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_versions ORDER BY version;";
        using var reader = command.ExecuteReader();
        var versions = new HashSet<int>();
        while (reader.Read())
        {
            versions.Add(reader.GetInt32(0));
        }

        return versions;
    }

    private void ValidateAppliedPrefix(IReadOnlySet<int> applied)
    {
        var expected = _migrations.Select(static migration => migration.Version).ToArray();
        var appliedOrdered = applied.Order().ToArray();
        if (appliedOrdered.Length > expected.Length)
        {
            throw new MigrationException(appliedOrdered[^1]);
        }

        for (var index = 0; index < appliedOrdered.Length; index++)
        {
            if (appliedOrdered[index] != expected[index])
            {
                throw new MigrationException(appliedOrdered[index]);
            }
        }
    }

    private static void ApplyOne(SqliteConnection connection, SqliteMigration migration)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            using var migrationCommand = connection.CreateCommand();
            migrationCommand.Transaction = transaction;
            migrationCommand.CommandText = migration.Sql;
            migrationCommand.ExecuteNonQuery();

            using var versionCommand = connection.CreateCommand();
            versionCommand.Transaction = transaction;
            versionCommand.CommandText = "INSERT INTO schema_versions(version,name,applied_utc) VALUES ($version,$name,$appliedUtc);";
            versionCommand.Parameters.AddWithValue("$version", migration.Version);
            versionCommand.Parameters.AddWithValue("$name", migration.Name);
            versionCommand.Parameters.AddWithValue("$appliedUtc", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            versionCommand.ExecuteNonQuery();
            transaction.Commit();
        }
        catch (SqliteException)
        {
            transaction.Rollback();
            throw new MigrationException(migration.Version);
        }
    }
}
