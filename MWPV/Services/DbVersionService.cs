using Microsoft.Data.Sqlite;
using MWPV.Models;
using System;
using System.Collections.Generic;
using System.IO;
using Utilities.Helpers;
using Utilities.Sql;

namespace MWPV.Services
{
    /// <summary>
    /// Runtime helpers for reading database version metadata.
    /// </summary>
    public static class DbVersionService
    {
        private const string Sql_CurrentVersion = "s_DbVersion_select_current.sql";
        private const string Sql_AllVersions = @"
SELECT
    Id        AS Id,
    Version   AS Version,
    IsCurrent AS IsCurrent,
    AppliedOn AS CreatedAt
FROM DbVersion
ORDER BY Id DESC;";

        private static string LoadSqlRequired(string assetName)
        {
            try
            {
                var sql = SqlCagegory.GetSql(assetName);
                if (!string.IsNullOrWhiteSpace(sql))
                    return sql;
            }
            catch
            {
                // Fall through to on-disk lookup so this new service can be
                // added without immediately expanding the SQL catalog.
            }

            var sqlPath = FindSqlFile(assetName);
            if (!string.IsNullOrWhiteSpace(sqlPath) && File.Exists(sqlPath))
            {
                var sql = File.ReadAllText(sqlPath);
                if (!string.IsNullOrWhiteSpace(sql))
                    return sql;
            }

            throw new InvalidOperationException($"SQL not loaded: {assetName}");
        }

        public static DbVersion? GetCurrentVersion()
        {
            try
            {
                var sql = LoadSqlRequired(Sql_CurrentVersion);

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                    return null;

                return MapRow(reader);
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error loading current DbVersion");
                return null;
            }
        }

        public static IReadOnlyList<DbVersion> GetAllVersions()
        {
            var rows = new List<DbVersion>();

            try
            {
                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = Sql_AllVersions;

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    rows.Add(MapRow(reader));
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error loading DbVersion history");
            }

            return rows;
        }

        private static DbVersion MapRow(SqliteDataReader reader)
        {
            int ordId = reader.GetOrdinal("Id");
            int ordVersion = reader.GetOrdinal("Version");
            int ordIsCurrent = reader.GetOrdinal("IsCurrent");
            int ordCreatedAt = reader.GetOrdinal("CreatedAt");

            return new DbVersion
            {
                Id = reader.IsDBNull(ordId) ? 0 : reader.GetInt32(ordId),
                Version = reader.IsDBNull(ordVersion) ? string.Empty : reader.GetString(ordVersion),
                IsCurrent = !reader.IsDBNull(ordIsCurrent) && reader.GetInt32(ordIsCurrent) != 0,
                CreatedAt = reader.IsDBNull(ordCreatedAt) ? string.Empty : reader.GetString(ordCreatedAt)
            };
        }

        private static string? FindSqlFile(string assetName)
        {
            foreach (var start in EnumerateSearchRoots())
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    var candidate = Path.Combine(dir.FullName, "sql", assetName);
                    if (File.Exists(candidate))
                        return candidate;

                    dir = dir.Parent;
                }
            }

            return null;
        }

        private static IEnumerable<string> EnumerateSearchRoots()
        {
            if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory))
                yield return AppContext.BaseDirectory;

            if (!string.IsNullOrWhiteSpace(Environment.CurrentDirectory) &&
                !string.Equals(Environment.CurrentDirectory, AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
            {
                yield return Environment.CurrentDirectory;
            }
        }
    }
}
