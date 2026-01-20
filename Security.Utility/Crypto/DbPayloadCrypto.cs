// File: Security.Utility/Crypto/Db/DpapiDbPayloadCrypto.cs
//
// FULL REWRITE
//
// IMPORTANT WARNING (READ THIS FIRST):
// -----------------------------------
// This class uses Windows DPAPI with DataProtectionScope.CurrentUser.
//
// That means:
// - Encrypted blobs produced here are ONLY decryptable on the SAME Windows machine
//   and under the SAME Windows user profile that created them.
// - If the database is copied to a different Windows machine, DPAPI decryption WILL FAIL.
// - If the Windows install/user profile is lost/corrupted, DPAPI decryption WILL FAIL.
// - If the machine dies, the DB data protected by this class is effectively LOST.
// - Therefore: THIS MUST NEVER BE USED for any database that might ever be moved,
//   restored, migrated, or opened on a different Windows machine.
//
// Allowed usage:
// - Machine-bound, non-portable data ONLY.
// - ELOG-style "local machine only" scenarios where portability is explicitly NOT supported.
//
// NOT allowed usage:
// - Any portable vault DB
// - Any DB that may be backed up and restored to another computer
// - Any DB intended to survive hardware replacement
//
// For portable DB encryption (cross-machine), use AES-based field encryption instead
// (ex: FieldAesCrypto) with keys derived from the user's key archive.
//
// ---------------------------------------------------------------------------

using System;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;
using Security.Utility.Crypto.Hash;

namespace Security.Utility.Crypto.Db
{
    /// <summary>
    /// DPAPI-based payload protection (Windows-only, machine/user-bound).
    ///
    /// This class protects UTF-8 plaintext into byte[] suitable for DB storage using:
    ///   ProtectedData.Protect(..., DataProtectionScope.CurrentUser)
    ///
    /// SECURITY / PORTABILITY WARNING:
    /// - This encryption is NOT portable.
    /// - Ciphertext can ONLY be decrypted on the SAME machine + SAME Windows user.
    /// - If the DB ever needs to move to a different computer, this MUST NOT be used.
    ///
    /// Notes:
    /// - Entropy string is NOT secret; it is a domain separator to prevent cross-purpose reuse.
    /// - Signature is SHA-256 over ciphertext bytes for quick integrity checks / drift detection.
    /// - padLen is reserved for future padding strategies; v1 returns null.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class DpapiDbPayloadCrypto
    {
        /// <summary>
        /// Domain separator (NOT a secret).
        /// This must remain stable for decryption to work.
        /// </summary>
        public const string PasswordHistoryEntropyLabel = "MWPV:CIPaH:PW:v1";

        /// <summary>
        /// Signature scheme version for DB column CIPaH_SigVersion (if stored).
        /// </summary>
        public const int SigVersionV1 = 1;

        /// <summary>
        /// Protects a password for machine/user-bound DB storage.
        ///
        /// Bookmark-only => empty cipher + SHA256(empty) signature.
        /// Empty password => empty cipher + SHA256(empty) signature.
        ///
        /// WARNING:
        /// DPAPI(CurrentUser) output is NOT portable across machines/users.
        /// </summary>
        public static void ProtectPasswordHistory(
            string? password,
            bool isBookmarkOnly,
            out byte[] pwCipher,
            out int? padLen,
            out byte[] pwSig,
            out int sigVersion)
        {
            padLen = null;
            sigVersion = SigVersionV1;

            if (isBookmarkOnly)
            {
                pwCipher = Array.Empty<byte>();
                pwSig = Sha256Common.Bytes(pwCipher);
                return;
            }

            var pw = password ?? string.Empty;
            if (pw.Length == 0)
            {
                pwCipher = Array.Empty<byte>();
                pwSig = Sha256Common.Bytes(pwCipher);
                return;
            }

            byte[] plain = Encoding.UTF8.GetBytes(pw);
            byte[] entropy = Encoding.UTF8.GetBytes(PasswordHistoryEntropyLabel);

            try
            {
                // DPAPI(CurrentUser) = SAME MACHINE + SAME WINDOWS USER only
                pwCipher = ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser);

                // Signature over ciphertext bytes (integrity/drift check)
                pwSig = Sha256Common.Bytes(pwCipher);
            }
            finally
            {
                Zero(plain);
                Zero(entropy);
            }
        }

        /// <summary>
        /// Attempts to decrypt a stored DPAPI-protected blob back into a UTF-8 string.
        ///
        /// Empty cipher => empty string.
        /// Returns false on decrypt failures (wrong machine/user context, corrupted bytes, etc.).
        ///
        /// WARNING:
        /// DPAPI(CurrentUser) decryption will FAIL if DB is moved to another machine/user.
        /// </summary>
        public static bool TryUnprotectPasswordHistory(byte[]? pwCipher, out string? password)
        {
            password = null;

            if (pwCipher is null || pwCipher.Length == 0)
            {
                password = string.Empty;
                return true;
            }

            byte[] entropy = Encoding.UTF8.GetBytes(PasswordHistoryEntropyLabel);
            byte[]? plain = null;

            try
            {
                // DPAPI(CurrentUser) = SAME MACHINE + SAME WINDOWS USER only
                plain = ProtectedData.Unprotect(pwCipher, entropy, DataProtectionScope.CurrentUser);

                password = Encoding.UTF8.GetString(plain);
                return true;
            }
            catch
            {
                password = null;
                return false;
            }
            finally
            {
                Zero(entropy);
                if (plain is not null) Zero(plain);
            }
        }

        private static void Zero(byte[]? bytes)
        {
            if (bytes is null || bytes.Length == 0) return;
            Array.Clear(bytes, 0, bytes.Length);
        }
    }
}
