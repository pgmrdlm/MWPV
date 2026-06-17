using System;
using System.Linq;
using Microsoft.Data.Sqlite;
using MWPV.Services.MigrationUpgrade;
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

        public UpgradeStepResult<MigrationUpgradePlan> BuildPlan(
            string currentVersion,
            UpgradeSqlCatalog catalog,
            string? targetVersion = null)
        {
            try
            {
                if (catalog == null)
                {
                    return UpgradeStepResult<MigrationUpgradePlan>.Failure(
                        "BuildPlan",
                        UpgradeFailureCategory.Plan,
                        AppExitCode.UpgradePlanInvalid,
                        "Upgrade SQL catalog is required.");
                }

                var scripts = catalog.UpgradeScripts
                    .Select(script => new MigrationUpgradeScript
                    {
                        FileName = script.FileName
                    })
                    .ToArray();

                var plan = MigrationUpgradePlanner.BuildPlan(currentVersion, scripts, targetVersion);
                if (!plan.IsValid)
                {
                    return UpgradeStepResult<MigrationUpgradePlan>.Failure(
                        "BuildPlan",
                        UpgradeFailureCategory.Plan,
                        AppExitCode.UpgradePlanInvalid,
                        string.Join(Environment.NewLine, plan.Errors));
                }

                return UpgradeStepResult<MigrationUpgradePlan>.Success(
                    "BuildPlan",
                    plan,
                    plan.IsNoOp
                        ? $"No upgrade required for version {plan.CurrentDbVersion}."
                        : $"Planned {plan.RequiredScripts.Count} upgrade script(s) from {plan.CurrentDbVersion} to {plan.TargetDbVersion}.");
            }
            catch (Exception ex)
            {
                return UpgradeStepResult<MigrationUpgradePlan>.Failure(
                    "BuildPlan",
                    UpgradeFailureCategory.Plan,
                    AppExitCode.UpgradePlanInvalid,
                    "Upgrade plan creation failed.",
                    ex);
            }
        }

        public UpgradeStepResult ExecutePlan(
            string databasePath,
            string? password,
            MigrationUpgradePlan plan,
            UpgradeSqlCatalog catalog)
        {
            try
            {
                if (plan == null || catalog == null)
                {
                    return UpgradeStepResult.Failure(
                        "ExecuteSqlUpgrade",
                        UpgradeFailureCategory.SqlExecution,
                        AppExitCode.UpgradeSqlExecutionFailed,
                        "Upgrade plan and SQL catalog are required.");
                }

                if (plan.IsNoOp || plan.RequiredScripts.Count == 0)
                    return UpgradeStepResult.Success("ExecuteSqlUpgrade", "No SQL upgrade scripts required.");

                using var connection = DbUpgradeVersionReader.OpenConnection(databasePath, password);
                foreach (var plannedScript in plan.RequiredScripts)
                {
                    var script = catalog.UpgradeScripts.FirstOrDefault(
                        item => string.Equals(item.FileName, plannedScript.FileName, StringComparison.OrdinalIgnoreCase));

                    if (script == null || string.IsNullOrWhiteSpace(script.SqlText))
                    {
                        return UpgradeStepResult.Failure(
                            "ExecuteSqlUpgrade",
                            UpgradeFailureCategory.SqlExecution,
                            AppExitCode.UpgradeSqlExecutionFailed,
                            $"Planned SQL script was not loaded: {plannedScript.FileName}");
                    }

                    using var command = connection.CreateCommand();
                    command.CommandText = script.SqlText;
                    command.ExecuteNonQuery();
                }

                return UpgradeStepResult.Success(
                    "ExecuteSqlUpgrade",
                    $"Executed {plan.RequiredScripts.Count} SQL upgrade script(s).");
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

                if (!string.Equals(versionResult.Value, expectedVersion, StringComparison.OrdinalIgnoreCase))
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
    }
}
