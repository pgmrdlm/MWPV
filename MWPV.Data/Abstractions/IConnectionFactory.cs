using Microsoft.Data.Sqlite;

namespace MWPV.Data.Abstractions;

/// <summary>
/// Factory for creating OPEN SQLite connections.
/// Caller is responsible for disposing the returned connection.
/// </summary>
public interface IConnectionFactory
{
    /// <summary>
    /// Creates and opens a new <see cref="SqliteConnection"/>.
    /// Implementations should enable safe defaults (e.g., PRAGMA foreign_keys=ON).
    /// </summary>
    SqliteConnection CreateConnection();
}
