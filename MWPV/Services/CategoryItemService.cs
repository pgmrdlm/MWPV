// File: Services/CategoryItemService.cs
//
// FULL REWRITE (AES-only, portable, keyfile-derived)
//
// What changed (by design):
// - NO DPAPI anywhere in this service.
// - CategoryItem sensitive columns (Email/Phone/PIN) use FieldAesCrypto (AES-GCM) with UserSecretsKey from SEDS.
// - PasswordHistory uses the SAME FieldAesCrypto (AES-GCM) with UserSecretsKey from SEDS.
// - Optional PwSig is SHA-256 over the encrypted BLOB (cipher blob) for drift/tamper diagnostics.
// - No “crypto hooks”. Service is the single source of truth for encrypt/decrypt.
//
// Requirements:
// - SEDS must contain FieldAesCrypto.SedsKey_UserSecretsKey as 32 bytes before any decrypt/encrypt.
// - SQL param names MUST match SQL assets exactly.
//
// Hard rule: parameter names MUST match SQL exactly.
// - s_CategoryItem_select_by_id.sql uses: @ItemId
// - s_CategoryItem_update.sql uses: @ItemId plus all @CI_* params (including @CI_Pin)
// - s_CategoryItemPasswordHistory_select_by_item_most_recent_first.sql uses: @CIPaH_ItemId
// - s_CategoryItemPasswordHistory_insert.sql uses: @ItemId, @Version, @PasswordBlob, @PadLen, @PwSig, @SigVersion

using Microsoft.Data.Sqlite;
using MWPV.Models;
using MWPV.Utilities.Json;               // AppJson
using Security.Utility.Crypto.Fields;    // FieldAesCrypto (AES-GCM portable)
using Security.Utility.Crypto.Hash;      // Sha256Common
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Utilities.Helpers;                 // DatabaseHelper, ErrorHandler
using Utilities.Sql;                     // SqlCagegory (SQL catalog/loader)

namespace MWPV.Services
{
    public static class CategoryItemService
    {
        // ============================================================
        // Purposes (AAD / KDF info) - must be stable forever
        // ============================================================
        private const string Purpose_CI_Email = "CI.Email";
        private const string Purpose_CI_Phone = "CI.Phone";
        private const string Purpose_CI_Pin = "CI.Pin";

        private const string Purpose_CIPaH_PasswordHistory = "CIPaH.PasswordHistory";

        // ============================================================
        // DTO: Basic tab row (CategoryItem only)
        // ============================================================

        public sealed class CategoryItemBasicRow
        {
            public long ItemId { get; init; }
            public int CategoryKey { get; init; }

            public string Name { get; init; } = string.Empty;
            public string? Description { get; init; }
            public string? Username { get; init; }
            public string? SignInUrl { get; init; }

            public int BookMarkOnly { get; init; }     // 0/1
            public int? IsActive { get; init; }        // null allowed

            // Encrypted-at-rest blobs
            public byte[]? AccountEmailCipher { get; init; } // CI_AccountEmail
            public byte[]? AccountPhoneCipher { get; init; } // CI_AccountPhoneNumber
            public byte[]? PinCipher { get; init; }          // CI_Pin

            // Decrypted (AES-GCM via FieldAesCrypto)
            public string? AccountEmailPlain { get; init; }
            public string? AccountPhonePlain { get; init; }
            public string? PinPlain { get; init; }

            public DateTime? CreatedUtc { get; init; } // CI_CreateUTC
            public DateTime? UpdatedUtc { get; init; } // CI_UpdateUTC
        }

        // ============================================================
        // DTO: Most-recent password history row (AES-GCM)
        // ============================================================

        public sealed class PasswordHistoryMostRecent
        {
            public long PwHistId { get; init; }
            public long ItemId { get; init; }
            public DateTime? CreatedAtUtc { get; init; }
            public int? Version { get; init; }

            public byte[] PasswordCipher { get; init; } = Array.Empty<byte>();
            public int? PadLen { get; init; }
            public byte[]? PwSig { get; init; }
            public int? SigVersion { get; init; }

            public bool DecryptOk { get; init; }
            public string? PasswordPlain { get; init; }
            public bool SigOk { get; init; }
        }

        // ============================================================
        // SQL loading
        // ============================================================

        private static string LoadSqlRequired(string assetName)
        {
            var sql = SqlCagegory.GetSql(assetName);
            if (string.IsNullOrWhiteSpace(sql))
                throw new InvalidOperationException($"SQL not loaded: {assetName}");
            return sql;
        }

        // ============================================================
        // AES helpers (portable, multi-machine)
        // ============================================================

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
                return false;

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

        /// <summary>
        /// Builds PasswordHistory cipher + sig using AES-GCM (portable).
        /// - Bookmark-only => returns EMPTY cipher.
        /// - pwSig is SHA-256 over cipher bytes (EMPTY is allowed).
        /// </summary>
        public static void BuildPasswordHistoryPayloadAes(
            string? password,
            bool isBookmarkOnly,
            out byte[] pwCipher,
            out int? padLen,
            out byte[] pwSig,
            out int? sigVersion)
        {
            padLen = null;
            sigVersion = 1;

            if (isBookmarkOnly)
            {
                pwCipher = Array.Empty<byte>();
                pwSig = Sha256Common.Bytes(pwCipher);
                return;
            }

            var pw = password ?? string.Empty;

            byte[] pwBytes = Encoding.UTF8.GetBytes(pw);
            try
            {
                pwCipher = FieldAesCrypto.EncryptBytes(
                    masterKeySedsName: FieldAesCrypto.SedsKey_UserSecretsKey,
                    purpose: Purpose_CIPaH_PasswordHistory,
                    plaintext: pwBytes);

                pwSig = Sha256Common.Bytes(pwCipher);
            }
            finally
            {
                Array.Clear(pwBytes, 0, pwBytes.Length);
            }
        }

        // ============================================================
        // UPDATE: Basic tab (CategoryItem update by ItemId)
        // ============================================================

        public static int UpdateCategoryItemBasic(
            long itemId,
            string name,
            string? description,
            string? username,
            string? signInUrl,
            int? bookMarkOnly,
            string? accountEmailPlain,
            string? accountPhonePlain,
            string? pinPlain,
            int? isActive)
        {
            return UpdateCategoryItemBasicCore(
                itemId: itemId,
                name: name,
                description: description,
                username: username,
                signInUrl: signInUrl,
                bookMarkOnly: bookMarkOnly,
                accountEmailCipher: EncryptNullableUtf8(Purpose_CI_Email, accountEmailPlain),
                accountPhoneCipher: EncryptNullableUtf8(Purpose_CI_Phone, accountPhonePlain),
                pinCipher: EncryptNullableUtf8(Purpose_CI_Pin, pinPlain),
                isActive: isActive);
        }

        private static int UpdateCategoryItemBasicCore(
            long itemId,
            string name,
            string? description,
            string? username,
            string? signInUrl,
            int? bookMarkOnly,
            byte[]? accountEmailCipher,
            byte[]? accountPhoneCipher,
            byte[]? pinCipher,
            int? isActive)
        {
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0.");
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required.", nameof(name));
            if (bookMarkOnly.HasValue)
                _ = NormalizeBookMarkOnly(bookMarkOnly.Value);

            try
            {
                var sql = LoadSqlRequired("s_CategoryItem_update.sql");

#if DEBUG
                Debug.WriteLine("[SQL][TEXT] >>> s_CategoryItem_update.sql");
                Debug.WriteLine(sql);
                Debug.WriteLine("[SQL][TEXT] <<<");
#endif

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                AddInt64(cmd, "@ItemId", itemId);
                AddText(cmd, "@CI_Name", name.Trim());

                AddTextIfSqlUses(cmd, sql, "@CI_Description", string.IsNullOrWhiteSpace(description) ? null : description);
                AddTextIfSqlUses(cmd, sql, "@CI_Username", string.IsNullOrWhiteSpace(username) ? null : username);
                AddTextIfSqlUses(cmd, sql, "@CI_SignInUrl", string.IsNullOrWhiteSpace(signInUrl) ? null : signInUrl);

                AddInt32NullableIfSqlUses(cmd, sql, "@CI_BookMarkOnly", bookMarkOnly);
                AddInt32NullableIfSqlUses(cmd, sql, "@IsActive", isActive);

                AddBlobIfSqlUses(cmd, sql, "@CI_AccountEmail", accountEmailCipher);
                AddBlobIfSqlUses(cmd, sql, "@CI_AccountPhoneNumber", accountPhoneCipher);
                AddBlobIfSqlUses(cmd, sql, "@CI_Pin", pinCipher);

#if DEBUG
                DebugDumpParams(cmd, "[BASIC][DB][ITEM_UPDATE][PARAMS]");
#endif

                var scalar = cmd.ExecuteScalar();
                if (scalar == null || scalar == DBNull.Value)
                    throw new InvalidOperationException("Update failed (no RowsAffected returned)");

                int rows = Convert.ToInt32(scalar, CultureInfo.InvariantCulture);

#if DEBUG
                Debug.WriteLine($"[BASIC][DB][ITEM_UPDATE] itemId={itemId} rowsAffected={rows}");
#endif

                return rows;
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error updating CategoryItem (Basic tab) by ItemId");
                return 0;
            }
        }

        // ============================================================
        // SELECT: Basic tab (CategoryItem by ItemId)
        // ============================================================

        public static CategoryItemBasicRow? LoadCategoryItemBasicById(long itemId)
        {
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0.");

            try
            {
                var sql = LoadSqlRequired("s_CategoryItem_select_by_id.sql");

#if DEBUG
                Debug.WriteLine("[SQL][TEXT] >>> s_CategoryItem_select_by_id.sql");
                Debug.WriteLine(sql);
                Debug.WriteLine("[SQL][TEXT] <<<");
#endif

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                AddInt64(cmd, "@ItemId", itemId);

#if DEBUG
                DebugDumpParams(cmd, "[BASIC][DB][ITEM_SELECT][PARAMS]");
#endif

                using var r = cmd.ExecuteReader();
                if (!r.Read())
                    return null;

                int oItemId = r.GetOrdinal("ItemId");
                int oCatKey = r.GetOrdinal("Category_Key");
                int oName = r.GetOrdinal("CI_Name");
                int oDesc = r.GetOrdinal("CI_Description");
                int oUser = r.GetOrdinal("CI_Username");
                int oUrl = r.GetOrdinal("CI_SignInUrl");
                int oBmo = r.GetOrdinal("CI_BookMarkOnly");
                int oEmail = r.GetOrdinal("CI_AccountEmail");
                int oPhone = r.GetOrdinal("CI_AccountPhoneNumber");
                int oPin = r.GetOrdinal("CI_Pin");
                int oCreate = r.GetOrdinal("CI_CreateUTC");
                int oUpdate = r.GetOrdinal("CI_UpdateUTC");
                int oActive = r.GetOrdinal("IsActive");

                byte[]? emailCipher = ReadBlobNullable(r, oEmail);
                byte[]? phoneCipher = ReadBlobNullable(r, oPhone);
                byte[]? pinCipher = ReadBlobNullable(r, oPin);

                string? emailPlain = null;
                string? phonePlain = null;
                string? pinPlain = null;

                _ = TryDecryptUtf8(Purpose_CI_Email, emailCipher, out emailPlain);
                _ = TryDecryptUtf8(Purpose_CI_Phone, phoneCipher, out phonePlain);
                _ = TryDecryptUtf8(Purpose_CI_Pin, pinCipher, out pinPlain);

                return new CategoryItemBasicRow
                {
                    ItemId = SafeGetInt64(r, oItemId),
                    CategoryKey = SafeGetInt32(r, oCatKey),

                    Name = r.IsDBNull(oName) ? string.Empty : r.GetString(oName),
                    Description = r.IsDBNull(oDesc) ? null : r.GetString(oDesc),
                    Username = r.IsDBNull(oUser) ? null : r.GetString(oUser),
                    SignInUrl = r.IsDBNull(oUrl) ? null : r.GetString(oUrl),

                    BookMarkOnly = r.IsDBNull(oBmo) ? 0 : SafeGetInt32(r, oBmo),
                    IsActive = r.IsDBNull(oActive) ? null : SafeGetInt32(r, oActive),

                    AccountEmailCipher = emailCipher,
                    AccountPhoneCipher = phoneCipher,
                    PinCipher = pinCipher,

                    AccountEmailPlain = emailPlain,
                    AccountPhonePlain = phonePlain,
                    PinPlain = pinPlain,

                    CreatedUtc = ReadNullableUtc(r, oCreate),
                    UpdatedUtc = ReadNullableUtc(r, oUpdate)
                };
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error loading CategoryItem (Basic tab) by ItemId");
                return null;
            }
        }

        // ============================================================
        // SELECT: PasswordHistory most recent (AES-GCM + optional Sig verify)
        // ============================================================

        public static PasswordHistoryMostRecent? LoadMostRecentPasswordHistoryByItemId(long itemId)
        {
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0.");

            try
            {
                var sql = LoadSqlRequired("s_CategoryItemPasswordHistory_select_by_item_most_recent_first.sql");

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                AddInt64(cmd, "@CIPaH_ItemId", itemId);

                using var r = cmd.ExecuteReader();
                if (!r.Read())
                    return null;

                int oHistId = r.GetOrdinal("CIPaH_PwHistId");
                int oItemId = r.GetOrdinal("CIPaH_ItemId");
                int oCreated = r.GetOrdinal("CIPaH_CreatedAt");
                int oVer = r.GetOrdinal("CIPaH_Version");
                int oPw = r.GetOrdinal("CIPaH_Password");
                int oPad = r.GetOrdinal("CIPaH_PadLen");
                int oSig = r.GetOrdinal("CIPaH_PwSig");
                int oSigVer = r.GetOrdinal("CIPaH_SigVersion");

                var cipher = ReadBlobNullable(r, oPw) ?? Array.Empty<byte>();
                int? padLen = r.IsDBNull(oPad) ? (int?)null : SafeGetInt32(r, oPad);
                var sig = ReadBlobNullable(r, oSig);
                int? sigVer = r.IsDBNull(oSigVer) ? (int?)null : SafeGetInt32(r, oSigVer);

                bool sigOk = true;
                if (sig is { Length: > 0 })
                {
                    try
                    {
                        var calc = Sha256Common.Bytes(cipher);
                        sigOk = FixedTimeEquals(calc, sig);
                    }
                    catch
                    {
                        sigOk = false;
                    }
                }

                bool decOk = false;
                string? plain = null;

                if (cipher.Length == 0)
                {
                    decOk = true;
                    plain = string.Empty;
                }
                else
                {
                    if (FieldAesCrypto.TryDecryptBytes(
                            masterKeySedsName: FieldAesCrypto.SedsKey_UserSecretsKey,
                            purpose: Purpose_CIPaH_PasswordHistory,
                            blob: cipher,
                            out var plainBytes))
                    {
                        try
                        {
                            plain = Encoding.UTF8.GetString(plainBytes);
                            decOk = true;
                        }
                        finally
                        {
                            Array.Clear(plainBytes, 0, plainBytes.Length);
                        }
                    }
                }

                return new PasswordHistoryMostRecent
                {
                    PwHistId = SafeGetInt64(r, oHistId),
                    ItemId = SafeGetInt64(r, oItemId),
                    CreatedAtUtc = ReadNullableUtc(r, oCreated),
                    Version = r.IsDBNull(oVer) ? null : SafeGetInt32(r, oVer),

                    PasswordCipher = cipher,
                    PadLen = padLen,
                    PwSig = sig,
                    SigVersion = sigVer,

                    DecryptOk = decOk,
                    PasswordPlain = decOk ? plain : null,
                    SigOk = sigOk
                };
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error loading most recent password history by ItemId");
                return null;
            }
        }

        public static string? LoadMostRecentPasswordPlainByItemId(long itemId)
        {
            var row = LoadMostRecentPasswordHistoryByItemId(itemId);
            if (row == null) return null;
            if (!row.DecryptOk) return null;
            if (!row.SigOk) return null;
            return row.PasswordPlain;
        }

        // ============================================================
        // SELECT: Category Items grid
        // ============================================================

        public static ObservableCollection<CategoryItemGriud> LoadCategoryItems(int categoryKey)
        {
            if (categoryKey < 0)
                throw new ArgumentOutOfRangeException(nameof(categoryKey), "categoryKey cannot be negative.");

            var rows = new ObservableCollection<CategoryItemGriud>();

            try
            {
                var sql = LoadSqlRequired("s_CategoryItem_SelectGrid.sql");

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                AddInt32(cmd, "@Category_Key", categoryKey);

                using var r = cmd.ExecuteReader();

                int oKey1 = r.GetOrdinal("Key1");
                int oKey2 = r.GetOrdinal("Key2");
                int oKey3 = r.GetOrdinal("Key3");
                int oCol1 = r.GetOrdinal("Col1");
                int oCol2 = r.GetOrdinal("Col2");
                int oCol3 = r.GetOrdinal("Col3");
                int oDes1 = r.GetOrdinal("Des1");
                int oDes2 = r.GetOrdinal("Des2");
                int oDes3 = r.GetOrdinal("Des3");

                while (r.Read())
                {
                    rows.Add(new CategoryItemGriud
                    {
                        strCategoryItemKey1 = r.IsDBNull(oKey1) ? "" : r.GetValue(oKey1)?.ToString() ?? "",
                        strCategoryItemKey2 = r.IsDBNull(oKey2) ? "" : r.GetValue(oKey2)?.ToString() ?? "",
                        strCategoryItemKey3 = r.IsDBNull(oKey3) ? "" : r.GetValue(oKey3)?.ToString() ?? "",

                        strCategoryItem1 = r.IsDBNull(oCol1) ? "" : r.GetString(oCol1),
                        strCategoryItem2 = r.IsDBNull(oCol2) ? "" : r.GetString(oCol2),
                        strCategoryItem3 = r.IsDBNull(oCol3) ? "" : r.GetString(oCol3),

                        strCategoryItemToolTip1 = r.IsDBNull(oDes1) ? "" : r.GetString(oDes1),
                        strCategoryItemToolTip2 = r.IsDBNull(oDes2) ? "" : r.GetString(oDes2),
                        strCategoryItemToolTip3 = r.IsDBNull(oDes3) ? "" : r.GetString(oDes3),
                    });
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error loading category items");
            }

            return rows;
        }

        // ============================================================
        // INSERT: CategoryItem ONLY (bookmark-only flow)
        // ============================================================

        public static long InsertCategoryItemOnly(
            int categoryKey,
            string name,
            string? description,
            string? username,
            string? signInUrl,
            string? accountEmailPlain,
            string? accountPhonePlain,
            string? pinPlain = null,
            int? isActive = null)
        {
            return InsertCategoryItemOnlyCore(
                categoryKey: categoryKey,
                name: name,
                description: description,
                username: username,
                signInUrl: signInUrl,
                accountEmailCipher: EncryptNullableUtf8(Purpose_CI_Email, accountEmailPlain),
                accountPhoneCipher: EncryptNullableUtf8(Purpose_CI_Phone, accountPhonePlain),
                pinCipher: EncryptNullableUtf8(Purpose_CI_Pin, pinPlain),
                isActive: isActive);
        }

        private static long InsertCategoryItemOnlyCore(
            int categoryKey,
            string name,
            string? description,
            string? username,
            string? signInUrl,
            byte[]? accountEmailCipher,
            byte[]? accountPhoneCipher,
            byte[]? pinCipher,
            int? isActive = null)
        {
            if (categoryKey < 0)
                throw new ArgumentOutOfRangeException(nameof(categoryKey), "categoryKey cannot be negative.");
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required.", nameof(name));

            try
            {
                var itemSql = LoadSqlRequired("s_CategoryItem_insert.sql");

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var tx = conn.BeginTransaction();

                long newItemId;
                try
                {
                    newItemId = InsertCategoryItemCore(
                        conn, tx, itemSql,
                        categoryKey: categoryKey,
                        name: name.Trim(),
                        description: description,
                        username: username,
                        signInUrl: signInUrl,
                        bookMarkOnly: 1,
                        accountEmailCipher: accountEmailCipher,
                        accountPhoneCipher: accountPhoneCipher,
                        pinCipher: pinCipher,
                        isActive: isActive);

                    if (newItemId <= 0)
                        throw new InvalidOperationException("Insert failed (no ItemId returned)");

                    tx.Commit();
                }
                catch
                {
                    try { tx.Rollback(); } catch { }
                    throw;
                }

                BestEffortLogItemCreated(
                    itemId: newItemId,
                    categoryKey: categoryKey,
                    bookMarkOnly: 1,
                    namePresent: true,
                    descriptionPresent: !string.IsNullOrWhiteSpace(description),
                    usernamePresent: !string.IsNullOrWhiteSpace(username),
                    urlPresent: !string.IsNullOrWhiteSpace(signInUrl),
                    emailPresent: accountEmailCipher is { Length: > 0 },
                    phonePresent: accountPhoneCipher is { Length: > 0 },
                    pinPresent: pinCipher is { Length: > 0 },
                    isActive: isActive);

                return newItemId;
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error inserting category item (bookmark-only / no password history)");
                return 0;
            }
        }

        // ============================================================
        // INSERT: CategoryItem + PasswordHistory (single transaction)
        // ============================================================

        public static long InsertCategoryItemWithPasswordHistory(
            int categoryKey,
            string name,
            string? description,
            string? username,
            string? signInUrl,
            string? accountEmailPlain,
            string? accountPhonePlain,
            string? pinPlain,
            int? isActive,
            string? passwordPlain,
            bool isBookmarkOnly)
        {
            BuildPasswordHistoryPayloadAes(
                password: passwordPlain,
                isBookmarkOnly: isBookmarkOnly,
                out var pwCipher,
                out var pwPadLen,
                out var pwSig,
                out var _sigVer);

            return InsertCategoryItemWithPasswordHistoryCore(
                categoryKey: categoryKey,
                name: name,
                description: description,
                username: username,
                signInUrl: signInUrl,
                accountEmailCipher: EncryptNullableUtf8(Purpose_CI_Email, accountEmailPlain),
                accountPhoneCipher: EncryptNullableUtf8(Purpose_CI_Phone, accountPhonePlain),
                pinCipher: EncryptNullableUtf8(Purpose_CI_Pin, pinPlain),
                isActive: isActive,
                pwCipher: pwCipher,
                pwPadLen: pwPadLen,
                pwSig: pwSig);
        }

        private static long InsertCategoryItemWithPasswordHistoryCore(
            int categoryKey,
            string name,
            string? description,
            string? username,
            string? signInUrl,
            byte[]? accountEmailCipher,
            byte[]? accountPhoneCipher,
            byte[]? pinCipher,
            int? isActive,
            byte[] pwCipher,
            int? pwPadLen,
            byte[] pwSig)
        {
            if (categoryKey < 0)
                throw new ArgumentOutOfRangeException(nameof(categoryKey), "categoryKey cannot be negative.");
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required.", nameof(name));
            if (pwCipher is null)
                throw new ArgumentNullException(nameof(pwCipher));
            if (pwSig is null || pwSig.Length == 0)
                throw new ArgumentException("pwSig is required.", nameof(pwSig));

            try
            {
                var itemSql = LoadSqlRequired("s_CategoryItem_insert.sql");
                var pwSql = LoadSqlRequired("s_CategoryItemPasswordHistory_insert.sql");

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var tx = conn.BeginTransaction();

                long newItemId;
                long newPwHistId;

                try
                {
                    newItemId = InsertCategoryItemCore(
                        conn, tx, itemSql,
                        categoryKey: categoryKey,
                        name: name.Trim(),
                        description: description,
                        username: username,
                        signInUrl: signInUrl,
                        bookMarkOnly: 0,
                        accountEmailCipher: accountEmailCipher,
                        accountPhoneCipher: accountPhoneCipher,
                        pinCipher: pinCipher,
                        isActive: isActive);

                    if (newItemId <= 0)
                        throw new InvalidOperationException("Insert failed (no ItemId returned)");

                    newPwHistId = InsertPasswordHistoryCore(
                        conn, tx, pwSql,
                        itemId: newItemId,
                        pwCipher: pwCipher,
                        pwPadLen: pwPadLen,
                        pwSig: pwSig);

                    if (newPwHistId <= 0)
                        throw new InvalidOperationException("PasswordHistory insert failed (no PwHistId returned)");

                    tx.Commit();
                }
                catch
                {
                    try { tx.Rollback(); } catch { }
                    throw;
                }

                BestEffortLogItemCreated(
                    itemId: newItemId,
                    categoryKey: categoryKey,
                    bookMarkOnly: 0,
                    namePresent: true,
                    descriptionPresent: !string.IsNullOrWhiteSpace(description),
                    usernamePresent: !string.IsNullOrWhiteSpace(username),
                    urlPresent: !string.IsNullOrWhiteSpace(signInUrl),
                    emailPresent: accountEmailCipher is { Length: > 0 },
                    phonePresent: accountPhoneCipher is { Length: > 0 },
                    pinPresent: pinCipher is { Length: > 0 },
                    isActive: isActive);

                return newItemId;
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error inserting category item + password history");
                return 0;
            }
        }

        // ============================================================
        // INSERT helpers
        // ============================================================

        private static long InsertCategoryItemCore(
            SqliteConnection conn,
            SqliteTransaction tx,
            string sql,
            int categoryKey,
            string name,
            string? description,
            string? username,
            string? signInUrl,
            int bookMarkOnly,
            byte[]? accountEmailCipher,
            byte[]? accountPhoneCipher,
            byte[]? pinCipher,
            int? isActive)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;

            AddInt32(cmd, "@Category_Key", categoryKey);
            AddText(cmd, "@CI_Name", name);

            AddTextIfSqlUses(cmd, sql, "@CI_Description", description);
            AddTextIfSqlUses(cmd, sql, "@CI_Username", username);
            AddTextIfSqlUses(cmd, sql, "@CI_SignInUrl", signInUrl);

            AddInt32IfSqlUses(cmd, sql, "@CI_BookMarkOnly", NormalizeBookMarkOnly(bookMarkOnly));

            AddBlobIfSqlUses(cmd, sql, "@CI_AccountEmail", accountEmailCipher);
            AddBlobIfSqlUses(cmd, sql, "@CI_AccountPhoneNumber", accountPhoneCipher);
            AddBlobIfSqlUses(cmd, sql, "@CI_Pin", pinCipher);

            AddInt32NullableIfSqlUses(cmd, sql, "@IsActive", isActive);

#if DEBUG
            DebugDumpParams(cmd, "[CAT_ITEM][INSERT][PARAMS]");
#endif

            var scalar = cmd.ExecuteScalar();
            if (scalar == null || scalar == DBNull.Value)
                throw new InvalidOperationException("Insert failed (no ItemId returned)");

            return Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Inserts into CategoryItemPasswordHistory using sql/s_CategoryItemPasswordHistory_insert.sql.
        /// IMPORTANT: parameter names MUST match that SQL exactly:
        ///   @ItemId, @Version, @PasswordBlob, @PadLen, @PwSig, @SigVersion
        /// </summary>
        private static long InsertPasswordHistoryCore(
            SqliteConnection conn,
            SqliteTransaction tx,
            string sql,
            long itemId,
            byte[] pwCipher,
            int? pwPadLen,
            byte[] pwSig)
        {
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0 for password history insert.");
            if (pwCipher is null)
                throw new ArgumentNullException(nameof(pwCipher));
            if (pwSig is null || pwSig.Length == 0)
                throw new ArgumentException("pwSig is required.", nameof(pwSig));

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;

            // NOTE: these names match sql/s_CategoryItemPasswordHistory_insert.sql
            AddInt64(cmd, "@ItemId", itemId);

            // We can let SQL COALESCE handle defaults by passing NULL
            AddInt32Nullable(cmd, "@Version", null);

            AddBlob(cmd, "@PasswordBlob", pwCipher);
            AddInt32Nullable(cmd, "@PadLen", pwPadLen);
            AddBlob(cmd, "@PwSig", pwSig);

            // Default = 1 via COALESCE in SQL
            AddInt32Nullable(cmd, "@SigVersion", null);

#if DEBUG
            DebugDumpParams(cmd, "[CAT_ITEM][PW_HIST][INSERT][PARAMS]");
#endif

            var scalar = cmd.ExecuteScalar();
            if (scalar == null || scalar == DBNull.Value)
                throw new InvalidOperationException("PasswordHistory insert failed (no PwHistId returned)");

            return Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
        }

        // ============================================================
        // Logging (best effort, no secrets)
        // ============================================================

        private static void BestEffortLogItemCreated(
            long itemId,
            int categoryKey,
            int bookMarkOnly,
            bool namePresent,
            bool descriptionPresent,
            bool usernamePresent,
            bool urlPresent,
            bool emailPresent,
            bool phonePresent,
            bool pinPresent,
            int? isActive)
        {
            try
            {
                var dto = new AppJson.LogPayloadDto
                {
                    Message = bookMarkOnly == 1
                        ? "Category item created (bookmark-only)"
                        : "Category item created",
                    Source = "CategoryItemService",
                    EventCode = bookMarkOnly == 1
                        ? "CATEGORYITEM_CREATED_BOOKMARK_ONLY"
                        : "CATEGORYITEM_CREATED",
                    OccurredUtc = DateTime.UtcNow,
                    Context = BuildContext(new
                    {
                        itemId,
                        categoryKey,
                        bookMarkOnly,
                        fieldsPresent = new
                        {
                            name = namePresent,
                            description = descriptionPresent,
                            username = usernamePresent,
                            url = urlPresent,
                            email = emailPresent,
                            phone = phonePresent,
                            pin = pinPresent
                        },
                        isActive
                    })
                };

                LogCatalogService.AppendJson(
                    level: "INFO",
                    source: "CategoryItem",
                    eventCode: dto.EventCode ?? "CATEGORYITEM_CREATED",
                    dto: dto,
                    whenUtc: DateTime.UtcNow,
                    itemId: itemId);
            }
            catch
            {
                // Never allow logging to break insertion UX
            }
        }

        private static System.Text.Json.JsonElement? BuildContext(object obj)
        {
            try
            {
                var json = AppJson.Serialize(obj, pretty: false);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                return doc.RootElement.Clone();
            }
            catch
            {
                return null;
            }
        }

        // ============================================================
        // Normalization
        // ============================================================

        private static int NormalizeBookMarkOnly(int value)
        {
            return value switch
            {
                0 => 0,
                1 => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, "CI_BookMarkOnly must be 0 or 1.")
            };
        }

        // ============================================================
        // Reader helpers
        // ============================================================

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

        private static DateTime? ReadNullableUtc(SqliteDataReader r, int ordinal)
        {
            if (r.IsDBNull(ordinal)) return null;

            var v = r.GetValue(ordinal);

            if (v is DateTime dt)
            {
                return dt.Kind switch
                {
                    DateTimeKind.Utc => dt,
                    DateTimeKind.Local => dt.ToUniversalTime(),
                    _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                };
            }

            if (v is string s)
            {
                if (DateTime.TryParse(
                    s,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
                {
                    return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                }
                return null;
            }

            try
            {
                var s2 = Convert.ToString(v, CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(s2)) return null;

                if (DateTime.TryParse(
                    s2,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed2))
                {
                    return DateTime.SpecifyKind(parsed2, DateTimeKind.Utc);
                }
            }
            catch { }

            return null;
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= (a[i] ^ b[i]);
            return diff == 0;
        }

        // ============================================================
        // Param helpers (single place, avoids drift)
        // ============================================================

        private static bool SqlUses(string sql, string paramName)
            => sql.IndexOf(paramName, StringComparison.OrdinalIgnoreCase) >= 0;

        private static void AddTextIfSqlUses(SqliteCommand cmd, string sql, string name, string? value)
        {
            if (!SqlUses(sql, name)) return;
            AddText(cmd, name, value);
        }

        private static void AddBlobIfSqlUses(SqliteCommand cmd, string sql, string name, byte[]? value)
        {
            if (!SqlUses(sql, name)) return;
            AddBlob(cmd, name, value);
        }

        private static void AddInt32IfSqlUses(SqliteCommand cmd, string sql, string name, int value)
        {
            if (!SqlUses(sql, name)) return;
            AddInt32(cmd, name, value);
        }

        private static void AddInt32NullableIfSqlUses(SqliteCommand cmd, string sql, string name, int? value)
        {
            if (!SqlUses(sql, name)) return;
            AddInt32Nullable(cmd, name, value);
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

#if DEBUG
        private static void DebugDumpParams(SqliteCommand cmd, string tag)
        {
            Debug.WriteLine(tag);
            foreach (SqliteParameter p in cmd.Parameters)
            {
                string v = (p.Value == null || p.Value == DBNull.Value)
                    ? "NULL"
                    : (p.Value is byte[] b ? $"BLOB[{b.Length}]" : p.Value.ToString() ?? "");

                Debug.WriteLine($"  {p.ParameterName} = {v} (SqliteType={p.SqliteType})");
            }
        }
#endif
    }
}
