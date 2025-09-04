using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
// File: Crypto/KeyArchiveVerifier.cs (example)
using Security.Utility.Storage;   // SEDS lives here
using Security.Utility.Wiping;    // SensitiveDataCleaner lives here

namespace Security.Utility.Crypto
{
    // POCOs for the v2 keyset format (everything lives in keyset.json).
    // {
    //   "keySetVersion": 2,
    //   "createdUtc": "2025-08-30T18:03:21.123Z",
    //   "meta": { "archiveId": "GUID", "appVersion": "1.0.0" },
    //   "secrets": {
    //     "dbPassword": "base64(UTF8)", "logPayloadKey": "base64", "userSecretsKey": "base64"
    //   },
    //   "sql": { "File.sql": "...\n" }
    // }
    public sealed class KeysetV2
    {
        public int keySetVersion { get; set; } = 2;
        public string createdUtc { get; set; } = DateTime.UtcNow.ToString("o");
        public Meta meta { get; set; } = new Meta();
        public Secrets secrets { get; set; } = new Secrets();
        public Dictionary<string, string> sql { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public sealed class Meta
    {
        public string archiveId { get; set; } = Guid.NewGuid().ToString("D");
        public string appVersion { get; set; } = "0.0.0";
    }

    public sealed class Secrets
    {
        // dbPassword is base64(UTF8 of the password). This is NOT extra crypto; rely on archive encryption.
        public string dbPassword { get; set; } = "";
        public string logPayloadKey { get; set; } = "";
        public string userSecretsKey { get; set; } = "";
    }

    public static class KeysetJsonV2
    {
        public static KeysetV2 Deserialize(string json)
        {
            var ks = JsonSerializer.Deserialize<KeysetV2>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) ?? throw new InvalidOperationException("Invalid keyset.json");

            Validate(ks);
            return ks;
        }

        public static void Validate(KeysetV2 ks, IReadOnlyList<string>? mustHaveSql = null)
        {
            if (ks.keySetVersion != 2) throw new InvalidOperationException("Unsupported keySetVersion.");
            if (ks.secrets == null) throw new InvalidOperationException("Missing secrets.");
            if (string.IsNullOrWhiteSpace(ks.secrets.dbPassword)) throw new InvalidOperationException("Missing dbPassword.");
            if (ks.sql == null) throw new InvalidOperationException("Missing sql section.");

            if (mustHaveSql != null)
            {
                foreach (var name in mustHaveSql)
                {
                    if (!ks.sql.ContainsKey(name) || string.IsNullOrWhiteSpace(ks.sql[name]))
                        throw new InvalidOperationException($"Missing required SQL: {name}");
                }
            }
        }

        public static char[] DecodeDbPasswordToChars(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return Array.Empty<char>();
            byte[]? bytes = null;
            try
            {
                bytes = Convert.FromBase64String(base64);
                return Encoding.UTF8.GetChars(bytes);
            }
            finally
            {
                if (bytes != null) Array.Clear(bytes, 0, bytes.Length);
            }
        }
    }
}
