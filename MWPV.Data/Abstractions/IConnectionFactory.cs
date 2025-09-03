using System.Data.Common;

namespace MWPV.Data.Abstractions;

public interface IConnectionFactory
{
    DbConnection CreateConnection();
}
