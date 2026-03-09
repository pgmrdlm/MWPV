// File: Services/CategoryItemService.cs
//
// FULL REWRITE (AES-only, portable, keyfile-derived)
//
// Password-history “fingerprint” work:
// - Uses STABLE keyed fingerprint (HMAC-SHA256) via SensitiveValueSignature.Compute(...)
// - Stores fingerprint in CategoryItemPasswordHistory.CIPaH_PwFp
// - Supports fingerprint version via CIPaH_FpVersion (optional but recommended)
//
// Duplicate-password warning (GLOBAL across vault):
// - Check runs inside service immediately BEFORE inserting PasswordHistory row
// - If match found => throws DuplicatePasswordWarningException
// - UI must decide: Cancel => stop / Accept => call again with allowDuplicate:true
//
// BEFORE SIGNATURE CAPTURE (THIS SESSION TASK):
// - Anything masked in the Basic UI gets a "before signature" captured here on LOAD:
//     Email, Phone, PIN
// - EXCEPT Password (handled separately later)
// - Signatures stored in SEDS as Base64 (non-sensitive signature bytes), with DEBUG lines.
//
// IMPORTANT:
// - The BEFORE signature capture MUST use the SAME decrypt path used for UI display.
//   That means we call TryDecryptUtf8(...) and capture signatures ONLY on decrypt success.
//
// IMPORTANT FIX (EMPTY ENCRYPTED COLUMNS):
// - We must NOT decrypt NULL/empty blobs.
// - NULL/empty blob must be treated as a CLEAN SUCCESS (value = empty).
// - This prevents false decrypt failures for empty DB columns.
// - This also ensures we still capture a stable "empty before signature" for comparisons.
//
// Important SQL expectations:
// - CategoryItemPasswordHistory insert expects params:
//      @ItemId, @Version, @PasswordBlob, @PadLen, @PwFp
//   Optional (recommended): @FpVersion
//
// - Reuse check SQL expects param:
//      @CIPaH_PwFp
//
// Notes:
// - This service never reveals which item matched. Warning is generic only.
// - Bookmark-only does NOT trigger duplicate warning (no password).
//
// LOGGING NOTE (IMPORTANT):
// - Service no longer writes "item created" logs.
// - Logging is now centralized at the UI layer (CategoryItemEditorTabs) so it can
//   populate SubjectText/MessageText (template expanded) without duplicate inserts.
//

using Microsoft.Data.Sqlite;
using MWPV.Models;
using Security.Utility.Crypto.Fields;    // FieldAesCrypto (AES-GCM portable)
using Security.Utility.Storage;          // SecureEncryptedDataStore (SEDS)
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Utilities.Helpers;                 // DatabaseHelper, ErrorHandler
using Utilities.Sql;                     // SqlCagegory (SQL catalog/loader)

// Helper assumed to exist
using Security.Utility.Crypto.Signatures;  // SensitiveValueSignature

namespace MWPV.Services
{
    public static class CategoryItemService
    {
        // ============================================================
        // Purposes (AAD / domain separation) - must be stable forever
        // ============================================================

        private const string Purpose_CI_Email = "CI.Email";
        private const string Purpose_CI_Phone = "CI.Phone";
        private const string Purpose_CI_Pin = "CI.Pin";

        private const string Purpose_BC_Number = "BC.Number";
        private const string Purpose_BC_CVV = "BC.CVV";
        private const string Purpose_BC_Pin = "BC.Pin";
        private const string Purpose_BC_BillingZip = "BC.BillingZip";

        private const string Purpose_CIPaH_PasswordHistory = "CIPaH.PasswordHistory";
        private const string Purpose_CIPaH_PasswordFingerprint = "PW.Fingerprint.V1";

        // BEFORE signatures (masked fields EXCEPT password)
        private const string Purpose_CI_Email_BeforeSig = "CI.Email.BeforeSig.V1";
        private const string Purpose_CI_Phone_BeforeSig = "CI.Phone.BeforeSig.V1";
        private const string Purpose_CI_Pin_BeforeSig = "CI.Pin.BeforeSig.V1";

        private const int CurrentFingerprintVersion = 1;

        // ============================================================
        // SEDS keys for BEFORE signatures (Base64 strings)
        // ============================================================

        // NOTE: These are non-sensitive signatures, but we still treat them as session-only.
        private const string SedsKey_BeforeSig_Email = "CI.BeforeSig.Email";
        private const string SedsKey_BeforeSig_Phone = "CI.BeforeSig.Phone";
        private const string SedsKey_BeforeSig_Pin = "CI.BeforeSig.Pin";

        // ============================================================
        // Duplicate warning exception (service-driven)
        // ============================================================

        public sealed class DuplicatePasswordWarningException : Exception
        {
            public DuplicatePasswordWarningException()
                : base("DUPLICATE_PASSWORD_WARNING") { }
        }

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

            public int BookMarkOnly { get; init; } // 0/1
            public int? IsActive { get; init; }    // nullable allowed

            // Encrypted-at-rest blobs
            public byte[]? AccountEmailCipher { get; init; } // CI_AccountEmail
            public byte[]? AccountPhoneCipher { get; init; } // CI_AccountPhoneNumber
            public byte[]? PinCipher { get; init; }          // CI_Pin

            // Decrypted (best-effort)
            public string? AccountEmailPlain { get; init; }
            public string? AccountPhonePlain { get; init; }
            public string? PinPlain { get; init; }

            public DateTime? CreatedUtc { get; init; }
            public DateTime? UpdatedUtc { get; init; }
        }

        // ============================================================
        // DTO: Most-recent password history row (AES-GCM + fingerprint verify)
        // ============================================================

        public sealed class PasswordHistoryMostRecent
        {
            public long PwHistId { get; init; }
            public long ItemId { get; init; }
            public DateTime? CreatedAtUtc { get; init; }
            public int? Version { get; init; }

            public byte[] PasswordCipher { get; init; } = Array.Empty<byte>();
            public int? PadLen { get; init; }

            // Stable keyed fingerprint (HMAC-SHA256)
            public byte[]? PwFp { get; init; }
            public int? FpVersion { get; init; }

            public bool DecryptOk { get; init; }
            public string? PasswordPlain { get; init; }

            // True when stored PwFp matches recomputed PwFp from decrypted plaintext
            public bool FingerprintOk { get; init; }
        }

        // ============================================================

        // ============================================================
        // DTO: BankCards row (service contract for BankCards tab)
        // ============================================================

        public sealed class BankCardRow
        {
            public long Id { get; init; }
            public long ItemId { get; init; }

            public int CardTypeId { get; init; }
            public string CardTypeDisplay { get; init; } = string.Empty;
            public string? Cardholder { get; init; }

            // Save-input values (raw/plain)
            public string CardNumberRaw { get; init; } = string.Empty;
            public string ExpirationDisplay { get; init; } = string.Empty; // MM/YYYY
            public int ExpMonth { get; init; }
            public int ExpYear { get; init; }
            public string CvvRaw { get; init; } = string.Empty;
            public string PinRaw { get; init; } = string.Empty;
            public string? BillingZipRaw { get; init; }

            // Display values (masked)
            public string CardNumberMasked { get; init; } = string.Empty;
            public string CvvMasked { get; init; } = string.Empty;
            public string PinMasked { get; init; } = string.Empty;

            public bool IsPrimary { get; init; }
            public bool IsActive { get; init; } = true;

            public long CreatedAtUtcSeconds { get; init; }
            public long UpdatedAtUtcSeconds { get; init; }
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

        /// <summary>
        /// THIS IS THE decrypt path the Basic UI uses for display.
        /// We do not add alternate decrypt logic anywhere else.
        ///
        /// IMPORTANT FIX:
        /// - NULL/empty blobs are NOT decrypted.
        /// - NULL/empty blobs are treated as a CLEAN SUCCESS (empty value).
        ///   This prevents false decrypt failures for empty DB columns.
        /// </summary>
        private static bool TryDecryptUtf8(string purpose, byte[]? cipherBlob, out string? plain)
        {
            // Default: empty/absent fields are just empty.
            plain = null;

            // Empty DB column must NOT be treated as "decrypt failed".
            // It's simply "no value".
            if (cipherBlob is null || cipherBlob.Length == 0)
            {
                // decOk=true with plain=null (normalized to "" by signature capture)
                return true;
            }

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

        // ============================================================
        // BEFORE SIGNATURE CAPTURE (masked fields except password)
        // ============================================================

        private static void ClearBeforeSignatureSedsKeys()
        {
            try { SecureEncryptedDataStore.Clear(SedsKey_BeforeSig_Email); } catch { }
            try { SecureEncryptedDataStore.Clear(SedsKey_BeforeSig_Phone); } catch { }
            try { SecureEncryptedDataStore.Clear(SedsKey_BeforeSig_Pin); } catch { }
        }

        /// <summary>
        /// Captures a "before signature" into SEDS ONLY when decrypt succeeds.
        /// Signature is computed over normalized plaintext (null => "").
        /// </summary>
        private static void CaptureBeforeSignatureToSeds(string sedsKey, string purpose, string? plain, bool decryptOk)
        {
            // We ONLY capture if decrypt succeeded.
            if (!decryptOk)
            {
                try { SecureEncryptedDataStore.Clear(sedsKey); } catch { }

#if DEBUG
                Debug.WriteLine($"[CI][BEFORE-SIG] SKIP (decrypt failed) sedsKey={sedsKey} purpose={purpose}");
#endif
                return;
            }

            // Normalize: null -> empty string (stable signature)
            string value = plain ?? string.Empty;

            byte[] keyBytes = SecureEncryptedDataStore.GetBytes(FieldAesCrypto.SedsKey_UserSecretsKey);

            try
            {
                byte[] sig = SensitiveValueSignature.Compute(value, keyBytes, purpose: purpose);

                try
                {
                    // Store as Base64 string (non-sensitive signature bytes)
                    string b64 = Convert.ToBase64String(sig);
                    SecureEncryptedDataStore.SetString(sedsKey, b64);

#if DEBUG
                    bool present = !string.IsNullOrWhiteSpace(value);
                    Debug.WriteLine($"[CI][BEFORE-SIG] CAPTURED sedsKey={sedsKey} purpose={purpose} sigLen={sig.Length} present={present}");
#endif
                }
                finally
                {
                    Array.Clear(sig, 0, sig.Length);
                }
            }
            finally
            {
                Array.Clear(keyBytes, 0, keyBytes.Length);
            }
        }

        /// <summary>
        /// SINGLE SOURCE OF TRUTH FOR "decrypt -> before signature" for masked fields.
        /// Uses the SAME decrypt path used by the Basic UI display.
        /// </summary>
        private static bool TryDecryptUtf8AndCaptureBeforeSig(
            long itemId,
            string fieldNameForDebug,
            string decryptPurpose,
            byte[]? cipherBlob,
            string sigSedsKey,
            string sigPurpose,
            out string? plain)
        {
            plain = null;

            // 1) Decrypt using the SAME method used by the Basic UI path
            bool decOk = TryDecryptUtf8(decryptPurpose, cipherBlob, out plain);

            // 2) Capture signature ONLY if decrypt succeeded (includes empty field success)
            try
            {
                CaptureBeforeSignatureToSeds(sigSedsKey, sigPurpose, plain, decOk);

#if DEBUG
                Debug.WriteLine($"[CI][BEFORE-SIG] itemId={itemId} field={fieldNameForDebug} decOk={decOk} plainLen={(plain == null ? -1 : plain.Length)} cipherLen={(cipherBlob == null ? -1 : cipherBlob.Length)}");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[CI][BEFORE-SIG] CAPTURE FAILED itemId={itemId} field={fieldNameForDebug}: {ex}");
#endif
                // Fail-safe: clear the key to avoid stale/incorrect comparisons
                try { SecureEncryptedDataStore.Clear(sigSedsKey); } catch { }
            }

            return decOk;
        }

        private static void CaptureBeforeSignaturesForBasicMaskedFields_AlreadyCaptured()
        {
            // Intentionally empty.
            // This exists only to stop anyone from reintroducing the old two-pass pattern:
            // (decrypt in one place, then capture in another).
            // We capture inside TryDecryptUtf8AndCaptureBeforeSig(...).
        }

        // ============================================================
        // Password fingerprint helpers (stable reuse detection)
        // ============================================================

        /// <summary>
        /// Computes stable, keyed fingerprint (32 bytes) for a plaintext password.
        /// Uses SEDS UserSecretsKey as secret. Purpose-separated with PW.Fingerprint.V1.
        /// </summary>
        private static byte[] ComputePasswordFingerprint(string? passwordPlain)
        {
            // IMPORTANT: We assume SEDS returns a COPY of the key bytes.
            // If it ever returns a live reference, wiping here would be catastrophic.
            byte[] keyBytes = SecureEncryptedDataStore.GetBytes(FieldAesCrypto.SedsKey_UserSecretsKey);

            try
            {
                return SensitiveValueSignature.Compute(
                    passwordPlain,
                    keyBytes,
                    purpose: Purpose_CIPaH_PasswordFingerprint);
            }
            finally
            {
                Array.Clear(keyBytes, 0, keyBytes.Length);
            }
        }

        /// <summary>
        /// Builds PasswordHistory payload (AES-GCM + stable fingerprint).
        /// Bookmark-only => empty cipher; fingerprint computed over "" (for stable checks).
        /// </summary>
        public static void BuildPasswordHistoryPayloadAes(
            string? password,
            bool isBookmarkOnly,
            out byte[] pwCipher,
            out int? padLen,
            out byte[] pwFp,
            out int fpVersion)
        {
            padLen = null;
            fpVersion = CurrentFingerprintVersion;

            if (isBookmarkOnly)
            {
                pwCipher = Array.Empty<byte>();
                pwFp = ComputePasswordFingerprint(string.Empty);
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

                pwFp = ComputePasswordFingerprint(pw);
            }
            finally
            {
                Array.Clear(pwBytes, 0, pwBytes.Length);
            }
        }

        // ============================================================
        // DUPLICATE NAME CHECK (GLOBAL across all categories)
        // ============================================================

        public static bool ItemNameExistsAcrossAllCategories(string name, long? excludeItemId = null)
            => CategoryItemNameExistsGlobal(name, excludeItemId);

        private static bool CategoryItemNameExistsGlobal(string name, long? excludeItemId)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            try
            {
                var sql = LoadSqlRequired("s_CategoryItem_exists_by_name.sql");

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                AddText(cmd, "@CI_Name", name.Trim());
                AddInt64Nullable(cmd, "@ExcludeItemId", excludeItemId);

#if DEBUG
                DebugDumpParams(cmd, "[CAT_ITEM][NAME_EXISTS_GLOBAL][PARAMS]");
#endif

                using var r = cmd.ExecuteReader();
                return r.Read();
            }
            catch (Exception ex)
            {
                // Fail closed
                ErrorHandler.Abend(ex, "Error checking CategoryItem name existence (global)");
                return true;
            }
        }

        // ============================================================
        // PASSWORD REUSE CHECKS (fingerprint-based)
        // ============================================================

        /// <summary>
        /// Optional helper (local policy). Not used by the global warning rule.
        /// </summary>
        public static bool PasswordReuseWithin365Days(long itemId, string? passwordPlain)
        {
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0.");

            var fp = ComputePasswordFingerprint(passwordPlain ?? string.Empty);

            try
            {
                var sql = LoadSqlRequired("s_CategoryItemPasswordHistory_check_reuse_365days.sql");

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                AddInt64(cmd, "@CIPaH_ItemId", itemId);
                AddBlob(cmd, "@CIPaH_PwFp", fp);

#if DEBUG
                DebugDumpParams(cmd, "[PW_REUSE_365][PARAMS]");
#endif

                object? scalar = cmd.ExecuteScalar();
                if (scalar == null || scalar == DBNull.Value)
                    return false;

                int v = Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
                return v == 1;
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error checking password reuse within 365 days");
                return false;
            }
            finally
            {
                Array.Clear(fp, 0, fp.Length);
            }
        }

        /// <summary>
        /// Public helper if UI wants to query, but the ENFORCEMENT is inside insert paths.
        /// </summary>
        public static bool PasswordReuseExistsAnywhere(string? passwordPlain)
        {
            var fp = ComputePasswordFingerprint(passwordPlain ?? string.Empty);

            try
            {
                return PasswordReuseExistsAnywhereByFingerprint(fp);
            }
            finally
            {
                Array.Clear(fp, 0, fp.Length);
            }
        }

        /// <summary>
        /// Core reuse check: requires fingerprint only. SQL must accept @CIPaH_PwFp.
        /// </summary>
        private static bool PasswordReuseExistsAnywhereByFingerprint(byte[] pwFp)
        {
            if (pwFp is null || pwFp.Length == 0)
                return false;

            try
            {
                string sql;
                try
                {
                    sql = LoadSqlRequired("s_CategoryItemPasswordHistory_check_reuse_365days.sql");
                }
                catch
                {
#if DEBUG
                    Debug.WriteLine("[PW_REUSE_ANYWHERE] SQL asset missing: s_CategoryItemPasswordHistory_check_reuse_anywhere.sql (returning false).");
#endif
                    return false;
                }

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                // IMPORTANT: reuse-check SQL expects @CIPaH_PwFp
                AddBlob(cmd, "@CIPaH_PwFp", pwFp);

#if DEBUG
                DebugDumpParams(cmd, "[PW_REUSE_ANYWHERE][BY_FP][PARAMS]");
#endif

                object? scalar = cmd.ExecuteScalar();
                if (scalar == null || scalar == DBNull.Value)
                    return false;

                int v = Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
                return v == 1;
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error checking password reuse anywhere (by fingerprint)");
                return false;
            }
        }

        /// <summary>
        /// Enforcer: If reused and not allowed, throws DuplicatePasswordWarningException.
        /// This is the ONE centralized gate.
        /// </summary>
        private static void ThrowIfPasswordReusedElsewhere(byte[] pwFp, bool allowDuplicate)
        {
            if (allowDuplicate)
                return;

            if (PasswordReuseExistsAnywhereByFingerprint(pwFp))
                throw new DuplicatePasswordWarningException();
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

                // =======================================================
                // BEFORE SIGNATURE CAPTURE (SAME decrypt path as UI display)
                // =======================================================

                bool emailDecOk = TryDecryptUtf8AndCaptureBeforeSig(
                    itemId: itemId,
                    fieldNameForDebug: "Email",
                    decryptPurpose: Purpose_CI_Email,
                    cipherBlob: emailCipher,
                    sigSedsKey: SedsKey_BeforeSig_Email,
                    sigPurpose: Purpose_CI_Email_BeforeSig,
                    out emailPlain);

                bool phoneDecOk = TryDecryptUtf8AndCaptureBeforeSig(
                    itemId: itemId,
                    fieldNameForDebug: "Phone",
                    decryptPurpose: Purpose_CI_Phone,
                    cipherBlob: phoneCipher,
                    sigSedsKey: SedsKey_BeforeSig_Phone,
                    sigPurpose: Purpose_CI_Phone_BeforeSig,
                    out phonePlain);

                bool pinDecOk = TryDecryptUtf8AndCaptureBeforeSig(
                    itemId: itemId,
                    fieldNameForDebug: "PIN",
                    decryptPurpose: Purpose_CI_Pin,
                    cipherBlob: pinCipher,
                    sigSedsKey: SedsKey_BeforeSig_Pin,
                    sigPurpose: Purpose_CI_Pin_BeforeSig,
                    out pinPlain);

#if DEBUG
                Debug.WriteLine($"[CI][BEFORE-SIG] COMPLETE itemId={itemId} (Email/Phone/PIN) captured into SEDS. decOk: email={emailDecOk} phone={phoneDecOk} pin={pinDecOk}");
#endif

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
        // SELECT: PasswordHistory most recent (AES-GCM + fingerprint verify)
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

                int oFp = r.GetOrdinal("CIPaH_PwFp");

                // Optional: if present in SELECT
                int oFpVer = -1;
                try { oFpVer = r.GetOrdinal("CIPaH_FpVersion"); } catch { oFpVer = -1; }

                var cipher = ReadBlobNullable(r, oPw) ?? Array.Empty<byte>();
                int? padLen = r.IsDBNull(oPad) ? (int?)null : SafeGetInt32(r, oPad);

                var fpStored = ReadBlobNullable(r, oFp);
                int? fpVerStored = (oFpVer >= 0 && !r.IsDBNull(oFpVer)) ? SafeGetInt32(r, oFpVer) : null;

                bool decOk = false;
                string? plain = null;

                if (cipher.Length == 0)
                {
                    // bookmark-only / empty password history payload
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

                bool fpOk = true;

                if (fpStored is { Length: > 0 })
                {
                    if (!decOk)
                    {
                        fpOk = false;
                    }
                    else
                    {
                        byte[] fpCalc = ComputePasswordFingerprint(plain ?? string.Empty);
                        try
                        {
                            fpOk = CryptographicOperations.FixedTimeEquals(fpCalc, fpStored);
                        }
                        finally
                        {
                            Array.Clear(fpCalc, 0, fpCalc.Length);
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

                    PwFp = fpStored,
                    FpVersion = fpVerStored,

                    DecryptOk = decOk,
                    PasswordPlain = decOk ? plain : null,
                    FingerprintOk = fpOk
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
            if (!row.FingerprintOk) return null;
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
        // ============================================================
        // BANK CARDS: load/save
        // ============================================================

        public static IReadOnlyList<BankCardRow> LoadBankCardsByItemId(long itemId)
        {
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0.");

            var rows = new List<BankCardRow>();

            try
            {
                var sql = LoadSqlRequired("s_BankCard_select_by_itemid.sql");

                // Card type lookup is best-effort display enrichment only.
                var cardTypeDisplayById = new Dictionary<int, string>();
                try
                {
                    foreach (var t in ComboDetailService.GetByTypeId(2))
                    {
                        if (!cardTypeDisplayById.ContainsKey(t.ComboDet))
                        {
                            cardTypeDisplayById[t.ComboDet] =
                                string.IsNullOrWhiteSpace(t.Description) ? (t.Code ?? string.Empty) : t.Description;
                        }
                    }
                }
                catch { }

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                AddInt64(cmd, "@ItemId", itemId);

                using var r = cmd.ExecuteReader();

                int oId = r.GetOrdinal("Id");
                int oItemId = r.GetOrdinal("ItemId");
                int oCardTypeId = r.GetOrdinal("CardTypeId");
                int oCardholder = r.GetOrdinal("Cardholder");
                int oNumber = r.GetOrdinal("Number");
                int oExpMonth = r.GetOrdinal("ExpMonth");
                int oExpYear = r.GetOrdinal("ExpYear");
                int oCvv = r.GetOrdinal("Cvv");
                int oPin = r.GetOrdinal("Pin");
                int oBillingZip = r.GetOrdinal("BillingZip");
                int oIsPrimary = r.GetOrdinal("IsPrimary");
                int oIsActive = r.GetOrdinal("IsActive");
                int oCreated = r.GetOrdinal("CreatedAtUtcSeconds");
                int oUpdated = r.GetOrdinal("UpdatedAtUtcSeconds");

                while (r.Read())
                {
                    long id = SafeGetInt64(r, oId);
                    long rowItemId = SafeGetInt64(r, oItemId);
                    int cardTypeId = SafeGetInt32(r, oCardTypeId);

                    string? cardholder = r.IsDBNull(oCardholder) ? null : r.GetString(oCardholder);

                    byte[]? numberCipher = ReadBlobNullable(r, oNumber);
                    byte[]? cvvCipher = ReadBlobNullable(r, oCvv);
                    byte[]? pinCipher = ReadBlobNullable(r, oPin);
                    byte[]? billingZipCipher = ReadBlobNullable(r, oBillingZip);

                    _ = TryDecryptUtf8(Purpose_BC_Number, numberCipher, out string? numberPlain);
                    _ = TryDecryptUtf8(Purpose_BC_CVV, cvvCipher, out string? cvvPlain);
                    _ = TryDecryptUtf8(Purpose_BC_Pin, pinCipher, out string? pinPlain);
                    _ = TryDecryptUtf8(Purpose_BC_BillingZip, billingZipCipher, out _);

                    int expMonth = SafeGetInt32(r, oExpMonth);
                    int expYear = SafeGetInt32(r, oExpYear);

                    string expDisplay = (expMonth >= 1 && expMonth <= 12 && expYear > 0)
                        ? $"{expMonth:00}/{expYear:0000}"
                        : string.Empty;

                    bool isPrimary = !r.IsDBNull(oIsPrimary) && SafeGetInt32(r, oIsPrimary) == 1;
                    bool isActive = r.IsDBNull(oIsActive) || SafeGetInt32(r, oIsActive) == 1;

                    string cardTypeDisplay = cardTypeDisplayById.TryGetValue(cardTypeId, out var display)
                        ? display
                        : string.Empty;

                    rows.Add(new BankCardRow
                    {
                        Id = id,
                        ItemId = rowItemId,

                        CardTypeId = cardTypeId,
                        CardTypeDisplay = cardTypeDisplay,
                        Cardholder = cardholder,

                        // Service load does not return plaintext secrets to UI.
                        CardNumberRaw = string.Empty,
                        ExpirationDisplay = expDisplay,
                        ExpMonth = expMonth,
                        ExpYear = expYear,
                        CvvRaw = string.Empty,
                        PinRaw = string.Empty,
                        BillingZipRaw = null,

                        CardNumberMasked = MaskPanLast4(numberPlain),
                        CvvMasked = string.IsNullOrWhiteSpace(cvvPlain) ? string.Empty : "***",
                        PinMasked = string.IsNullOrWhiteSpace(pinPlain) ? string.Empty : "***",

                        IsPrimary = isPrimary,
                        IsActive = isActive,

                        CreatedAtUtcSeconds = r.IsDBNull(oCreated) ? 0 : SafeGetInt64(r, oCreated),
                        UpdatedAtUtcSeconds = r.IsDBNull(oUpdated) ? 0 : SafeGetInt64(r, oUpdated)
                    });
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error loading BankCards by ItemId");
            }

            return rows;
        }

        /// <summary>
        /// Saves BankCards for the given item.
        ///
        /// Policy used for minimal wiring:
        /// - New rows (Id <= 0): INSERT via s_BankCard_insert.sql.
        /// - Existing rows (Id > 0): UPDATE only when CardNumberRaw is present
        ///   (unchanged service-loaded rows carry empty raw fields and are skipped).
        ///
        /// DEPENDENCY OUTSIDE THIS FILE:
        /// - Requires sql/s_BankCard_update.sql to exist and be loaded in SqlCagegory.
        /// </summary>
        public static int SaveBankCardsByItemId(long itemId, IReadOnlyList<BankCardRow>? rows)
        {
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0.");

            var input = rows ?? Array.Empty<BankCardRow>();

            try
            {
                var insertSql = LoadSqlRequired("s_BankCard_insert.sql");
                var updateSql = LoadSqlRequired("s_BankCard_update.sql");

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var tx = conn.BeginTransaction();

                int writes = 0;

                try
                {
                    foreach (var row in input)
                    {
                        if (row == null)
                            continue;

                        if (row.ItemId > 0 && row.ItemId != itemId)
                            throw new InvalidOperationException("BankCard row ItemId does not match active item.");

                        if (row.CardTypeId <= 0)
                            throw new InvalidOperationException("BankCard CardTypeId is required.");

                        int expMonth = row.ExpMonth;
                        int expYear = row.ExpYear;

                        if (expMonth <= 0 || expYear <= 0)
                        {
                            var expText = (row.ExpirationDisplay ?? string.Empty).Trim();
                            var parts = expText.Split('/', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length != 2)
                                throw new InvalidOperationException("BankCard expiration must be MM/YYYY.");

                            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out expMonth) ||
                                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out expYear))
                                throw new InvalidOperationException("BankCard expiration is invalid.");

                            if (expYear < 100)
                                expYear += 2000;
                        }

                        if (expMonth < 1 || expMonth > 12)
                            throw new InvalidOperationException("BankCard expiration month must be 1-12.");

                        byte[]? numberCipher = EncryptNullableUtf8(Purpose_BC_Number, row.CardNumberRaw);
                        byte[]? cvvCipher = EncryptNullableUtf8(Purpose_BC_CVV, row.CvvRaw);
                        byte[]? pinCipher = EncryptNullableUtf8(Purpose_BC_Pin, row.PinRaw);
                        byte[]? billingZipCipher = EncryptNullableUtf8(Purpose_BC_BillingZip, row.BillingZipRaw);

                        try
                        {
                            if (row.Id <= 0)
                            {
                                if (numberCipher is null || numberCipher.Length == 0)
                                    throw new InvalidOperationException("Card number is required for new BankCard rows.");

                                long newId = InsertBankCardCore(
                                    conn: conn,
                                    tx: tx,
                                    sql: insertSql,
                                    itemId: itemId,
                                    cardTypeId: row.CardTypeId,
                                    cardholder: row.Cardholder,
                                    numberCipher: numberCipher,
                                    expMonth: expMonth,
                                    expYear: expYear,
                                    cvvCipher: cvvCipher,
                                    pinCipher: pinCipher,
                                    billingZipCipher: billingZipCipher,
                                    isPrimary: row.IsPrimary,
                                    isActive: row.IsActive);

                                if (newId <= 0)
                                    throw new InvalidOperationException("BankCard insert failed (no Id returned).");

                                writes++;
                            }
                            else
                            {
                                // Existing rows loaded from service have empty raw values unless user re-entered/editing.
                                if (string.IsNullOrWhiteSpace(row.CardNumberRaw))
                                    continue;

                                int affected = UpdateBankCardCore(
                                    conn: conn,
                                    tx: tx,
                                    sql: updateSql,
                                    id: row.Id,
                                    itemId: itemId,
                                    cardTypeId: row.CardTypeId,
                                    cardholder: row.Cardholder,
                                    numberCipher: numberCipher,
                                    expMonth: expMonth,
                                    expYear: expYear,
                                    cvvCipher: cvvCipher,
                                    pinCipher: pinCipher,
                                    billingZipCipher: billingZipCipher,
                                    isPrimary: row.IsPrimary,
                                    isActive: row.IsActive);

                                if (affected > 0)
                                    writes += affected;
                            }
                        }
                        finally
                        {
                            if (numberCipher != null) Array.Clear(numberCipher, 0, numberCipher.Length);
                            if (cvvCipher != null) Array.Clear(cvvCipher, 0, cvvCipher.Length);
                            if (pinCipher != null) Array.Clear(pinCipher, 0, pinCipher.Length);
                            if (billingZipCipher != null) Array.Clear(billingZipCipher, 0, billingZipCipher.Length);
                        }
                    }

                    tx.Commit();
                }
                catch
                {
                    try { tx.Rollback(); } catch { }
                    throw;
                }

                return writes;
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error saving BankCards by ItemId");
                return 0;
            }
        }

        private static long InsertBankCardCore(
            SqliteConnection conn,
            SqliteTransaction tx,
            string sql,
            long itemId,
            int cardTypeId,
            string? cardholder,
            byte[] numberCipher,
            int expMonth,
            int expYear,
            byte[]? cvvCipher,
            byte[]? pinCipher,
            byte[]? billingZipCipher,
            bool isPrimary,
            bool isActive)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;

            AddInt64(cmd, "@ItemId", itemId);
            AddInt32(cmd, "@CardTypeId", cardTypeId);
            AddText(cmd, "@Cardholder", string.IsNullOrWhiteSpace(cardholder) ? null : cardholder);
            AddBlob(cmd, "@Number", numberCipher);
            AddInt32(cmd, "@ExpMonth", expMonth);
            AddInt32(cmd, "@ExpYear", expYear);
            AddBlob(cmd, "@Cvv", cvvCipher);
            AddBlob(cmd, "@Pin", pinCipher);
            AddBlob(cmd, "@BillingZip", billingZipCipher);
            AddInt32(cmd, "@IsPrimary", isPrimary ? 1 : 0);
            AddInt32(cmd, "@IsActive", isActive ? 1 : 0);

#if DEBUG
            DebugDumpParams(cmd, "[BANKCARD][INSERT][PARAMS]");
#endif

            var scalar = cmd.ExecuteScalar();
            if (scalar == null || scalar == DBNull.Value)
                throw new InvalidOperationException("BankCard insert failed (no Id returned)");

            return Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
        }

        private static int UpdateBankCardCore(
            SqliteConnection conn,
            SqliteTransaction tx,
            string sql,
            long id,
            long itemId,
            int cardTypeId,
            string? cardholder,
            byte[]? numberCipher,
            int expMonth,
            int expYear,
            byte[]? cvvCipher,
            byte[]? pinCipher,
            byte[]? billingZipCipher,
            bool isPrimary,
            bool isActive)
        {
            if (numberCipher is null || numberCipher.Length == 0)
                throw new InvalidOperationException("Card number is required for BankCard update.");

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;

            // Minimal SQL compatibility: support either @Id or @BC_Id for row key.
            if (SqlUses(sql, "@Id"))
                AddInt64(cmd, "@Id", id);
            else if (SqlUses(sql, "@BC_Id"))
                AddInt64(cmd, "@BC_Id", id);
            else
                throw new InvalidOperationException("s_BankCard_update.sql must include @Id or @BC_Id parameter.");

            AddInt64IfSqlUses(cmd, sql, "@ItemId", itemId);
            AddInt32IfSqlUses(cmd, sql, "@CardTypeId", cardTypeId);
            AddTextIfSqlUses(cmd, sql, "@Cardholder", string.IsNullOrWhiteSpace(cardholder) ? null : cardholder);
            AddBlobIfSqlUses(cmd, sql, "@Number", numberCipher);
            AddInt32IfSqlUses(cmd, sql, "@ExpMonth", expMonth);
            AddInt32IfSqlUses(cmd, sql, "@ExpYear", expYear);
            AddBlobIfSqlUses(cmd, sql, "@Cvv", cvvCipher);
            AddBlobIfSqlUses(cmd, sql, "@Pin", pinCipher);
            AddBlobIfSqlUses(cmd, sql, "@BillingZip", billingZipCipher);
            AddInt32IfSqlUses(cmd, sql, "@IsPrimary", isPrimary ? 1 : 0);
            AddInt32IfSqlUses(cmd, sql, "@IsActive", isActive ? 1 : 0);

#if DEBUG
            DebugDumpParams(cmd, "[BANKCARD][UPDATE][PARAMS]");
#endif

            return cmd.ExecuteNonQuery();
        }

        private static string MaskPanLast4(string? panPlain)
        {
            if (string.IsNullOrWhiteSpace(panPlain))
                return string.Empty;

            var digits = new StringBuilder();
            foreach (char c in panPlain)
            {
                if (char.IsDigit(c))
                    digits.Append(c);
            }

            if (digits.Length == 0)
                return string.Empty;

            string d = digits.ToString();
            string last4 = d.Length <= 4 ? d : d.Substring(d.Length - 4, 4);
            return $"**** {last4}";
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

            if (CategoryItemNameExistsGlobal(name, excludeItemId: null))
                throw new InvalidOperationException("Category item name already exists (global uniqueness rule).");

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

                // NOTE: Logging removed from service (UI now owns created/change logs).
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
        // DUPLICATE WARNING enforced HERE (service gate)
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
            bool isBookmarkOnly,
            bool allowDuplicate = false)
        {
            BuildPasswordHistoryPayloadAes(
                password: passwordPlain,
                isBookmarkOnly: isBookmarkOnly,
                out var pwCipher,
                out var pwPadLen,
                out var pwFp,
                out var fpVersion);

            try
            {
                // DUP CHECK: only real passwords, not bookmark-only
                if (!isBookmarkOnly)
                    ThrowIfPasswordReusedElsewhere(pwFp, allowDuplicate);

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
                    pwFp: pwFp,
                    fpVersion: fpVersion);
            }
            finally
            {
                // wipe buffers best-effort
                try { Array.Clear(pwFp, 0, pwFp.Length); } catch { }
                try { Array.Clear(pwCipher, 0, pwCipher.Length); } catch { }
            }
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
            byte[] pwFp,
            int fpVersion)
        {
            if (categoryKey < 0)
                throw new ArgumentOutOfRangeException(nameof(categoryKey), "categoryKey cannot be negative.");
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required.", nameof(name));
            if (pwCipher is null)
                throw new ArgumentNullException(nameof(pwCipher));
            if (pwFp is null || pwFp.Length == 0)
                throw new ArgumentException("pwFp is required.", nameof(pwFp));
            if (fpVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(fpVersion), "fpVersion must be > 0.");

            if (CategoryItemNameExistsGlobal(name, excludeItemId: null))
                throw new InvalidOperationException("Category item name already exists (global uniqueness rule).");

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
                        pwFp: pwFp,
                        fpVersion: fpVersion);

                    if (newPwHistId <= 0)
                        throw new InvalidOperationException("PasswordHistory insert failed (no PwHistId returned)");

                    tx.Commit();
                }
                catch
                {
                    try { tx.Rollback(); } catch { }
                    throw;
                }

                // NOTE: Logging removed from service (UI now owns created/change logs).
                return newItemId;
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error inserting category item + password history");
                return 0;
            }
        }

        // ============================================================
        // INSERT: PasswordHistory for EXISTING ITEM
        // DUPLICATE WARNING enforced HERE (service gate)
        // ============================================================

        public static long InsertPasswordHistoryForExistingItem(
            long itemId,
            string? passwordPlain,
            bool isBookmarkOnly,
            bool allowDuplicate = false)
        {
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0.");

            BuildPasswordHistoryPayloadAes(
                password: passwordPlain,
                isBookmarkOnly: isBookmarkOnly,
                out var pwCipher,
                out var pwPadLen,
                out var pwFp,
                out var fpVersion);

            try
            {
                // DUP CHECK: only real passwords, not bookmark-only
                if (!isBookmarkOnly)
                    ThrowIfPasswordReusedElsewhere(pwFp, allowDuplicate);

                var pwSql = LoadSqlRequired("s_CategoryItemPasswordHistory_insert.sql");

                using var conn = DatabaseHelper.GetAppOpenConnection();
                using var tx = conn.BeginTransaction();

                long newPwHistId;

                try
                {
                    newPwHistId = InsertPasswordHistoryCore(
                        conn, tx, pwSql,
                        itemId: itemId,
                        pwCipher: pwCipher,
                        pwPadLen: pwPadLen,
                        pwFp: pwFp,
                        fpVersion: fpVersion);

                    if (newPwHistId <= 0)
                        throw new InvalidOperationException("PasswordHistory insert failed (no PwHistId returned)");

                    tx.Commit();
                }
                catch
                {
                    try { tx.Rollback(); } catch { }
                    throw;
                }

                return newPwHistId;
            }
            catch (DuplicatePasswordWarningException)
            {
                // UI decides Accept/Cancel, do NOT swallow
                throw;
            }
            catch (Exception ex)
            {
                ErrorHandler.Abend(ex, "Error inserting password history for existing item");
                return 0;
            }
            finally
            {
                // wipe buffers best-effort
                try { Array.Clear(pwFp, 0, pwFp.Length); } catch { }
                try { Array.Clear(pwCipher, 0, pwCipher.Length); } catch { }
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
        /// Expected:
        ///   @ItemId, @Version, @PasswordBlob, @PadLen, @PwFp
        /// Optional:
        ///   @FpVersion
        /// </summary>
        private static long InsertPasswordHistoryCore(
            SqliteConnection conn,
            SqliteTransaction tx,
            string sql,
            long itemId,
            byte[] pwCipher,
            int? pwPadLen,
            byte[] pwFp,
            int fpVersion)
        {
            if (itemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemId), "itemId must be > 0 for password history insert.");
            if (pwCipher is null)
                throw new ArgumentNullException(nameof(pwCipher));
            if (pwFp is null || pwFp.Length == 0)
                throw new ArgumentException("pwFp is required.", nameof(pwFp));
            if (fpVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(fpVersion), "fpVersion must be > 0.");

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;

            AddInt64(cmd, "@ItemId", itemId);

            // Let SQL COALESCE handle defaults by passing NULL
            AddInt32Nullable(cmd, "@Version", null);

            AddBlob(cmd, "@PasswordBlob", pwCipher);
            AddInt32Nullable(cmd, "@PadLen", pwPadLen);

            AddBlob(cmd, "@PwFp", pwFp);

            // drift-safe: only add if SQL actually uses it
            AddInt32IfSqlUses(cmd, sql, "@FpVersion", fpVersion);

#if DEBUG
            DebugDumpParams(cmd, "[CAT_ITEM][PW_HIST][INSERT][PARAMS]");
#endif

            var scalar = cmd.ExecuteScalar();
            if (scalar == null || scalar == DBNull.Value)
                throw new InvalidOperationException("PasswordHistory insert failed (no PwHistId returned)");

            return Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
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

        private static void AddInt64NullableIfSqlUses(SqliteCommand cmd, string sql, string name, long? value)
        {
            if (!SqlUses(sql, name)) return;
            AddInt64Nullable(cmd, name, value);
        }

        private static void AddInt64IfSqlUses(SqliteCommand cmd, string sql, string name, long value)
        {
            if (!SqlUses(sql, name)) return;
            AddInt64(cmd, name, value);
        }

        // ============================================================
        // Parameter primitives
        // ============================================================

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

        private static void AddInt64Nullable(SqliteCommand cmd, string name, long? value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.SqliteType = SqliteType.Integer;
            p.Value = (object?)value ?? DBNull.Value;
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
