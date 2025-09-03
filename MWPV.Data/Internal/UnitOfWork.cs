using System.Data.Common;
using MWPV.Data.Abstractions;

namespace MWPV.Data.Internal;

public sealed class UnitOfWork : IDisposable
{
    private readonly IConnectionFactory _factory;

    public DbConnection Connection { get; }
    public DbTransaction Transaction { get; private set; } = default!;

    public UnitOfWork(IConnectionFactory factory)
    {
        _factory = factory;
        Connection = _factory.CreateConnection();
    }

    public void Begin()
    {
        if (Connection.State != System.Data.ConnectionState.Open)
            Connection.Open();

        Transaction = Connection.BeginTransaction();
    }

    public void Commit() => Transaction?.Commit();

    public void Rollback() => Transaction?.Rollback();

    public void Dispose()
    {
        Transaction?.Dispose();
        Connection.Dispose();
    }
}
