// File: Security.Utility/Crypto/Fields/FieldAesCrypto.cs
//
// FULL REWRITE
//
// Portable field-level encryption (Layer 2) for DB payloads.
// - AES-GCM (nonce 12, tag 16)
// - Master key is loaded from SecureEncryptedDataStore (SEDS) at runtime
// - Derives a per-purpose subkey so email/phone/pin/passwordHistory don't share raw key bytes
// - Blob format:  [V=1][Nonce(12)][Tag(16)][Ciphertext...]
//
// IMPORTANT:
// - Caller must provide the same "purpose" on decrypt as on encrypt.
// - Caller MUST wipe returned byte[] / char[] buffers after use.
// - This class intentionally does NOT touch UI. It belongs in Security.Utility.
//
// CHANGE (2026-01-20):
// - Treat NULL/EMPTY blobs as a VALID "no value stored" state.
//   TryDecryptBytes/TryDecryptChars will return true with empty output.
//   Only malformed non-empty blobs will return false.

using System;
using System.Security.Cryptography;
using System.Text;
using Security.Utility.Storage;
using Security.Utility.Wiping;

namespace Security.Utility.Crypto.Fields
{
    public static class FieldAesCrypto
    {
        // ===== SEDS keys for master keys (store these as raw bytes in SEDS) =====
        // Keep names stable. Change only with a versioned migration plan.
        public const string SedsKey_UserSecretsKey = "UserSecretsKey"; // 32 bytes
        public const string SedsKey_LogPayloadKey = "LogPayloadKey";  // 32 bytes (optional, for logs)

        // ===== Blob constants =====
        private const byte BlobVersionV1 = 1;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int MinBlobLen = 1 + NonceSize + TagSize;

        // Domain separator for HKDF info (prevents cross-project reuse)
        private const string KdfDomain = "MWPV:FieldAesCrypto:v1";

        // ----------------------------
        // Public: Encrypt (bytes)
        // ----------------------------

        public static byte[] EncryptBytes(string masterKeySedsName, string purpose, byte[] plaintext)
        {
            if (string.IsNullOrWhiteSpace(masterKeySedsName)) throw new ArgumentException("Master key SEDS name is required.", nameof(masterKeySedsName));
            if (string.IsNullOrWhiteSpace(purpose)) throw new ArgumentException("Purpose is required.", nameof(purpose));
            if (plaintext is null) throw new ArgumentNullException(nameof(plaintext));

            byte[] master = GetRequiredKeyFromSeds(masterKeySedsName);
            byte[]? subkey = null;
            byte[]? aad = null;

            try
            {
                subkey = DeriveSubKey(master, purpose);      // 32 bytes
                aad = Encoding.UTF8.GetBytes(purpose);       // binds ciphertext to the logical purpose

                var nonce = RandomNumberGenerator.GetBytes(NonceSize);
                var tag = new byte[TagSize];
                var ciphertext = new byte[plaintext.Length];

                try
                {
                    using (var gcm = new AesGcm(subkey))
                    {
                        gcm.Encrypt(nonce, plaintext, ciphertext, tag, aad);
                    }

                    var blob = new byte[1 + NonceSize + TagSize + ciphertext.Length];
                    blob[0] = BlobVersionV1;
                    Buffer.BlockCopy(nonce, 0, blob, 1, NonceSize);
                    Buffer.BlockCopy(tag, 0, blob, 1 + NonceSize, TagSize);
                    Buffer.BlockCopy(ciphertext, 0, blob, 1 + NonceSize + TagSize, ciphertext.Length);

                    return blob;
                }
                finally
                {
                    Array.Clear(nonce, 0, nonce.Length);
                    Array.Clear(tag, 0, tag.Length);
                    Array.Clear(ciphertext, 0, ciphertext.Length);
                }
            }
            finally
            {
                Array.Clear(master, 0, master.Length);
                if (subkey is not null) Array.Clear(subkey, 0, subkey.Length);
                if (aad is not null) Array.Clear(aad, 0, aad.Length);
            }
        }

        // ----------------------------
        // Public: Decrypt (bytes)
        // ----------------------------

        public static bool TryDecryptBytes(string masterKeySedsName, string purpose, byte[]? blob, out byte[] plaintext)
        {
            plaintext = Array.Empty<byte>();

            if (string.IsNullOrWhiteSpace(masterKeySedsName)) return false;
            if (string.IsNullOrWhiteSpace(purpose)) return false;

            // IMPORTANT: NULL/EMPTY means "no value stored" (valid, not an error)
            if (blob is null || blob.Length == 0)
            {
                plaintext = Array.Empty<byte>();
                return true;
            }

            // Non-empty but too small => malformed/corrupt
            if (blob.Length < MinBlobLen) return false;
            if (blob[0] != BlobVersionV1) return false;

            byte[] master = GetRequiredKeyFromSeds(masterKeySedsName);
            byte[]? subkey = null;
            byte[]? aad = null;

            try
            {
                subkey = DeriveSubKey(master, purpose);
                aad = Encoding.UTF8.GetBytes(purpose);

                var nonce = new byte[NonceSize];
                var tag = new byte[TagSize];

                Buffer.BlockCopy(blob, 1, nonce, 0, NonceSize);
                Buffer.BlockCopy(blob, 1 + NonceSize, tag, 0, TagSize);

                int ctLen = blob.Length - (1 + NonceSize + TagSize);
                if (ctLen < 0) return false;

                var ciphertext = new byte[ctLen];
                Buffer.BlockCopy(blob, 1 + NonceSize + TagSize, ciphertext, 0, ctLen);

                var plain = new byte[ctLen];
                try
                {
                    using (var gcm = new AesGcm(subkey))
                    {
                        gcm.Decrypt(nonce, ciphertext, tag, plain, aad);
                    }

                    plaintext = plain; // caller must wipe
                    return true;
                }
                catch
                {
                    Array.Clear(plain, 0, plain.Length);
                    return false;
                }
                finally
                {
                    Array.Clear(nonce, 0, nonce.Length);
                    Array.Clear(tag, 0, tag.Length);
                    Array.Clear(ciphertext, 0, ciphertext.Length);
                }
            }
            finally
            {
                Array.Clear(master, 0, master.Length);
                if (subkey is not null) Array.Clear(subkey, 0, subkey.Length);
                if (aad is not null) Array.Clear(aad, 0, aad.Length);
            }
        }

        /// <summary>
        /// Attempts to decrypt bytes and returns a Security.Utility technical result.
        /// The result reports only what happened and how serious it is; it does not
        /// include message text, exception text, protected payloads, keys, passwords,
        /// sensitive paths, or caller actions.
        /// </summary>
        public static SecurityUtilityResult TryDecryptBytesResult(
            string masterKeySedsName,
            string purpose,
            byte[]? blob,
            out byte[] plaintext)
        {
            plaintext = Array.Empty<byte>();

            if (string.IsNullOrWhiteSpace(masterKeySedsName))
                return Result(SecurityUtilityReturnCode.InvalidInput, SecurityUtilityResultKind.Failure);

            if (string.IsNullOrWhiteSpace(purpose))
                return Result(SecurityUtilityReturnCode.InvalidInput, SecurityUtilityResultKind.Failure);

            if (blob is null || blob.Length == 0)
                return Result(SecurityUtilityReturnCode.Success, SecurityUtilityResultKind.Success);

            if (blob.Length < MinBlobLen || blob[0] != BlobVersionV1)
                return Result(SecurityUtilityReturnCode.ProtectedDataMalformed, SecurityUtilityResultKind.Abend);

            var keyResult = ValidateRequiredKeyForResult(masterKeySedsName);
            if (!keyResult.Succeeded)
                return keyResult;

            try
            {
                if (TryDecryptBytes(masterKeySedsName, purpose, blob, out plaintext))
                    return Result(SecurityUtilityReturnCode.Success, SecurityUtilityResultKind.Success);

                return Result(SecurityUtilityReturnCode.ProtectedDataDecryptFailed, SecurityUtilityResultKind.Abend);
            }
            catch (ArgumentException)
            {
                return Result(SecurityUtilityReturnCode.InvalidInput, SecurityUtilityResultKind.Failure);
            }
            catch (InvalidOperationException)
            {
                return Result(SecurityUtilityReturnCode.CryptoKeyMissing, SecurityUtilityResultKind.Failure);
            }
            catch (CryptographicException)
            {
                return Result(SecurityUtilityReturnCode.ProtectedDataDecryptFailed, SecurityUtilityResultKind.Abend);
            }
            catch
            {
                return Result(SecurityUtilityReturnCode.UnknownSecurityFailure, SecurityUtilityResultKind.Abend);
            }
        }

        // ----------------------------
        // Public: Encrypt (chars -> blob) and wipe input
        // ----------------------------

        public static byte[] EncryptCharsAndWipe(string masterKeySedsName, string purpose, char[] plaintextChars)
        {
            if (plaintextChars is null) throw new ArgumentNullException(nameof(plaintextChars));

            byte[]? utf8 = null;
            try
            {
                utf8 = Encoding.UTF8.GetBytes(plaintextChars);
                return EncryptBytes(masterKeySedsName, purpose, utf8);
            }
            finally
            {
                SensitiveDataCleaner.WipeCharArray(plaintextChars);
                if (utf8 is not null) Array.Clear(utf8, 0, utf8.Length);
            }
        }

        // ----------------------------
        // Public: Decrypt (blob -> chars)
        // ----------------------------

        public static bool TryDecryptChars(string masterKeySedsName, string purpose, byte[]? blob, out char[] chars)
        {
            chars = Array.Empty<char>();

            if (!TryDecryptBytes(masterKeySedsName, purpose, blob, out var plainBytes))
                return false;

            // IMPORTANT: empty plaintext is a valid "no value stored" state
            if (plainBytes.Length == 0)
            {
                chars = Array.Empty<char>();
                return true;
            }

            try
            {
                // UTF-8 decode into char[]
                var decoder = Encoding.UTF8.GetDecoder();
                int charCount = decoder.GetCharCount(plainBytes, 0, plainBytes.Length);
                var tmp = new char[charCount];
                decoder.GetChars(plainBytes, 0, plainBytes.Length, tmp, 0, flush: true);

                chars = tmp; // caller must wipe
                return true;
            }
            finally
            {
                Array.Clear(plainBytes, 0, plainBytes.Length);
            }
        }

        /// <summary>
        /// Attempts to decrypt UTF-8 character data and returns a Security.Utility
        /// technical result without returning message text or exception details.
        /// </summary>
        public static SecurityUtilityResult TryDecryptCharsResult(
            string masterKeySedsName,
            string purpose,
            byte[]? blob,
            out char[] chars)
        {
            chars = Array.Empty<char>();

            var result = TryDecryptBytesResult(masterKeySedsName, purpose, blob, out var plainBytes);
            if (!result.Succeeded)
                return result;

            if (plainBytes.Length == 0)
                return Result(SecurityUtilityReturnCode.Success, SecurityUtilityResultKind.Success);

            try
            {
                var decoder = Encoding.UTF8.GetDecoder();
                int charCount = decoder.GetCharCount(plainBytes, 0, plainBytes.Length);
                var tmp = new char[charCount];
                decoder.GetChars(plainBytes, 0, plainBytes.Length, tmp, 0, flush: true);

                chars = tmp;
                return Result(SecurityUtilityReturnCode.Success, SecurityUtilityResultKind.Success);
            }
            catch (ArgumentException)
            {
                return Result(SecurityUtilityReturnCode.ProtectedDataMalformed, SecurityUtilityResultKind.Abend);
            }
            finally
            {
                Array.Clear(plainBytes, 0, plainBytes.Length);
            }
        }

        // ==========================================================
        // Internals
        // ==========================================================

        private static byte[] GetRequiredKeyFromSeds(string sedsName)
        {
            // This returns a new buffer from SEDS (caller wipes it)
            if (!SecureEncryptedDataStore.TryGetBytes(sedsName, out var key) || key.Length == 0)
                throw new InvalidOperationException($"Missing AES master key in SEDS: '{sedsName}'.");

            if (key.Length != 32)
            {
                Array.Clear(key, 0, key.Length);
                throw new CryptographicException($"SEDS key '{sedsName}' must be 32 bytes (AES-256). Actual={key.Length}.");
            }

            return key;
        }

        private static SecurityUtilityResult ValidateRequiredKeyForResult(string sedsName)
        {
            if (!SecureEncryptedDataStore.TryGetBytes(sedsName, out var key) || key.Length == 0)
                return Result(SecurityUtilityReturnCode.CryptoKeyMissing, SecurityUtilityResultKind.Failure);

            try
            {
                return key.Length == 32
                    ? Result(SecurityUtilityReturnCode.Success, SecurityUtilityResultKind.Success)
                    : Result(SecurityUtilityReturnCode.CryptoKeyInvalid, SecurityUtilityResultKind.Abend);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        // HKDF-SHA256 (extract+expand) to derive a per-purpose 32-byte subkey from the master key.
        // This prevents "same key used everywhere" even though we store one master.
        private static byte[] DeriveSubKey(byte[] masterKey, string purpose)
        {
            // info = domain || purpose (UTF-8)
            byte[] info = Encoding.UTF8.GetBytes(KdfDomain + "|" + purpose);

            // HKDF extract with salt = zeros(32) (portable, deterministic)
            byte[] salt = new byte[32]; // zeros
            byte[] prk;
            using (var h = new HMACSHA256(salt))
                prk = h.ComputeHash(masterKey);

            // HKDF expand for 32 bytes
            byte[] okm = new byte[32];
            try
            {
                byte[] t = Array.Empty<byte>();
                int pos = 0;
                byte counter = 1;

                using var hmac = new HMACSHA256(prk);

                while (pos < okm.Length)
                {
                    // T(n) = HMAC(PRK, T(n-1) || info || counter)
                    byte[] input = new byte[t.Length + info.Length + 1];
                    Buffer.BlockCopy(t, 0, input, 0, t.Length);
                    Buffer.BlockCopy(info, 0, input, t.Length, info.Length);
                    input[input.Length - 1] = counter;

                    byte[] tn = hmac.ComputeHash(input);

                    int take = Math.Min(tn.Length, okm.Length - pos);
                    Buffer.BlockCopy(tn, 0, okm, pos, take);
                    pos += take;

                    Array.Clear(input, 0, input.Length);
                    if (t.Length != 0) Array.Clear(t, 0, t.Length);
                    t = tn; // next T(n-1)
                    counter++;
                }

                return okm;
            }
            finally
            {
                Array.Clear(info, 0, info.Length);
                Array.Clear(salt, 0, salt.Length);
                Array.Clear(prk, 0, prk.Length);
            }
        }

        private static SecurityUtilityResult Result(
            SecurityUtilityReturnCode code,
            SecurityUtilityResultKind kind)
            => new()
            {
                Code = code,
                Kind = kind
            };
    }
}
