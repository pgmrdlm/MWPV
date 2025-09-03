using MWPV.Data.Abstractions;
using MWPV.Data.Internal;
using MWPV.Data.Bootstrap;

public sealed class DataModule
{
    public IConnectionFactory Factory { get; }
    public ISqlExecutor Executor { get; }

    private DataModule(IConnectionFactory f, ISqlExecutor e) { Factory = f; Executor = e; }

    public static DataModule Build(string connectionString, IDataLogSink log, Func<string, string> sqlResolver)
    {
        SqlCatalog.Init(sqlResolver);
        var factory = new SqliteConnectionFactory(connectionString);
        var exec = new SqlExecutor(factory, log);

        new SchemaBootstrapper(exec).EnsureCreated();  // idempotent

        return new DataModule(factory, exec);
    }
}
