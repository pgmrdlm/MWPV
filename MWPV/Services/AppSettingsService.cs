using Microsoft.Data.Sqlite;
using System;
using Utilities.Helpers;
using Utilities.Sql;

namespace MWPV.Services
{
    /// <summary>
    /// Runtime helpers for reading application settings.
    /// </summary>
    public static class AppSettingsService
    {
        private const string Sql_AppSettingsSelect = "s_AppSettings_select.sql";
        private const int FallbackPasswordMinimum = 12;
        private const int AbsolutePasswordMinimum = 8;

        private static string LoadSqlRequired(string assetName)
        {
            var sql = SqlCagegory.GetSql(assetName);
            if (string.IsNullOrWhiteSpace(sql))
                throw new InvalidOperationException($"SQL not loaded: {assetName}");
            return sql;
        }

        public static int GetPasswordMinimum()
        {
            try
            {
                var sql = LoadSqlRequired(Sql_AppSettingsSelect);

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                    return FallbackPasswordMinimum;

                int ordMinimum = reader.GetOrdinal("AS_PW_Minimum");
                if (reader.IsDBNull(ordMinimum))
                    return FallbackPasswordMinimum;

                int configured = ReadInt32(reader, ordMinimum);
                return Math.Max(configured, AbsolutePasswordMinimum);
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error loading AppSettings password minimum");
                return FallbackPasswordMinimum;
            }
        }

        private static int ReadInt32(SqliteDataReader reader, int ordinal)
        {
            try
            {
                return reader.GetFieldType(ordinal) == typeof(int)
                    ? reader.GetInt32(ordinal)
                    : Convert.ToInt32(reader.GetValue(ordinal));
            }
            catch
            {
                try { return Convert.ToInt32(reader.GetInt64(ordinal)); }
                catch { return FallbackPasswordMinimum; }
            }
        }
    }
}
