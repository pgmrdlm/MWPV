using Microsoft.Data.Sqlite;
using MWPV.Data.Abstractions;

namespace MWPV.Data.Internal;

/// <summary>
/// Creates OPEN SQLite connections with safe defaults.
/// </summary>
internal sealed class SqliteConnectionFactory : IConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Safe defaults per connection
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = ON;";
            cmd.ExecuteNonQuery();
        }

        return conn;
    }
}
