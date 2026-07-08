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
        private const int FallbackPasswordIncrement = 10;
        private const int FallbackPasswordEntryCount = 10;
        private const bool FallbackDisplayCategoriesWithItems = true;
        private const int FallbackSensitiveClipboardClearSeconds = 45;
        private const int MinimumSensitiveClipboardClearSeconds = 5;
        private const int MaximumSensitiveClipboardClearSeconds = 300;
        private const int AbsolutePasswordMinimum = 8;
        private const int AbsolutePasswordIncrementMinimum = 1;
        private const int AbsolutePasswordEntryCountMinimum = 1;

        private static string LoadSqlRequired(string assetName)
        {
            var sql = SqlCagegory.GetSql(assetName);
            if (string.IsNullOrWhiteSpace(sql))
                throw new InvalidOperationException($"SQL not loaded: {assetName}");
            return sql;
        }

        public static int GetPasswordMinimum()
        {
            return GetPasswordLengthSettings().Minimum;
        }

        public static PasswordLengthSettings GetPasswordLengthSettings()
        {
            try
            {
                var sql = LoadSqlRequired(Sql_AppSettingsSelect);

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                    return PasswordLengthSettings.Fallback;

                int ordMinimum = reader.GetOrdinal("AS_PW_Minimum");
                int ordIncrement = reader.GetOrdinal("AS_PW_Incriments");
                int ordEntryCount = reader.GetOrdinal("AS_PW_Inctriment_Steps");

                int minimum = reader.IsDBNull(ordMinimum)
                    ? FallbackPasswordMinimum
                    : ReadInt32(reader, ordMinimum, FallbackPasswordMinimum);

                int increment = reader.IsDBNull(ordIncrement)
                    ? FallbackPasswordIncrement
                    : ReadInt32(reader, ordIncrement, FallbackPasswordIncrement);

                int entryCount = reader.IsDBNull(ordEntryCount)
                    ? FallbackPasswordEntryCount
                    : ReadInt32(reader, ordEntryCount, FallbackPasswordEntryCount);

                return new PasswordLengthSettings(
                    Math.Max(minimum, AbsolutePasswordMinimum),
                    Math.Max(increment, AbsolutePasswordIncrementMinimum),
                    Math.Max(entryCount, AbsolutePasswordEntryCountMinimum));
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error loading AppSettings password length settings");
                return PasswordLengthSettings.Fallback;
            }
        }

        public static bool GetDisplayCategoriesWithItems()
        {
            try
            {
                var sql = LoadSqlRequired(Sql_AppSettingsSelect);

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                    return FallbackDisplayCategoriesWithItems;

                int ordinal = reader.GetOrdinal("AS_DisplayCategoriesWithItems");
                if (reader.IsDBNull(ordinal))
                    return FallbackDisplayCategoriesWithItems;

                return ReadInt32(reader, ordinal, FallbackDisplayCategoriesWithItems ? 1 : 0) != 0;
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error loading AppSettings DisplayCategoriesWithItems setting");
                return FallbackDisplayCategoriesWithItems;
            }
        }

        public static int GetSensitiveClipboardClearSeconds()
        {
            try
            {
                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT SensitiveClipboardClearSeconds FROM AppSettings LIMIT 1;";

                var value = cmd.ExecuteScalar();
                if (value == null || value == DBNull.Value)
                    return FallbackSensitiveClipboardClearSeconds;

                return ClampSensitiveClipboardSeconds(Convert.ToInt32(value));
            }
            catch (Exception ex)
            {
                return FallbackSensitiveClipboardClearSeconds;
            }
        }

        private static int ClampSensitiveClipboardSeconds(int value)
        {
            if (value < MinimumSensitiveClipboardClearSeconds)
                return FallbackSensitiveClipboardClearSeconds;

            if (value > MaximumSensitiveClipboardClearSeconds)
                return FallbackSensitiveClipboardClearSeconds;

            return value;
        }

        private static int ReadInt32(SqliteDataReader reader, int ordinal, int fallback)
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
                catch { return fallback; }
            }
        }
    }

    public sealed record PasswordLengthSettings(int Minimum, int Increment, int EntryCount)
    {
        public static PasswordLengthSettings Fallback { get; } =
            new(FallbackMinimum, FallbackIncrement, FallbackEntryCount);

        private const int FallbackMinimum = 12;
        private const int FallbackIncrement = 10;
        private const int FallbackEntryCount = 10;

        public IReadOnlyList<int> BuildLengthOptions()
        {
            var values = new List<int>(EntryCount);
            for (int i = 0; i < EntryCount; i++)
                values.Add(Minimum + (Increment * i));

            return values;
        }
    }
}
