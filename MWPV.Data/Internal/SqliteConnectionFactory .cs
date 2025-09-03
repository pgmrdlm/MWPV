using System.Data.Common;
using Microsoft.Data.Sqlite;
using MWPV.Data.Abstractions;

namespace MWPV.Data.Internal;

public sealed class SqliteConnectionFactory : IConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public DbConnection CreateConnection()
    {
        return new SqliteConnection(_connectionString);
    }
}
