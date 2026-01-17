// File: Security.Utility/Crypto/Password/SensitiveValueSignature.cs
//
// FULL REWRITE (tightened + uses SensitiveDataCleaner for wiping)
//
// PURPOSE
// - Computes a stable, secret-keyed fingerprint for a password that can be compared across
//   different encryptions (AES-GCM uses random nonce, so ciphertext changes each time).
// - Intended for password reuse checks without decrypting history rows.
//
// DESIGN
// - Fingerprint = HMAC-SHA256(secretKey, purpose || 0x00 || UTF8(password))
// - Output is 32 bytes.
// - "purpose" is a domain separator so this key is not reused ambiguously.
//
// NOTES
// - secretKey is NOT stored here; caller supplies it (from key file / SEDS).
// - Best-effort wiping of temporary buffers (byte arrays + key copy).
// - We cannot wipe the original C# string (immutable), but we wipe derived buffers.
//

using System;
using System.Security.Cryptography;
using System.Text;
using Security.Utility.Wiping;

namespace Security.Utility.Crypto.Signatures
{
    /// <summary>
    /// Stable, keyed password fingerprint helper.
    /// Use this when we need equality checks across time even though encryption is randomized.
    /// </summary>
    public static class SensitiveValueSignature
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

            // Normalize: treat null as empty string (still deterministic).
            string pw = passwordPlain ?? string.Empty;

            byte[]? keyCopy = null;
            byte[]? purposeBytes = null;
            byte[]? pwBytes = null;

            try
            {
                // NOTE: HMACSHA256 requires a byte[] key. We copy so we can wipe it after.
                keyCopy = secretKey.ToArray();

                purposeBytes = Encoding.UTF8.GetBytes(purpose);
                pwBytes = Encoding.UTF8.GetBytes(pw);

                using var hmac = new HMACSHA256(keyCopy);

                // Avoid allocating a combined message buffer:
                // Feed purpose, then separator, then password bytes.
                hmac.TransformBlock(purposeBytes, 0, purposeBytes.Length, null, 0);
                hmac.TransformBlock(new byte[] { 0x00 }, 0, 1, null, 0);
                hmac.TransformFinalBlock(pwBytes, 0, pwBytes.Length);

                // HMACSHA256.Hash is 32 bytes.
                // Copy it so we return a standalone array not tied to the HMAC object.
                return hmac.Hash!.ToArray();
            }
            finally
            {
                // Best-effort wipe temp buffers and key material.
                SensitiveDataCleaner.Zero(purposeBytes);
                SensitiveDataCleaner.Zero(pwBytes);
                SensitiveDataCleaner.Zero(keyCopy);
            }
        }

        /// <summary>
        /// Constant-time compare for fingerprints.
        /// </summary>
        public static bool FixedTimeEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
            => CryptographicOperations.FixedTimeEquals(a, b);
    }
}
