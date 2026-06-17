using System;
using Microsoft.Data.Sqlite;
using MWPV.Services.AppLifecycle;

namespace MWPV.Services.Upgrade
{
    public sealed class DbUpgradeVersionReader
    {
        public UpgradeStepResult<string> ReadCurrentVersion(string databasePath, string? password = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(databasePath))
                {
                    return UpgradeStepResult<string>.Failure(
                        "ReadCurrentVersion",
                        UpgradeFailureCategory.VersionRead,
                        AppExitCode.UpgradeCurrentVersionReadFailed,
                        "Database path is required.");
                }

                using var connection = OpenConnection(databasePath, password);
                using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT Version
                    FROM DbVersion
                    WHERE IsCurrent = 1
                    ORDER BY Id DESC
                    LIMIT 1;
                    """;

                var value = command.ExecuteScalar();
                var version = value as string;
                if (string.IsNullOrWhiteSpace(version))
                {
                    return UpgradeStepResult<string>.Failure(
                        "ReadCurrentVersion",
                        UpgradeFailureCategory.VersionRead,
                        AppExitCode.UpgradeCurrentVersionReadFailed,
                        "Current database version was not found.");
                }

                return UpgradeStepResult<string>.Success(
                    "ReadCurrentVersion",
                    version,
                    $"Current database version is {version}.");
            }
            catch (Exception ex)
            {
                return UpgradeStepResult<string>.Failure(
                    "ReadCurrentVersion",
                    UpgradeFailureCategory.VersionRead,
                    AppExitCode.UpgradeCurrentVersionReadFailed,
                    "Current database version read failed.",
                    ex);
            }
        }

        public UpgradeStepResult ReadCurrentVersionPlaceholder() =>
            UpgradeStepResult.Failure(
                "ReadCurrentVersion",
                UpgradeFailureCategory.VersionRead,
                AppExitCode.UpgradeCurrentVersionReadFailed,
                "Database version reading is not implemented yet.");

        internal static SqliteConnection OpenConnection(string databasePath, string? password = null)
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWrite
            };

            if (!string.IsNullOrEmpty(password))
                builder.Password = password;

            var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            return connection;
        }
    }
}
