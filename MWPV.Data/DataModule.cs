using MWPV.Data.Abstractions;
using MWPV.Data.Bootstrap;
using MWPV.Data.Internal;
using MWPV.Data.Repositories;

namespace MWPV.Data;

/// <summary>
/// Composition root for the Data layer when not using a DI container.
/// Wires up the connection factory, executor, SQL catalog, schema bootstrapper,
/// and exposes repositories.
/// </summary>
public sealed class DataModule
{
    /// <summary>Central entry for creating open SQLite connections.</summary>
    public IConnectionFactory Factory { get; }

    /// <summary>Executes SQL batches against the database.</summary>
    public ISqlExecutor Executor { get; }

    /// <summary>Repository for managing Categories.</summary>
    public ICategoryRepository Categories { get; }

    private DataModule(
        IConnectionFactory factory,
        ISqlExecutor executor,
        ICategoryRepository categories)
    {
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        Executor = executor ?? throw new ArgumentNullException(nameof(executor));
        Categories = categories ?? throw new ArgumentNullException(nameof(categories));
    }

    /// <summary>
    /// Builds a DataModule using the provided connection string, log sink, and SQL resolver.
    /// Initializes SqlCatalog and runs schema creation/upgrade (idempotent).
    /// </summary>
    /// <param name="connectionString">A fully-formed SQLite connection string (app builds this).</param>
    /// <param name="log">Temporary or real log sink implementing IDataLogSink.</param>
    /// <param name="sqlResolver">Maps a logical SQL name (e.g., "Logs_Insert_V2.sql") to full SQL text.</param>
    public static DataModule Build(string connectionString, IDataLogSink log, Func<string, string> sqlResolver)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
        if (log is null) throw new ArgumentNullException(nameof(log));
        if (sqlResolver is null) throw new ArgumentNullException(nameof(sqlResolver));

        // Initialize the SQL catalog so scripts can be resolved by name.
        SqlCatalog.Init(sqlResolver);

        // Compose the core pieces.
        var factory = new SqliteConnectionFactory(connectionString);
        var executor = new SqlExecutor(factory, log);
        var categories = new CategoryRepository(factory, log);

        // Ensure schema exists / is current (idempotent).
        new SchemaBootstrapper(executor, log).EnsureCreated();

        return new DataModule(factory, executor, categories);
    }

    /// <summary>
    /// Convenience overload that uses a console logger during development.
    /// </summary>
    public static DataModule Build(string connectionString, Func<string, string> sqlResolver)
        => Build(connectionString, new ConsoleLogSink(), sqlResolver);
}
