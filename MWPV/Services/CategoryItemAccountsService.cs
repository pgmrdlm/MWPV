// File: MWPV/Services/tmp_CategoryItemAccountsService.cs
using Microsoft.Data.Sqlite;
using MWPV.Models;
using Security.Utility.Crypto.Fields;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
        private const string Purpose_CIA_Number = "CIA.Number";
        private const string Sql_AccountsByItemId_ActiveOnly = "s_CategoryItemAccounts_select_by_itemid.sql";
        private const string Sql_AccountsByItemId_AllRows = "s_CategoryItemAccounts_select_all_by_itemid.sql";

        public sealed class AccountListRow
        {
            public long Id { get; init; }
            public long ItemId { get; init; }
            public int AccountTypeId { get; init; }
            public string AccountTypeDisplay { get; init; } = string.Empty;
            public string AccountNumberMasked { get; init; } = string.Empty;
            public bool IsActive { get; init; }
            public long CreatedAtUtcSeconds { get; init; }
            public long UpdatedAtUtcSeconds { get; init; }
        }

        private static string LoadSqlRequired(string assetName)
        {
            var sql = SqlCagegory.GetSql(assetName);
            if (string.IsNullOrWhiteSpace(sql))
                throw new InvalidOperationException($"SQL not loaded: {assetName}");
            return sql;
        }

        public static IReadOnlyList<AccountListRow> LoadAccountListRowsByItemId(long itemId)
        {
            var sourceRows = LoadCategoryItemAccountsByItemId(itemId);
            var list = new List<AccountListRow>(sourceRows.Count);

            var accountTypeDisplayById = new Dictionary<int, string>();
            try
            {
                foreach (var t in ComboDetailService.GetByTypeId(1))
                {
                    if (!accountTypeDisplayById.ContainsKey(t.ComboDet))
                    {
                        accountTypeDisplayById[t.ComboDet] =
                            string.IsNullOrWhiteSpace(t.Description) ? (t.Code ?? string.Empty) : t.Description;
                    }
                }
            }
            catch { }

            foreach (var row in sourceRows)
            {
                _ = TryDecryptUtf8(Purpose_CIA_Number, row.Number, out string? numberPlain);

                int accountTypeId = row.AccountTypeId ?? 0;
                string accountTypeDisplay =
                    accountTypeId > 0 && accountTypeDisplayById.TryGetValue(accountTypeId, out var display)
                    ? display
                    : string.Empty;

                list.Add(new AccountListRow
                {
                    Id = row.Id,
                    ItemId = row.ItemId,
                    AccountTypeId = accountTypeId,
                    AccountTypeDisplay = accountTypeDisplay,
                    AccountNumberMasked = MaskPanLast4(numberPlain),
                    IsActive = row.IsActive,
                    CreatedAtUtcSeconds = row.CreatedAtUtcSeconds,
                    UpdatedAtUtcSeconds = row.UpdatedAtUtcSeconds
                });
            }

            return list;
        }

        public static IReadOnlyList<CategoryItemAccountLean> LoadCategoryItemAccountsByItemId(long itemId)
        {
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0.");

            return LoadCategoryItemAccountsByItemIdCore(
                itemId: itemId,
                sqlAssetName: Sql_AccountsByItemId_ActiveOnly,
                errorContext: "Error loading CategoryItemAccounts by ItemId");
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

            if (!TryDecryptUtf8(Purpose_CIA_Number, numberCipher, out string? candidatePlain) ||
                string.IsNullOrWhiteSpace(candidatePlain))
            {
                throw new InvalidOperationException("Unable to validate account number uniqueness for this item.");
            }

            EnsureNoDuplicateAccountNumberForItem(
                itemId: itemId,
                candidateAccountNumberPlain: candidatePlain,
                excludeId: null);

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

        public static long InsertCategoryItemAccountFromUi(
            long itemId,
            int accountTypeId,
            string accountNumberRaw,
            bool isActive)
        {
            if (accountTypeId <= 0)
                throw new ArgumentOutOfRangeException(nameof(accountTypeId), "accountTypeId must be > 0.");

            byte[]? numberCipher = EncryptNullableUtf8(Purpose_CIA_Number, accountNumberRaw);
            if (numberCipher is null || numberCipher.Length == 0)
                throw new InvalidOperationException("Account number is required for CategoryItemAccounts insert.");

            try
            {
                return InsertCategoryItemAccount(
                    itemId: itemId,
                    label: null,
                    numberCipher: numberCipher,
                    accountTypeId: accountTypeId,
                    accountTypeFreeform: null,
                    isActive: isActive);
            }
            finally
            {
                if (numberCipher != null)
                    Array.Clear(numberCipher, 0, numberCipher.Length);
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

            if (!TryDecryptUtf8(Purpose_CIA_Number, numberCipher, out string? candidatePlain) ||
                string.IsNullOrWhiteSpace(candidatePlain))
            {
                throw new InvalidOperationException("Unable to validate account number uniqueness for this item.");
            }

            EnsureNoDuplicateAccountNumberForItem(
                itemId: itemId,
                candidateAccountNumberPlain: candidatePlain,
                excludeId: id);

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

        private static IReadOnlyList<CategoryItemAccountLean> LoadAllCategoryItemAccountsByItemIdForEnforcement(long itemId)
        {
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0.");

            return LoadCategoryItemAccountsByItemIdCore(
                itemId: itemId,
                sqlAssetName: Sql_AccountsByItemId_AllRows,
                errorContext: "Error loading CategoryItemAccounts by ItemId for enforcement");
        }

        private static IReadOnlyList<CategoryItemAccountLean> LoadCategoryItemAccountsByItemIdCore(
            long itemId,
            string sqlAssetName,
            string errorContext)
        {
            var rows = new List<CategoryItemAccountLean>();

            try
            {
                var sql = LoadSqlRequired(sqlAssetName);

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
                {
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
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, errorContext);
            }

            return rows;
        }

        private static string NormalizeAccountNumberDigitsOnly(string? accountNumber)
        {
            if (string.IsNullOrWhiteSpace(accountNumber))
                return string.Empty;

            return new string(accountNumber.Where(char.IsDigit).ToArray());
        }

        private static void EnsureNoDuplicateAccountNumberForItem(
            long itemId,
            string candidateAccountNumberPlain,
            long? excludeId)
        {
            string candidateNormalized = NormalizeAccountNumberDigitsOnly(candidateAccountNumberPlain);
            if (string.IsNullOrWhiteSpace(candidateNormalized))
                throw new InvalidOperationException("Unable to validate account number uniqueness for this item.");

            foreach (var row in LoadAllCategoryItemAccountsByItemIdForEnforcement(itemId))
            {
                if (excludeId.HasValue && row.Id == excludeId.Value)
                    continue;

                if (!TryDecryptUtf8(Purpose_CIA_Number, row.Number, out string? existingPlain) ||
                    string.IsNullOrWhiteSpace(existingPlain))
                {
                    throw new InvalidOperationException("Unable to validate account number uniqueness for this item.");
                }

                string existingNormalized = NormalizeAccountNumberDigitsOnly(existingPlain);
                if (string.IsNullOrWhiteSpace(existingNormalized))
                    throw new InvalidOperationException("Unable to validate account number uniqueness for this item.");

                if (string.Equals(candidateNormalized, existingNormalized, StringComparison.Ordinal))
                    throw new InvalidOperationException("Duplicate account number is not allowed for this item.");
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

        private static byte[]? EncryptNullableUtf8(string purpose, string? plain)
        {
            if (string.IsNullOrWhiteSpace(plain))
                return null;

            byte[] bytes = Encoding.UTF8.GetBytes(plain.Trim());

            try
            {
                return FieldAesCrypto.EncryptBytes(
                    masterKeySedsName: FieldAesCrypto.SedsKey_UserSecretsKey,
                    purpose: purpose,
                    plaintext: bytes);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        private static bool TryDecryptUtf8(string purpose, byte[]? cipherBlob, out string? plain)
        {
            plain = null;

            if (cipherBlob is null || cipherBlob.Length == 0)
                return true;

            try
            {
                if (!FieldAesCrypto.TryDecryptBytes(
                        masterKeySedsName: FieldAesCrypto.SedsKey_UserSecretsKey,
                        purpose: purpose,
                        blob: cipherBlob,
                        out var plainBytes))
                {
                    return false;
                }

                try
                {
                    plain = Encoding.UTF8.GetString(plainBytes);
                    return true;
                }
                finally
                {
                    Array.Clear(plainBytes, 0, plainBytes.Length);
                }
            }
            catch
            {
                return false;
            }
        }

        private static string MaskPanLast4(string? accountNumberPlain)
        {
            if (string.IsNullOrWhiteSpace(accountNumberPlain))
                return string.Empty;

            string digits = new string(accountNumberPlain.Where(char.IsDigit).ToArray());
            if (digits.Length == 0)
                return string.Empty;

            if (digits.Length <= 4)
                return $"**** {digits}";

            string last4 = digits.Substring(digits.Length - 4, 4);
            return $"**** {last4}";
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
