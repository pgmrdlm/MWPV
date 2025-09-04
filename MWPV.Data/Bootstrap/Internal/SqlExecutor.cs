using MWPV.Data.Abstractions;

namespace MWPV.Data.Internal;

/// <summary>
/// Executes raw SQL batches using an <see cref="IConnectionFactory"/>.
/// Intended for bootstrap/migrations and simple commands.
/// </summary>
internal sealed class SqlExecutor : ISqlExecutor
{
    private readonly IConnectionFactory _factory;
    private readonly IDataLogSink _log;

    public SqlExecutor(IConnectionFactory factory, IDataLogSink log)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public int Execute(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            _log.Warn("SqlExecutor.EmptyBatch");
            return 0;
        }

        try
        {
            using var conn = _factory.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            var affected = cmd.ExecuteNonQuery();

            _log.Info("SqlExecutor.Executed", new { affected, length = sql.Length });
            return affected;
        }
        catch (Exception ex)
        {
            _log.Error("SqlExecutor.Failed", new { length = sql?.Length }, ex);
            throw;
        }
    }
}
