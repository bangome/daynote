using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Persistence;

public interface IFtsCapabilityProbe
{
    FtsCapabilityResult Check(SqliteConnection connection);
}

public sealed class FtsCapabilityProbe : IFtsCapabilityProbe
{
    public FtsCapabilityResult Check(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        try
        {
            using var compileOption = connection.CreateCommand();
            compileOption.CommandText = "SELECT sqlite_compileoption_used('ENABLE_FTS5');";
            if (Convert.ToInt64(compileOption.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != 1L)
            {
                return FtsCapabilityResult.Fts5Unavailable;
            }
        }
        catch (SqliteException)
        {
            return FtsCapabilityResult.Fts5Unavailable;
        }

        try
        {
            using var create = connection.CreateCommand();
            create.CommandText = "CREATE VIRTUAL TABLE temp.daynote_trigram_capability USING fts5(value, tokenize='trigram');";
            create.ExecuteNonQuery();
            using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE temp.daynote_trigram_capability;";
            drop.ExecuteNonQuery();
            return FtsCapabilityResult.Available;
        }
        catch (SqliteException)
        {
            return FtsCapabilityResult.TrigramUnavailable;
        }
    }
}
