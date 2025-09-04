using MWPV.Data.Abstractions;
using MWPV.Data.Internal;
using MWPV.Data.Bootstrap;

namespace MWPV.Data.Bootstrap;

/// <summary>
/// Ensures the database schema is created or upgraded to the latest version.
/// Safe to call multiple times (idempotent).
/// </summary>
public sealed class SchemaBootstrapper
{
    private readonly ISqlExecutor _executor;
    private readonly IDataLogSink _log;

    /// <summary>
    /// Fixed script order for now; later you can drive this from SecureEncryptedDataStore.
    /// Names must match the keys your SqlCatalog resolver understands.
    /// </summary>
    private static readonly string[] ScriptOrder =
    {
        "MWPV_DB_Create.sql",
        "Logs_Init.sql",
        "Logs_Indexes.sql"
        // add additional scripts here in the required order
    };

    public SchemaBootstrapper(ISqlExecutor executor, IDataLogSink log)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Runs all schema scripts in order. Throws if any script fails.
    /// </summary>
    public void EnsureCreated()
    {
        foreach (var scriptName in ScriptOrder)
        {
            try
            {
                _log.Info("SchemaBootstrapper.StartScript", new { script = scriptName });

                var sql = SqlCatalog.Require(scriptName);
                var affected = _executor.Execute(sql);

                _log.Info("SchemaBootstrapper.Success", new { script = scriptName, affected });
            }
            catch (Exception ex)
            {
                _log.Error("SchemaBootstrapper.Failure", new { script = scriptName }, ex);
                throw; // Bubble up: schema must succeed for the app to run
            }
        }
    }
}
