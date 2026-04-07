using Microsoft.Data.Sqlite;
using MWPV.Models;
using System;
using System.Collections.Generic;
using Utilities.Helpers;
using Utilities.Sql;

namespace MWPV.Services
{
    /// <summary>
    /// Runtime helpers for reading database version metadata.
    /// Review-only tmp copy: relies exclusively on the SQL catalog.
    /// </summary>
    public static class DbVersionService
    {
        private const string Sql_CurrentVersion = "s_DbVersion_select_current.sql";

        private static string LoadSqlRequired(string assetName)
        {
            var sql = SqlCagegory.GetSql(assetName);
            if (string.IsNullOrWhiteSpace(sql))
                throw new InvalidOperationException($"SQL not loaded: {assetName}");
            return sql;
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
    }
}
