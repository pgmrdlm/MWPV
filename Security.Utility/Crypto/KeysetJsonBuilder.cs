using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.Json;
// File: Crypto/KeyArchiveVerifier.cs (example)
using Security.Utility.Storage;   // SEDS lives here
using Security.Utility.Wiping;    // SensitiveDataCleaner lives here

namespace Security.Utility.Crypto
{
    /// <summary>
    /// Builds v2 keyset JSON (everything in one file).
    /// </summary>
    public static class KeysetJsonBuilder
    {
        public static string BuildV2(
            char[] dbPassword,                           // plaintext chars
            ReadOnlySpan<byte> logPayloadKey,            // 32 bytes recommended
            ReadOnlySpan<byte> userSecretsKey,           // 32 bytes recommended
            IReadOnlyDictionary<string, string> sqlMap,  // filename -> SQL text
            string? appVersionOverride = null)
        {
            if (dbPassword == null || dbPassword.Length == 0)
                throw new ArgumentException("dbPassword required.", nameof(dbPassword));
            if (sqlMap == null) throw new ArgumentNullException(nameof(sqlMap));

            byte[]? pwUtf8 = null;
            try
            {
                pwUtf8 = Encoding.UTF8.GetBytes(dbPassword);

                var ks = new KeysetV2
                {
                    keySetVersion = 2,
                    createdUtc = DateTime.UtcNow.ToString("o"),
                    meta = new Meta
                    {
                        archiveId = Guid.NewGuid().ToString("D"),
                        appVersion = appVersionOverride ?? (Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString() ?? "0.0.0")
                    },
                    secrets = new Secrets
                    {
                        dbPassword = Convert.ToBase64String(pwUtf8),
                        logPayloadKey = Convert.ToBase64String(logPayloadKey.ToArray()),
                        userSecretsKey = Convert.ToBase64String(userSecretsKey.ToArray())
                    },
                    sql = new Dictionary<string, string>(sqlMap, StringComparer.Ordinal)
                };

                return JsonSerializer.Serialize(ks, new JsonSerializerOptions { WriteIndented = true });
            }
            finally
            {
                if (pwUtf8 != null) Array.Clear(pwUtf8, 0, pwUtf8.Length);
            }
        }
    }
}
