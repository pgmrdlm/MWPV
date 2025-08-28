using System;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace Security.Utility
{
    /// <summary>
    /// Central source of truth for application keys.
    /// Creates any missing keys in the encrypted key archive and loads them into
    /// SecureEncryptedDataStore for the current session. Idempotent.
    ///
    /// Keys:
    ///   - DbPasswordHex     (string hex of 32-byte key)
    ///   - LogPayloadKey     (byte[32])
    ///   - UserSecretsKey    (byte[32])
    ///   - KeySetVersion     (int)
    ///
    /// Storage: UTF-8 JSON (keyset.json) inside the already-unlocked encrypted key archive.
    /// Callers provide load/save delegates so this class stays archive-agnostic.
    /// </summary>
    public static class KeyProvisioner
    {
        /// <summary>
        /// Ensures required keys exist in the key archive and are loaded into the secure store.
        /// Safe to call after each successful keyfile unlock.
        /// </summary>
        /// <param name="loadKeyset">Return keyset.json bytes (or null/empty if missing).</param>
        /// <param name="saveKeyset">Persist keyset.json bytes back into the archive.</param>
        public static void EnsureKeySetLoaded(Func<byte[]> loadKeyset, Action<byte[]> saveKeyset)
        {
            byte[]? jsonBytes = null;
            var keyset = new KeysetDto();

            try
            {
                // 1) Load current keyset (if present)
                jsonBytes = loadKeyset?.Invoke();
                if (jsonBytes is { Length: > 0 })
                {
                    string json = Encoding.UTF8.GetString(jsonBytes);
                    keyset = JsonSerializer.Deserialize<KeysetDto>(json) ?? new KeysetDto();
                }

                bool changed = false;

                // 2) Ensure DbPasswordHex exists (first-run / migration safety)
                if (string.IsNullOrWhiteSpace(keyset.DbPasswordHex))
                {
                    var dbKey = NewKey32();
                    try { keyset.DbPasswordHex = ToHexLower(dbKey); }
                    finally { SensitiveDataCleaner.WipeByteArray(ref dbKey); }
                    changed = true;
                }

                // 3) Ensure AES-256 keys exist
                if (keyset.LogPayloadKey is not { Length: 32 })
                {
                    keyset.LogPayloadKey = NewKey32();
                    changed = true;
                }
                if (keyset.UserSecretsKey is not { Length: 32 })
                {
                    keyset.UserSecretsKey = NewKey32();
                    changed = true;
                }

                // 4) Persist if changed
                if (changed)
                {
                    keyset.KeySetVersion = keyset.KeySetVersion <= 0 ? 1 : keyset.KeySetVersion + 1;

                    string jsonOut = JsonSerializer.Serialize(keyset);
                    byte[] outBytes = Encoding.UTF8.GetBytes(jsonOut);
                    try { saveKeyset?.Invoke(outBytes); }
                    finally { SensitiveDataCleaner.WipeByteArray(ref outBytes); }
                }

                // 5) Load into SEDS (write both new names and legacy aliases)

                // ----- DB key (hex + bytes) -----
                if (!SecureEncryptedDataStore.HasKey("Key.DbPassword.Hex"))
                {
                    var hexChars = keyset.DbPasswordHex.ToCharArray();
                    SecureEncryptedDataStore.SetAndWipe("Key.DbPassword.Hex", hexChars);
                }

                if (!SecureEncryptedDataStore.HasKey("Key.DbPassword.Bytes"))
                {
                    byte[] dbBytes = FromHex(keyset.DbPasswordHex);
                    try { SecureEncryptedDataStore.Set("Key.DbPassword.Bytes", dbBytes); }
                    finally { SensitiveDataCleaner.WipeByteArray(ref dbBytes); }
                }

                // Legacy alias for older code that fetched "DbPassword" as chars
                if (!SecureEncryptedDataStore.HasKey("DbPassword"))
                {
                    var hexChars = keyset.DbPasswordHex.ToCharArray();
                    SecureEncryptedDataStore.SetAndWipe("DbPassword", hexChars);
                }

                // ----- Log key -----
                if (!SecureEncryptedDataStore.HasKey("Key.LogPayloadKey"))
                    SecureEncryptedDataStore.Set("Key.LogPayloadKey", keyset.LogPayloadKey!);

                // legacy alias
                if (!SecureEncryptedDataStore.HasKey("LogPayloadKey"))
                    SecureEncryptedDataStore.Set("LogPayloadKey", keyset.LogPayloadKey!);

                // ----- User secrets key -----
                if (!SecureEncryptedDataStore.HasKey("Key.UserSecretsKey"))
                    SecureEncryptedDataStore.Set("Key.UserSecretsKey", keyset.UserSecretsKey!);

                // legacy alias
                if (!SecureEncryptedDataStore.HasKey("UserSecretsKey"))
                    SecureEncryptedDataStore.Set("UserSecretsKey", keyset.UserSecretsKey!);

                SecureEncryptedDataStore.SetString("KeySetVersion", keyset.KeySetVersion.ToString());
            }
            finally
            {
                if (jsonBytes != null) SensitiveDataCleaner.WipeByteArray(ref jsonBytes);
                keyset?.Wipe();
            }
        }

        private static byte[] NewKey32()
        {
            var k = new byte[32];
            RandomNumberGenerator.Fill(k);
            return k;
        }

        private static string ToHexLower(ReadOnlySpan<byte> bytes)
        {
            char[] c = new char[bytes.Length * 2];
            int idx = 0;
            foreach (byte b in bytes)
            {
                c[idx++] = GetHexLower(b >> 4);
                c[idx++] = GetHexLower(b & 0xF);
            }
            var s = new string(c);
            Array.Clear(c, 0, c.Length);
            return s;

            static char GetHexLower(int v) => (char)(v < 10 ? ('0' + v) : ('a' + (v - 10)));
        }

        private static byte[] FromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return Array.Empty<byte>();
            if ((hex.Length & 1) == 1) throw new ArgumentException("Hex string must have even length.", nameof(hex));

            int len = hex.Length / 2;
            var bytes = new byte[len];
            for (int i = 0; i < len; i++)
            {
                int hi = ParseNibble(hex[2 * i]) << 4;
                int lo = ParseNibble(hex[2 * i + 1]);
                bytes[i] = (byte)(hi | lo);
            }
            return bytes;

            static int ParseNibble(char c) =>
                c switch
                {
                    >= '0' and <= '9' => c - '0',
                    >= 'a' and <= 'f' => c - 'a' + 10,
                    >= 'A' and <= 'F' => c - 'A' + 10,
                    _ => throw new ArgumentException("Invalid hex character.")
                };
        }

        /// <summary>DTO persisted inside the encrypted archive (JSON).</summary>
        private sealed class KeysetDto
        {
            public string DbPasswordHex { get; set; } = "";
            public byte[]? LogPayloadKey { get; set; }
            public byte[]? UserSecretsKey { get; set; }
            public int KeySetVersion { get; set; }

            public void Wipe()
            {
                if (LogPayloadKey != null)
                {
                    SensitiveDataCleaner.WipeByteArray(LogPayloadKey);
                    LogPayloadKey = null;
                }

                if (UserSecretsKey != null)
                {
                    SensitiveDataCleaner.WipeByteArray(UserSecretsKey);
                    UserSecretsKey = null;
                }

                if (!string.IsNullOrEmpty(DbPasswordHex))
                {
                    var chars = DbPasswordHex.ToCharArray();
                    SensitiveDataCleaner.WipeCharArray(chars);
                    DbPasswordHex = "";
                }
            }
        }
    }
}
