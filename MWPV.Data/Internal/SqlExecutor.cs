using System.Data.Common;
using Dapper;
using MWPV.Data.Abstractions;

namespace MWPV.Data.Internal;

public sealed class SqlExecutor : ISqlExecutor
{
    private readonly IConnectionFactory _factory;
    private readonly IDataLogSink _log;

    public SqlExecutor(IConnectionFactory factory, IDataLogSink log)
    {
        _factory = factory;
        _log = log;
    }

    public int Execute(string sql, object? parameters = null, DbTransaction? tx = null)
    {
        using var conn = tx?.Connection ?? _factory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        var rows = conn.Execute(sql, parameters, tx);
        _log.Info("sql.exec", new { sql, rows });
        return rows;
    }

    public IEnumerable<T> Query<T>(string sql, object? parameters = null, DbTransaction? tx = null)
    {
        using var conn = tx?.Connection ?? _factory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        return conn.Query<T>(sql, parameters, tx);
    }

    public T? QuerySingle<T>(string sql, object? parameters = null, DbTransaction? tx = null)
    {
        using var conn = tx?.Connection ?? _factory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        return conn.QuerySingleOrDefault<T>(sql, parameters, tx);
    }
}
