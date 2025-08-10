using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Utilities.Security
{
    /// <summary>
    /// In-memory encrypted key/value store for sensitive data.
    /// - Stores ciphertext only (IV || CIPHERTEXT per entry).
    /// - Returns plaintext as byte[] or char[] (caller must wipe).
    /// - No app/business logic (DB handling lives in DatabaseHelper).
    /// </summary>
    public static class SecureEncryptedDataStore
    {
        // Session-scoped AES-256 key
        private static readonly byte[] Key;

        // In-memory ciphertext: value = IV||CIPHERTEXT
        private static readonly Dictionary<string, byte[]> _store = new();

        static SecureEncryptedDataStore()
        {
            Key = new byte[32];
            RandomNumberGenerator.Fill(Key);
        }

        // =========================
        //            SET
        // =========================

        /// <summary>
        /// Store from char[] and WIPE the caller's buffer afterward (safest default).
        /// </summary>
        public static void SetAndWipe(string key, char[] plainChars)
        {
            if (plainChars == null) throw new ArgumentNullException(nameof(plainChars));

            byte[] utf8 = null;
            try
            {
                utf8 = Encoding.UTF8.GetBytes(plainChars);
                SetBytesInternal(key, utf8);
            }
            finally
            {
                SensitiveDataCleaner.WipeCharArray(plainChars);
                WipeByteArray(utf8);
            }
        }

        /// <summary>
        /// Store from char[] WITHOUT wiping the caller's buffer.
        /// Use only if you truly need to keep the original char[] momentarily.
        /// </summary>
        public static void SetNoWipe(string key, char[] plainChars)
        {
            if (plainChars == null) throw new ArgumentNullException(nameof(plainChars));

            byte[] utf8 = null;
            try
            {
                utf8 = Encoding.UTF8.GetBytes(plainChars);
                SetBytesInternal(key, utf8);
            }
            finally
            {
                WipeByteArray(utf8);
            }
        }

        /// <summary>
        /// Store from byte[]. Caller retains control of wiping their input buffer.
        /// </summary>
        public static void Set(string key, byte[] plainBytes)
        {
            if (plainBytes == null) throw new ArgumentNullException(nameof(plainBytes));
            SetBytesInternal(key, plainBytes);
        }

        private static void SetBytesInternal(string key, byte[] plainBytes)
        {
            // Encrypt with a fresh random IV per entry
            var combined = EncryptWithRandomIv(plainBytes);

            // Defensive copy into store
            var copy = new byte[combined.Length];
            Buffer.BlockCopy(combined, 0, copy, 0, combined.Length);

            lock (_store)
            {
                if (_store.TryGetValue(key, out var existing))
                {
                    WipeByteArray(existing);
                }
                _store[key] = copy;
            }

            // Wipe our temporary combined buffer
            WipeByteArray(combined);
        }

        // =========================
        //      NON-SENSITIVE
        //        HELPERS
        // =========================

        /// <summary>
        /// Convenience: store a NON-SENSITIVE string (e.g., file paths, SQL scripts).
        /// </summary>
        public static void SetString(string key, string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var bytes = Encoding.UTF8.GetBytes(value);
            try
            {
                Set(key, bytes);
            }
            finally
            {
                WipeByteArray(bytes); // string itself can't be wiped
            }
        }

        /// <summary>
        /// Convenience: retrieve a NON-SENSITIVE string.
        /// </summary>
        public static string GetString(string key)
        {
            var bytes = GetBytes(key);
            try
            {
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                WipeByteArray(bytes);
            }
        }

        // =========================
        //            GET
        // =========================

        /// <summary>
        /// Get plaintext bytes. Caller MUST wipe the returned buffer after use.
        /// </summary>
        public static byte[] GetBytes(string key)
        {
            byte[] combined;
            lock (_store)
            {
                if (!_store.TryGetValue(key, out combined))
                    throw new KeyNotFoundException($"Key not found: {key}");
            }

            // Decrypt (creates a new plaintext buffer)
            return DecryptWithEmbeddedIv(combined);
        }

        /// <summary>
        /// Get plaintext chars (UTF-8). Caller MUST wipe the returned buffer after use.
        /// </summary>
        public static char[] GetChars(string key)
        {
            var bytes = GetBytes(key);
            try
            {
                var decoder = Encoding.UTF8.GetDecoder();
                int charCount = decoder.GetCharCount(bytes, 0, bytes.Length);
                var chars = new char[charCount];
                decoder.GetChars(bytes, 0, bytes.Length, chars, 0, flush: true);
                return chars;
            }
            finally
            {
                WipeByteArray(bytes);
            }
        }

        // =========================
        //       HOUSEKEEPING
        // =========================

        public static bool HasKey(string key)
        {
            lock (_store) { return _store.ContainsKey(key); }
        }

        public static void Clear(string key)
        {
            lock (_store)
            {
                if (_store.TryGetValue(key, out var combined))
                {
                    WipeByteArray(combined);
                    _store.Remove(key);
                }
            }
        }

        public static void ClearAll()
        {
            lock (_store)
            {
                foreach (var kvp in _store)
                {
                    WipeByteArray(kvp.Value);
                }
                _store.Clear();
            }
        }

        public static IEnumerable<string> Keys()
        {
            lock (_store)
            {
                return new List<string>(_store.Keys);
            }
        }

        // =========================
        //     CRYPTO PRIMITIVES
        // =========================

        // Returns IV||CIPHERTEXT
        private static byte[] EncryptWithRandomIv(byte[] plain)
        {
            using var aes = Aes.Create();
            aes.Key = Key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            aes.GenerateIV();
            var iv = aes.IV;

            using var ms = new MemoryStream();
            // prepend IV
            ms.Write(iv, 0, iv.Length);

            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(aes.Key, iv), CryptoStreamMode.Write))
            {
                cs.Write(plain, 0, plain.Length);
            }

            return ms.ToArray(); // [IV(16)] + [CIPHERTEXT]
        }

        private static byte[] DecryptWithEmbeddedIv(byte[] combined)
        {
            if (combined == null || combined.Length < 16)
                throw new CryptographicException("Ciphertext is too short.");

            var iv = new byte[16];
            Buffer.BlockCopy(combined, 0, iv, 0, 16);

            using var aes = Aes.Create();
            aes.Key = Key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var ms = new MemoryStream(combined, 16, combined.Length - 16);
            using var cs = new CryptoStream(ms, aes.CreateDecryptor(aes.Key, iv), CryptoStreamMode.Read);
            using var outMs = new MemoryStream();
            cs.CopyTo(outMs);
            return outMs.ToArray();
        }

        // =========================
        //        WIPE HELPERS
        // =========================

        private static void WipeByteArray(byte[] data)
        {
            if (data == null) return;
            Array.Clear(data, 0, data.Length);
        }
    }
}
