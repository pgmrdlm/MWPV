using MWPV.Data.Abstractions;

namespace MWPV.Data.Internal;

/// <summary>
/// Simple unit-of-work wrapper providing a shared open connection and transaction scope
/// for a batch of operations. Optional; use when you need atomic multi-command work.
/// </summary>
internal sealed class UnitOfWork : IDisposable
{
    private readonly IConnectionFactory _factory;
    private readonly IDataLogSink _log;

    public Microsoft.Data.Sqlite.SqliteConnection Connection { get; }
    public Microsoft.Data.Sqlite.SqliteTransaction Transaction { get; }

    private bool _disposed;

    public UnitOfWork(IConnectionFactory factory, IDataLogSink log)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        Connection = _factory.CreateConnection();
        Transaction = Connection.BeginTransaction();
        _log.Info("UnitOfWork.Begin");
    }

    public void Commit()
    {
        if (_disposed) return;
        Transaction.Commit();
        _log.Info("UnitOfWork.Commit");
    }

    public void Rollback()
    {
        if (_disposed) return;
        Transaction.Rollback();
        _log.Warn("UnitOfWork.Rollback");
    }

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            Transaction.Dispose();
            Connection.Dispose();
            _log.Info("UnitOfWork.Dispose");
        }
        finally
        {
            _disposed = true;
        }
    }
}
