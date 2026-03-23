// File: MWPV/Services/tmp_CategoryItemAccountsService.cs
using Microsoft.Data.Sqlite;
using MWPV.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using Utilities.Helpers;   // DatabaseHelper, ErrorHandler
using Utilities.Sql;       // SqlCagegory

namespace MWPV.Services
{
    /// <summary>
    /// Review-only Accounts persistence service that extends the current
    /// read-side CategoryItemAccounts service shape with write-side methods.
    /// </summary>
    public static class tmp_CategoryItemAccountsService
    {
        private static string LoadSqlRequired(string assetName)
        {
            var sql = SqlCagegory.GetSql(assetName);
            if (string.IsNullOrWhiteSpace(sql))
                throw new InvalidOperationException($"SQL not loaded: {assetName}");
            return sql;
        }

        public static IReadOnlyList<CategoryItemAccountLean> LoadCategoryItemAccountsByItemId(long itemId)
        {
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0.");

            var rows = new List<CategoryItemAccountLean>();

            try
            {
                var sql = LoadSqlRequired("s_CategoryItemAccounts_select_by_itemid.sql");

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                AddInt64(cmd, "@ItemId", itemId);

                using var r = cmd.ExecuteReader();

                int oId = r.GetOrdinal("Id");
                int oItemId = r.GetOrdinal("ItemId");
                int oLabel = r.GetOrdinal("Label");
                int oNumber = r.GetOrdinal("Number");
                int oAccountTypeId = r.GetOrdinal("AccountTypeId");
                int oAccountTypeFreeform = r.GetOrdinal("AccountTypeFreeform");
                int oIsActive = r.GetOrdinal("IsActive");
                int oCreated = r.GetOrdinal("CreatedAtUtcSeconds");
                int oUpdated = r.GetOrdinal("UpdatedAtUtcSeconds");

                while (r.Read())
                    rows.Add(MapLeanAccountRow(
                        r,
                        oId,
                        oItemId,
                        oLabel,
                        oNumber,
                        oAccountTypeId,
                        oAccountTypeFreeform,
                        oIsActive,
                        oCreated,
                        oUpdated));
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error loading CategoryItemAccounts by ItemId");
            }

            return rows;
        }

        public static CategoryItemAccountLean? LoadPrimaryCategoryItemAccountByItemId(long itemId)
        {
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0.");

            try
            {
                var sql = LoadSqlRequired("s_CategoryItemAccounts_select_primary_by_itemid.sql");

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                AddInt64(cmd, "@ItemId", itemId);

                using var r = cmd.ExecuteReader();
                if (!r.Read())
                    return null;

                int oId = r.GetOrdinal("Id");
                int oItemId = r.GetOrdinal("ItemId");
                int oLabel = r.GetOrdinal("Label");
                int oNumber = r.GetOrdinal("Number");
                int oAccountTypeId = r.GetOrdinal("AccountTypeId");
                int oAccountTypeFreeform = r.GetOrdinal("AccountTypeFreeform");
                int oIsActive = r.GetOrdinal("IsActive");
                int oCreated = r.GetOrdinal("CreatedAtUtcSeconds");
                int oUpdated = r.GetOrdinal("UpdatedAtUtcSeconds");

                return MapLeanAccountRow(
                    r,
                    oId,
                    oItemId,
                    oLabel,
                    oNumber,
                    oAccountTypeId,
                    oAccountTypeFreeform,
                    oIsActive,
                    oCreated,
                    oUpdated);
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error loading primary CategoryItemAccount by ItemId");
                return null;
            }
        }

        public static long InsertCategoryItemAccount(
            long itemId,
            string? label,
            byte[] numberCipher,
            int? accountTypeId,
            string? accountTypeFreeform,
            bool isActive)
        {
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0.");
            if (numberCipher is null || numberCipher.Length == 0)
                throw new InvalidOperationException("Account number is required for CategoryItemAccounts insert.");

            try
            {
                var sql = LoadSqlRequired("s_CategoryItemAccounts_insert.sql");

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                AddInt64(cmd, "@ItemId", itemId);
                AddText(cmd, "@Label", string.IsNullOrWhiteSpace(label) ? null : label);
                AddBlob(cmd, "@Number", numberCipher);
                AddInt32Nullable(cmd, "@AccountTypeId", accountTypeId);
                AddText(cmd, "@AccountTypeFreeform", string.IsNullOrWhiteSpace(accountTypeFreeform) ? null : accountTypeFreeform);
                AddInt32(cmd, "@IsActive", isActive ? 1 : 0);

                var scalar = cmd.ExecuteScalar();
                if (scalar == null || scalar == DBNull.Value)
                    throw new InvalidOperationException("CategoryItemAccount insert failed (no Id returned).");

                return Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error inserting CategoryItemAccount");
                return 0;
            }
        }

        public static int UpdateCategoryItemAccount(
            long id,
            long itemId,
            string? label,
            byte[] numberCipher,
            int? accountTypeId,
            string? accountTypeFreeform,
            bool isActive)
        {
            if (id <= 0)
                throw new ArgumentOutOfRangeException(nameof(id), "id must be > 0.");
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0.");
            if (numberCipher is null || numberCipher.Length == 0)
                throw new InvalidOperationException("Account number is required for CategoryItemAccounts update.");

            try
            {
                var sql = LoadSqlRequired("s_CategoryItemAccounts_update.sql");

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                AddInt64(cmd, "@Id", id);
                AddInt64(cmd, "@ItemId", itemId);
                AddText(cmd, "@Label", string.IsNullOrWhiteSpace(label) ? null : label);
                AddBlob(cmd, "@Number", numberCipher);
                AddInt32Nullable(cmd, "@AccountTypeId", accountTypeId);
                AddText(cmd, "@AccountTypeFreeform", string.IsNullOrWhiteSpace(accountTypeFreeform) ? null : accountTypeFreeform);
                AddInt32(cmd, "@IsActive", isActive ? 1 : 0);

                return cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error updating CategoryItemAccount");
                return 0;
            }
        }

        private static CategoryItemAccountLean MapLeanAccountRow(
            SqliteDataReader r,
            int oId,
            int oItemId,
            int oLabel,
            int oNumber,
            int oAccountTypeId,
            int oAccountTypeFreeform,
            int oIsActive,
            int oCreated,
            int oUpdated)
        {
            return new CategoryItemAccountLean
            {
                Id = SafeGetInt32(r, oId),
                ItemId = SafeGetInt32(r, oItemId),
                Label = r.IsDBNull(oLabel) ? null : r.GetString(oLabel),
                Number = ReadBlobNullable(r, oNumber) ?? Array.Empty<byte>(),
                AccountTypeId = r.IsDBNull(oAccountTypeId) ? null : SafeGetInt32(r, oAccountTypeId),
                AccountTypeFreeform = r.IsDBNull(oAccountTypeFreeform) ? null : r.GetString(oAccountTypeFreeform),
                IsActive = r.IsDBNull(oIsActive) || SafeGetInt32(r, oIsActive) == 1,
                CreatedAtUtcSeconds = r.IsDBNull(oCreated) ? 0 : SafeGetInt64(r, oCreated),
                UpdatedAtUtcSeconds = r.IsDBNull(oUpdated) ? 0 : SafeGetInt64(r, oUpdated)
            };
        }

        private static byte[]? ReadBlobNullable(SqliteDataReader r, int ordinal)
        {
            if (r.IsDBNull(ordinal)) return null;

            try
            {
                return r.GetFieldValue<byte[]>(ordinal);
            }
            catch
            {
                var v = r.GetValue(ordinal);
                return v as byte[];
            }
        }

        private static int SafeGetInt32(SqliteDataReader r, int ordinal)
        {
            var v = r.GetValue(ordinal);

            if (v is int i) return i;
            if (v is long l) return checked((int)l);
            if (v is short s) return s;
            if (v is byte b) return b;

            if (v is string str && int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            return Convert.ToInt32(v, CultureInfo.InvariantCulture);
        }

        private static long SafeGetInt64(SqliteDataReader r, int ordinal)
        {
            var v = r.GetValue(ordinal);

            if (v is long l) return l;
            if (v is int i) return i;

            if (v is string str && long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            return Convert.ToInt64(v, CultureInfo.InvariantCulture);
        }

        private static void AddText(SqliteCommand cmd, string name, string? value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.SqliteType = SqliteType.Text;
            p.Value = (object?)value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        private static void AddBlob(SqliteCommand cmd, string name, byte[]? value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.SqliteType = SqliteType.Blob;
            p.Value = (object?)value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        private static void AddInt32(SqliteCommand cmd, string name, int value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.SqliteType = SqliteType.Integer;
            p.Value = value;
            cmd.Parameters.Add(p);
        }

        private static void AddInt32Nullable(SqliteCommand cmd, string name, int? value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.SqliteType = SqliteType.Integer;
            p.Value = (object?)value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        private static void AddInt64(SqliteCommand cmd, string name, long value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.SqliteType = SqliteType.Integer;
            p.Value = value;
            cmd.Parameters.Add(p);
        }
    }
}
