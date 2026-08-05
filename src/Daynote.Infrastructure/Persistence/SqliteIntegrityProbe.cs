using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Persistence;

public interface ISqliteIntegrityProbe
{
    bool Check(SqliteConnection connection);
}

public sealed class SqliteIntegrityProbe : ISqliteIntegrityProbe
{
    public bool Check(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        using var reader = command.ExecuteReader();
        var resultCount = 0;
        var isValid = true;
        while (reader.Read())
        {
            resultCount++;
            isValid &= string.Equals(reader.GetString(0), "ok", StringComparison.Ordinal);
        }

        return isValid && resultCount == 1;
    }
}
