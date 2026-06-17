using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MWPV.Services.AppLifecycle;

namespace MWPV.Services.Upgrade
{
    public sealed record UpgradeSqlScript
    {
        public string FileName { get; init; } = string.Empty;
        public string FullPath { get; init; } = string.Empty;
        public string SqlText { get; init; } = string.Empty;
    }

    public sealed record UpgradeSqlCatalogOptions
    {
        public string? SqlDirectory { get; init; }
        public IReadOnlyList<string> RequiredNormalSqlFiles { get; init; } = Array.Empty<string>();
        public bool RequireAtLeastOneUpgradeScript { get; init; } = true;
        public bool LoadAllNormalSqlFiles { get; init; } = true;
    }

    public sealed class UpgradeSqlCatalog
    {
        private static readonly Regex UpgradeScriptRegex = new(
            @"^v?(?<from>\d+(?:\.\d+)*)(?:_v|_to_v?)(?<to>\d+(?:\.\d+)*)_Upgrade\.sql$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public string SqlDirectory { get; init; } = string.Empty;
        public IReadOnlyList<UpgradeSqlScript> UpgradeScripts { get; init; } = Array.Empty<UpgradeSqlScript>();
        public IReadOnlyDictionary<string, string> NormalSqlScripts { get; init; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static UpgradeSqlCatalog Empty { get; } = new();

        public IReadOnlyDictionary<string, string> GetSqlMapForKeyFileRebuild() => NormalSqlScripts;

        public static OperationResult<UpgradeSqlCatalog> LoadInstalled(UpgradeSqlCatalogOptions? options = null)
        {
            options ??= new UpgradeSqlCatalogOptions();
            var sqlDirectory = ResolveSqlDirectory(options.SqlDirectory);

            try
            {
                if (!Directory.Exists(sqlDirectory))
                {
                    return OperationResult<UpgradeSqlCatalog>.Failure(
                        AppExitCode.UpgradeSqlCatalogMissing,
                        $"Installed SQL directory was not found: {sqlDirectory}");
                }

                var sqlFiles = Directory
                    .EnumerateFiles(sqlDirectory, "*.sql", SearchOption.TopDirectoryOnly)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (sqlFiles.Length == 0)
                {
                    return OperationResult<UpgradeSqlCatalog>.Failure(
                        AppExitCode.UpgradeSqlCatalogMissing,
                        $"No SQL files were found in installed SQL directory: {sqlDirectory}");
                }

                var upgradeScripts = new List<UpgradeSqlScript>();
                var normalScripts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var path in sqlFiles)
                {
                    var fileName = Path.GetFileName(path);
                    var sqlText = File.ReadAllText(path);

                    if (IsUpgradeScriptFileName(fileName))
                    {
                        upgradeScripts.Add(new UpgradeSqlScript
                        {
                            FileName = fileName,
                            FullPath = path,
                            SqlText = sqlText
                        });
                    }
                    else if (options.LoadAllNormalSqlFiles || options.RequiredNormalSqlFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                    {
                        normalScripts[fileName] = sqlText;
                    }
                }

                if (options.RequireAtLeastOneUpgradeScript && upgradeScripts.Count == 0)
                {
                    return OperationResult<UpgradeSqlCatalog>.Failure(
                        AppExitCode.UpgradeSqlCatalogMissing,
                        $"No upgrade SQL scripts were found in installed SQL directory: {sqlDirectory}");
                }

                foreach (var required in options.RequiredNormalSqlFiles)
                {
                    if (!normalScripts.TryGetValue(required, out var sql) || string.IsNullOrWhiteSpace(sql))
                    {
                        return OperationResult<UpgradeSqlCatalog>.Failure(
                            AppExitCode.UpgradeSqlCatalogMissing,
                            $"Required SQL file is missing or empty: {required}");
                    }
                }

                var catalog = new UpgradeSqlCatalog
                {
                    SqlDirectory = sqlDirectory,
                    UpgradeScripts = upgradeScripts,
                    NormalSqlScripts = normalScripts
                };

                return OperationResult<UpgradeSqlCatalog>.Success(
                    catalog,
                    $"Loaded {upgradeScripts.Count} upgrade SQL script(s) and {normalScripts.Count} normal SQL script(s).");
            }
            catch (Exception ex)
            {
                return OperationResult<UpgradeSqlCatalog>.Failure(
                    AppExitCode.UpgradeSqlCatalogMissing,
                    "Installed SQL catalog load failed.",
                    ex);
            }
        }

        public static bool IsUpgradeScriptFileName(string fileName) =>
            !string.IsNullOrWhiteSpace(fileName) && UpgradeScriptRegex.IsMatch(fileName);

        private static string ResolveSqlDirectory(string? explicitSqlDirectory)
        {
            if (!string.IsNullOrWhiteSpace(explicitSqlDirectory))
                return Path.GetFullPath(explicitSqlDirectory);

            return Path.Combine(AppContext.BaseDirectory, "sql");
        }
    }
}
