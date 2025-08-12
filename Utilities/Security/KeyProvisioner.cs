using System;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace Utilities.Security
{
    /// <summary>
    /// Central source of truth for application keys.
    /// Creates missing keys in the encrypted key archive and loads them into
    /// <see cref="SecureEncryptedDataStore"/> for the current session.
    ///
    /// <para><b>Keys managed</b></para>
    /// <list type="bullet">
    /// <item><description><c>DbPassword</c> (char[]) – existing DB encryption password (created elsewhere today; generated here only if missing)</description></item>
    /// <item><description><c>LogPayloadKey</c> (byte[32]) – AES-256 key for encrypting log payloads</description></item>
    /// <item><description><c>UserSecretsKey</c> (byte[32]) – AES-256 key for future sensitive user data</description></item>
    /// <item><description><c>KeySetVersion</c> (int) – increments whenever new keys are introduced</description></item>
    /// </list>
    ///
    /// <para><b>Persistence</b></para>
    /// Stored as UTF-8 JSON (e.g., <c>keyset.json</c>) inside the already-unlocked encrypted key archive.
    /// Callers provide delegates to read/write that blob so this class stays archive-agnostic.
    /// </summary>
    public static class KeyProvisioner
    {
        /// <summary>
        /// Ensures required keys exist in the key archive and are loaded into the secure store.
        /// Idempotent and safe to call after each successful keyfile unlock.
        /// </summary>
        /// <param name="loadKeyset">Returns the <c>keyset.json</c> bytes from the encrypted archive; return <c>null</c> or empty if missing.</param>
        /// <param name="saveKeyset">Persists <c>keyset.json</c> bytes back into the encrypted archive.</param>
        public static void EnsureKeySetLoaded(Func<byte[]> loadKeyset, Action<byte[]> saveKeyset)
        {
            byte[] jsonBytes = null;
            var keyset = new KeysetDto();

            try
            {
                // 1) Load current keyset (if present)
                jsonBytes = loadKeyset?.Invoke();
                if (jsonBytes != null && jsonBytes.Length > 0)
                {
                    var json = Encoding.UTF8.GetString(jsonBytes);
                    keyset = JsonSerializer.Deserialize<KeysetDto>(json) ?? new KeysetDto();
                }

                bool changed = false;

                // 2) Ensure DbPassword exists (first-run migration safety)
                if (string.IsNullOrEmpty(keyset.DbPassword))
                {
                    // NOTE: Today your first-run flow already creates DbPassword.
                    // We only generate here if it's missing (legacy/migration edge case).
                    keyset.DbPassword = SecurePassword.GenerateAsString(32);
                    changed = true;
                }

                // 3) Ensure new AES-256 keys exist
                if (keyset.LogPayloadKey == null || keyset.LogPayloadKey.Length != 32)
                {
                    keyset.LogPayloadKey = NewKey32();
                    changed = true;
                }
                if (keyset.UserSecretsKey == null || keyset.UserSecretsKey.Length != 32)
                {
                    keyset.UserSecretsKey = NewKey32();
                    changed = true;
                }

                // 4) Bump KeySetVersion when anything new appears (default to 1)
                if (changed)
                {
                    keyset.KeySetVersion = keyset.KeySetVersion <= 0 ? 1 : keyset.KeySetVersion + 1;

                    var json = JsonSerializer.Serialize(keyset);
                    var outBytes = Encoding.UTF8.GetBytes(json);
                    try { saveKeyset?.Invoke(outBytes); }
                    finally { SensitiveDataCleaner.WipeByteArray(ref outBytes); }
                }

                // 5) Load into session secure store (no duplicates)
                if (!SecureEncryptedDataStore.HasKey("DbPassword"))
                    SecureEncryptedDataStore.SetAndWipe("DbPassword", keyset.DbPassword.ToCharArray());

                if (!SecureEncryptedDataStore.HasKey("LogPayloadKey"))
                    SecureEncryptedDataStore.Set("LogPayloadKey", keyset.LogPayloadKey);

                if (!SecureEncryptedDataStore.HasKey("UserSecretsKey"))
                    SecureEncryptedDataStore.Set("UserSecretsKey", keyset.UserSecretsKey);

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

        /// <summary>JSON payload persisted inside the encrypted archive.</summary>
        private sealed class KeysetDto
        {
            public string DbPassword { get; set; }
            public byte[] LogPayloadKey { get; set; }
            public byte[] UserSecretsKey { get; set; }
            public int KeySetVersion { get; set; }

            public void Wipe()
            {
                if (LogPayloadKey != null)
                {
                    SensitiveDataCleaner.WipeByteArray(LogPayloadKey); // call central wipe
                    LogPayloadKey = null;
                }

                if (UserSecretsKey != null)
                {
                    SensitiveDataCleaner.WipeByteArray(UserSecretsKey); // call central wipe
                    UserSecretsKey = null;
                }

                if (!string.IsNullOrEmpty(DbPassword))
                {
                    var chars = DbPassword.ToCharArray();
                    SensitiveDataCleaner.WipeCharArray(chars); // call central wipe
                    DbPassword = null;
                }
            }
        }
    }
}
