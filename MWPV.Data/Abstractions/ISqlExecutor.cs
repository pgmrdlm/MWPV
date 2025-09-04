namespace MWPV.Data.Abstractions;

/// <summary>
/// Executes raw SQL batches against the database.
/// Intended for bootstrap/migration scenarios and simple commands.
/// </summary>
public interface ISqlExecutor
{
    /// <summary>
    /// Executes a SQL batch and returns the number of affected rows
    /// as reported by the underlying provider.
    /// </summary>
    int Execute(string sql);
}
