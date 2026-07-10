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
        private const string Sql_AppSettingsUpdateEditable = "s_AppSettings_update_editable.sql";
        private const int FallbackPasswordMinimum = 12;
        private const int FallbackPasswordIncrement = 10;
        private const int FallbackPasswordEntryCount = 10;
        private const bool FallbackPasswordIncludeSymbols = true;
        private const bool FallbackDisplayCategoriesWithItems = true;
        private const int FallbackSensitiveClipboardClearSeconds = 45;
        private const int MinimumSensitiveClipboardClearSeconds = 15;
        private const int MaximumSensitiveClipboardClearSeconds = 180;
        private const int FallbackLogRetentionDays = 30;
        private const int FallbackBackupRetentionCount = 5;
        private const int MinimumEditablePasswordMinimum = 12;
        private const int MinimumLogRetentionDays = 30;
        private const int MinimumBackupRetentionCount = 5;
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
                var sql = LoadSqlRequired(Sql_AppSettingsSelect);

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                    return FallbackSensitiveClipboardClearSeconds;

                return ClampSensitiveClipboardSeconds(ReadOptionalInt32(
                    reader,
                    "SensitiveClipboardClearSeconds",
                    FallbackSensitiveClipboardClearSeconds));
            }
            catch (Exception ex)
            {
                return FallbackSensitiveClipboardClearSeconds;
            }
        }

        public static EditableAppSettings LoadEditableSettings()
        {
            try
            {
                var sql = LoadSqlRequired(Sql_AppSettingsSelect);

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                    return EditableAppSettings.Defaults();

                return new EditableAppSettings
                {
                    SavedItemPasswordMinimum = Math.Max(
                        ReadOptionalInt32(reader, "AS_PW_Minimum", FallbackPasswordMinimum),
                        MinimumEditablePasswordMinimum),
                    IncludeSymbols = ReadOptionalInt32(
                        reader,
                        "AS_PW_IncludeSymbols",
                        FallbackPasswordIncludeSymbols ? 1 : 0) != 0,
                    ClipboardClearSeconds = ClampSensitiveClipboardSeconds(ReadOptionalInt32(
                        reader,
                        "SensitiveClipboardClearSeconds",
                        FallbackSensitiveClipboardClearSeconds)),
                    LogRetentionDays = Math.Max(
                        ReadOptionalInt32(reader, "AS_LogRetentionDays", FallbackLogRetentionDays),
                        MinimumLogRetentionDays),
                    BackupRetentionCount = Math.Max(
                        ReadOptionalInt32(reader, "AS_BackupRetentionCount", FallbackBackupRetentionCount),
                        MinimumBackupRetentionCount)
                };
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error loading editable AppSettings");
                return EditableAppSettings.Defaults();
            }
        }

        public static bool TryValidateEditableSettings(EditableAppSettings? settings, out string message)
        {
            message = string.Empty;

            if (settings == null)
            {
                message = "App settings are unavailable.";
                return false;
            }

            var result = ValidateEditableSettings(settings);
            message = result.FirstError;
            return result.IsValid;
        }

        public static EditableAppSettingsValidationResult ValidateEditableSettings(
            EditableAppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            return ValidateEditableSettings(
                settings.SavedItemPasswordMinimum,
                settings.ClipboardClearSeconds,
                settings.LogRetentionDays,
                settings.BackupRetentionCount);
        }

        public static EditableAppSettingsValidationResult ValidateEditableSettings(
            int? savedItemPasswordMinimum,
            int? clipboardClearSeconds,
            int? logRetentionDays,
            int? backupRetentionCount)
        {
            return new EditableAppSettingsValidationResult
            {
                PasswordMinimumError = savedItemPasswordMinimum.HasValue &&
                                       savedItemPasswordMinimum.Value < MinimumEditablePasswordMinimum
                    ? "Saved-item password minimum must be at least 12."
                    : string.Empty,
                ClipboardClearSecondsError = clipboardClearSeconds.HasValue &&
                                             (clipboardClearSeconds.Value < MinimumSensitiveClipboardClearSeconds ||
                                              clipboardClearSeconds.Value > MaximumSensitiveClipboardClearSeconds)
                    ? "Clipboard auto-clear must be between 15 and 180 seconds."
                    : string.Empty,
                LogRetentionDaysError = logRetentionDays.HasValue &&
                                        logRetentionDays.Value < MinimumLogRetentionDays
                    ? "Log retention must be at least 30 days."
                    : string.Empty,
                BackupRetentionCountError = backupRetentionCount.HasValue &&
                                            backupRetentionCount.Value < MinimumBackupRetentionCount
                    ? "Backup retention must be at least 5 backup sets."
                    : string.Empty
            };
        }

        public static void SaveEditableSettings(EditableAppSettings settings)
        {
            if (!TryValidateEditableSettings(settings, out var message))
                throw new ArgumentException(message, nameof(settings));

            var sql = LoadSqlRequired(Sql_AppSettingsUpdateEditable);

            using var conn = DatabaseHelper.GetAppOpenConnection();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@AS_PW_Minimum", settings.SavedItemPasswordMinimum);
            cmd.Parameters.AddWithValue("@AS_PW_IncludeSymbols", settings.IncludeSymbols ? 1 : 0);
            cmd.Parameters.AddWithValue("@SensitiveClipboardClearSeconds", settings.ClipboardClearSeconds);
            cmd.Parameters.AddWithValue("@AS_LogRetentionDays", settings.LogRetentionDays);
            cmd.Parameters.AddWithValue("@AS_BackupRetentionCount", settings.BackupRetentionCount);
            cmd.ExecuteNonQuery();
            tx.Commit();
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

        private static int ReadOptionalInt32(SqliteDataReader reader, string columnName, int fallback)
        {
            try
            {
                int ordinal = reader.GetOrdinal(columnName);
                if (reader.IsDBNull(ordinal))
                    return fallback;

                return ReadInt32(reader, ordinal, fallback);
            }
            catch
            {
                return fallback;
            }
        }
    }

    public sealed class EditableAppSettings
    {
        public int SavedItemPasswordMinimum { get; set; }
        public bool IncludeSymbols { get; set; }
        public int ClipboardClearSeconds { get; set; }
        public int LogRetentionDays { get; set; }
        public int BackupRetentionCount { get; set; }

        public static EditableAppSettings Defaults() => new()
        {
            SavedItemPasswordMinimum = 12,
            IncludeSymbols = true,
            ClipboardClearSeconds = 45,
            LogRetentionDays = 30,
            BackupRetentionCount = 5
        };
    }

    public sealed class EditableAppSettingsValidationResult
    {
        public string PasswordMinimumError { get; init; } = string.Empty;
        public string ClipboardClearSecondsError { get; init; } = string.Empty;
        public string LogRetentionDaysError { get; init; } = string.Empty;
        public string BackupRetentionCountError { get; init; } = string.Empty;

        public bool IsValid => string.IsNullOrEmpty(PasswordMinimumError) &&
                               string.IsNullOrEmpty(ClipboardClearSecondsError) &&
                               string.IsNullOrEmpty(LogRetentionDaysError) &&
                               string.IsNullOrEmpty(BackupRetentionCountError);

        public string FirstError =>
            !string.IsNullOrEmpty(PasswordMinimumError) ? PasswordMinimumError :
            !string.IsNullOrEmpty(ClipboardClearSecondsError) ? ClipboardClearSecondsError :
            !string.IsNullOrEmpty(LogRetentionDaysError) ? LogRetentionDaysError :
            BackupRetentionCountError;
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
