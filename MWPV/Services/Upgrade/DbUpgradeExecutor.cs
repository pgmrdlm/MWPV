using System;
using System.Linq;
using Microsoft.Data.Sqlite;
using MWPV.SqlCatalog;
using MWPV.Services.AppLifecycle;

namespace MWPV.Services.Upgrade
{
    public sealed class DbUpgradeExecutor
    {
        private readonly DbUpgradeVersionReader _versionReader;

        public DbUpgradeExecutor()
            : this(new DbUpgradeVersionReader())
        {
        }

        public DbUpgradeExecutor(DbUpgradeVersionReader versionReader)
        {
            _versionReader = versionReader ?? throw new ArgumentNullException(nameof(versionReader));
        }

        public UpgradeStepResult ExecutePlan(
            string databasePath,
            string? password,
            VerifiedUpgradePackage package)
        {
            try
            {
                if (package == null)
                {
                    return UpgradeStepResult.Failure(
                        "ExecuteSqlUpgrade",
                        UpgradeFailureCategory.SqlExecution,
                        AppExitCode.UpgradeSqlExecutionFailed,
                        "Upgrade plan and SQL catalog are required.");
                }

                if (package.OrderedUpgradeScripts.Count == 0)
                    return UpgradeStepResult.Success("ExecuteSqlUpgrade", "No SQL upgrade scripts required.");

                using var connection = DbUpgradeVersionReader.OpenConnection(databasePath, password);
                foreach (var script in package.OrderedUpgradeScripts)
                {
                    if (string.IsNullOrWhiteSpace(script.SqlText))
                    {
                        return UpgradeStepResult.Failure(
                            "ExecuteSqlUpgrade",
                            UpgradeFailureCategory.SqlExecution,
                            AppExitCode.UpgradeSqlExecutionFailed,
                            $"Verified SQL script was empty: {script.CatalogEntry.FileName}");
                    }

                    using var command = connection.CreateCommand();
                    command.CommandText = script.SqlText;
                    command.ExecuteNonQuery();
                }

                return UpgradeStepResult.Success(
                    "ExecuteSqlUpgrade",
                    $"Executed {package.OrderedUpgradeScripts.Count} SQL upgrade script(s).");
            }
            catch (Exception ex)
            {
                return UpgradeStepResult.Failure(
                    "ExecuteSqlUpgrade",
                    UpgradeFailureCategory.SqlExecution,
                    AppExitCode.UpgradeSqlExecutionFailed,
                    "SQL upgrade execution failed.",
                    ex);
            }
        }

        public UpgradeStepResult ValidateDatabase(
            string databasePath,
            string? password,
            string expectedVersion,
            bool runIntegrityCheck = true)
        {
            try
            {
                var versionResult = _versionReader.ReadCurrentVersion(databasePath, password);
                if (!versionResult.Succeeded)
                {
                    return UpgradeStepResult.Failure(
                        "ValidateDatabase",
                        UpgradeFailureCategory.DbValidation,
                        AppExitCode.UpgradeDbValidationFailed,
                        versionResult.Message,
                        versionResult.Exception);
                }

                if (!VersionsMatch(expectedVersion, versionResult.Value))
                {
                    return UpgradeStepResult.Failure(
                        "ValidateDatabase",
                        UpgradeFailureCategory.DbValidation,
                        AppExitCode.UpgradeDbValidationFailed,
                        $"Database version validation failed. Expected {expectedVersion}, found {versionResult.Value}.");
                }

                using var connection = DbUpgradeVersionReader.OpenConnection(databasePath, password);
                if (!RequiredTableExists(connection, "DbVersion"))
                {
                    return UpgradeStepResult.Failure(
                        "ValidateDatabase",
                        UpgradeFailureCategory.DbValidation,
                        AppExitCode.UpgradeDbValidationFailed,
                        "DbVersion table is missing.");
                }

                if (runIntegrityCheck)
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = "PRAGMA integrity_check;";
                    var value = command.ExecuteScalar() as string;
                    if (!string.Equals(value, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        return UpgradeStepResult.Failure(
                            "ValidateDatabase",
                            UpgradeFailureCategory.DbValidation,
                            AppExitCode.UpgradeDbValidationFailed,
                            $"PRAGMA integrity_check failed: {value}");
                    }
                }

                return UpgradeStepResult.Success(
                    "ValidateDatabase",
                    $"Database validated at version {expectedVersion}.");
            }
            catch (Exception ex)
            {
                return UpgradeStepResult.Failure(
                    "ValidateDatabase",
                    UpgradeFailureCategory.DbValidation,
                    AppExitCode.UpgradeDbValidationFailed,
                    "Database validation failed.",
                    ex);
            }
        }

        public UpgradeStepResult BuildPlanPlaceholder() =>
            UpgradeStepResult.Failure(
                "BuildPlan",
                UpgradeFailureCategory.Plan,
                AppExitCode.UpgradePlanInvalid,
                "Database upgrade planning is not implemented yet.");

        public UpgradeStepResult ExecutePlaceholder() =>
            UpgradeStepResult.Failure(
                "ExecuteSqlUpgrade",
                UpgradeFailureCategory.SqlExecution,
                AppExitCode.UpgradeSqlExecutionFailed,
                "SQL upgrade execution is not implemented yet.");

        private static bool RequiredTableExists(SqliteConnection connection, string tableName)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table'
                  AND name = $name
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$name", tableName);
            return command.ExecuteScalar() != null;
        }

        internal static bool VersionsMatch(string? expectedVersion, string? storedVersion)
        {
            return SqlVersion.TryParse(expectedVersion, out var expected) &&
                   SqlVersion.TryParse(storedVersion, out var stored) &&
                   expected.CompareTo(stored) == 0;
        }
    }
}
