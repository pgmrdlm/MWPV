// File: Security.Utility/Crypto/Passwords/PasswordFingerprint.cs
//
// PURPOSE
// - Computes a stable, secret-keyed fingerprint for a password that can be compared across
//   different encryptions (AES-GCM uses random nonce, so ciphertext changes each time).
// - Intended for "password reuse" checks (same item history) without decrypting history rows.
//
// DESIGN
// - Fingerprint = HMAC-SHA256(secretKey, purpose || 0x00 || UTF8(password))
// - Output is 32 bytes.
// - "purpose" is a domain separator so this key is not reused ambiguously.
//
// NOTES
// - secretKey is NOT stored here; caller supplies it (likely from key file / SEDS).
// - This file is UI-agnostic (no WPF types).
// - Best-effort wiping of temporary buffers.
//

using System;
using System.Security.Cryptography;
using System.Text;

namespace Security.Utility.Crypto.Password
{
    /// <summary>
    /// Stable, keyed password fingerprint helper.
    /// Use this when we need equality checks across time even though encryption is randomized.
    /// </summary>
    public static class PasswordFingerprint
    {
        /// <summary>
        /// Domain separator (purpose) used when computing fingerprints.
        /// Keep stable forever once shipped.
        /// </summary>
        public const string DefaultPurpose = "PW.Fingerprint.V1";

        /// <summary>
        /// Computes a 32-byte HMAC-SHA256 fingerprint:
        ///   HMAC(key, purpose || 0x00 || UTF8(password))
        /// </summary>
        public static byte[] Compute(string? passwordPlain, ReadOnlySpan<byte> secretKey, string? purpose = null)
        {
            purpose ??= DefaultPurpose;

            if (secretKey.IsEmpty)
                throw new ArgumentException("secretKey must be non-empty.", nameof(secretKey));
            if (string.IsNullOrWhiteSpace(purpose))
                throw new ArgumentException("purpose is required.", nameof(purpose));

            // Normalize: treat null as empty string (still produces a deterministic fingerprint).
            string pw = passwordPlain ?? string.Empty;

            byte[] purposeBytes = Encoding.UTF8.GetBytes(purpose);
            byte[] pwBytes = Encoding.UTF8.GetBytes(pw);

            // Compose message: purpose + 0x00 + pwBytes
            byte[] msg = new byte[purposeBytes.Length + 1 + pwBytes.Length];

            try
            {
                Buffer.BlockCopy(purposeBytes, 0, msg, 0, purposeBytes.Length);
                msg[purposeBytes.Length] = 0x00;
                Buffer.BlockCopy(pwBytes, 0, msg, purposeBytes.Length + 1, pwBytes.Length);

                using var hmac = new HMACSHA256(secretKey.ToArray());
                return hmac.ComputeHash(msg);
            }
            finally
            {
                // Best-effort wipe temp buffers.
                Array.Clear(purposeBytes, 0, purposeBytes.Length);
                Array.Clear(pwBytes, 0, pwBytes.Length);
                Array.Clear(msg, 0, msg.Length);
            }
        }

        /// <summary>
        /// Constant-time compare for fingerprints.
        /// </summary>
        public static bool FixedTimeEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
            => CryptographicOperations.FixedTimeEquals(a, b);
    }
}
