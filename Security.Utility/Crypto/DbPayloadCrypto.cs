// File: Security.Utility/Crypto/Db/DpapiDbPayloadCrypto.cs
using System;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;
using Security.Utility.Crypto.Hash;

namespace Security.Utility.Crypto.Db
{
    /// <summary>
    /// Central DB I/O payload protection for CategoryItemPasswordHistory (TEMP v1).
    /// Uses Windows DPAPI (CurrentUser) to protect UTF-8 plaintext into byte[] suitable for DB storage.
    ///
    /// Notes:
    /// - Entropy string is NOT secret; it is a domain separator to prevent cross-purpose reuse.
    /// - Signature is SHA-256 over the ciphertext bytes for quick integrity checks / drift detection.
    /// - padLen is reserved for future padding strategies; v1 returns null.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class DpapiDbPayloadCrypto
    {
        // Domain separator (NOT a secret)
        public const string PasswordHistoryEntropyLabel = "MWPV:CIPaH:PW:v1";

        // Signature scheme version for DB column CIPaH_SigVersion (if you store it)
        public const int SigVersionV1 = 1;

        /// <summary>
        /// Protects a password for CategoryItemPasswordHistory.
        /// Bookmark-only => empty cipher + SHA256(empty) signature.
        /// Empty password => empty cipher + SHA256(empty) signature.
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
                pwCipher = ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser);
                pwSig = Sha256Common.Bytes(pwCipher);
            }
            finally
            {
                Zero(plain);
                Zero(entropy);
            }
        }

        /// <summary>
        /// Attempts to decrypt a stored CategoryItemPasswordHistory blob back into a UTF-8 string.
        /// Empty cipher => empty string.
        /// Returns false on decrypt failures (wrong user context, corrupted bytes, etc.).
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
