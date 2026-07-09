using System;
using System.Text;
using System.Text.Json;
using Security.Utility.Wiping;   // SensitiveDataCleaner
using Security.Utility.Crypto;   // KeysetJsonV2

namespace Security.Utility.Crypto
{
    public static partial class KeyProvisioner
    {
        /// <summary>
        /// Read-only integrity check for keyset.json.
        /// - Loads raw bytes via the provided delegate (archive must be unlocked by caller).
        /// - Sequentially parses the whole JSON (Utf8JsonReader) to ensure it’s well-formed end-to-end.
        /// - Deserializes using the real runtime schema (KeysetJsonV2).
        /// - Validates base64 for secrets.dbPassword (required) and verifies optional 32-byte keys if present.
        /// - Does NOT write to SEDS or to the archive.
        /// Returns true iff all checks pass; false otherwise.
        /// </summary>
        public static bool ValidateKeysetJson(Func<byte[]> loadKeyset)
        {
            byte[]? jsonBytes = null;

            try
            {
                // 1) Load raw bytes
                jsonBytes = loadKeyset?.Invoke();
                if (jsonBytes is not { Length: > 0 })
                    return false;

                // 2) Forward-only scan to confirm the entire payload is valid JSON
                //    (ensures we don't accept truncated or trailing-junk content)
                var reader = new Utf8JsonReader(jsonBytes, isFinalBlock: true, state: default);
                while (reader.Read()) { /* consume */ }
                if (reader.BytesConsumed != jsonBytes.Length)
                    return false;

                // 3) Deserialize using the actual schema used at runtime
                var json = Encoding.UTF8.GetString(jsonBytes);
                var ks = KeysetJsonV2.Deserialize(json);   // throws on schema/shape problems

                // 4) Validate required secret: dbPassword (must be valid base64 and decode)
                var dbPwChars = KeysetJsonV2.DecodeDbPasswordToChars(ks.secrets.dbPassword); // throws on bad base64
                try
                {
                    if (dbPwChars == null || dbPwChars.Length == 0)
                        return false;
                }
                finally
                {
                    if (dbPwChars != null) Array.Clear(dbPwChars, 0, dbPwChars.Length);
                }

                // 5) If these are present in your V2 schema as base64 strings, verify they decode to 32 bytes.
                //    (If your schema stores them differently, adjust or remove these checks.)
                if (!string.IsNullOrWhiteSpace(ks.secrets.logPayloadKey))
                {
                    byte[]? k = null;
                    try
                    {
                        k = Convert.FromBase64String(ks.secrets.logPayloadKey); // throws on bad base64
                        if (k.Length != 32) return false;
                    }
                    finally
                    {
                        SensitiveDataCleaner.WipeByteArray(ref k);
                    }
                }
                if (!string.IsNullOrWhiteSpace(ks.secrets.userSecretsKey))
                {
                    byte[]? k = null;
                    try
                    {
                        k = Convert.FromBase64String(ks.secrets.userSecretsKey); // throws on bad base64
                        if (k.Length != 32) return false;
                    }
                    finally
                    {
                        SensitiveDataCleaner.WipeByteArray(ref k);
                    }
                }

                // 6) Basic sanity for SQL map (present and strings present)
                if (ks.sql == null || ks.sql.Count == 0)
                    return false;
                foreach (var kv in ks.sql)
                {
                    // Keys must exist; values must be non-null (can be empty string)
                    if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value is null)
                        return false;
                }

                // All checks passed
                return true;
            }
            catch
            {
                // Any parse/base64/shape error => invalid
                return false;
            }
            finally
            {
                if (jsonBytes != null) SensitiveDataCleaner.WipeByteArray(ref jsonBytes);
            }
        }

        /// <summary>
        /// Read-only integrity check for keyset.json that returns a Security.Utility
        /// technical result code and seriousness classification. The result does not
        /// include user-facing message text, exception text, SQL, secrets, keys,
        /// passwords, protected payloads, sensitive paths, or caller actions.
        /// </summary>
        public static SecurityUtilityResult ValidateKeysetJsonResult(Func<byte[]> loadKeyset)
        {
            if (loadKeyset is null)
                return Result(SecurityUtilityReturnCode.InvalidInput, SecurityUtilityResultKind.Failure);

            byte[]? jsonBytes = null;

            try
            {
                jsonBytes = loadKeyset.Invoke();
                if (jsonBytes is not { Length: > 0 })
                    return Result(SecurityUtilityReturnCode.RequiredPayloadMissing, SecurityUtilityResultKind.Abend);

                var reader = new Utf8JsonReader(jsonBytes, isFinalBlock: true, state: default);
                while (reader.Read()) { }
                if (reader.BytesConsumed != jsonBytes.Length)
                    return Result(SecurityUtilityReturnCode.KeysetInvalid, SecurityUtilityResultKind.Abend);

                var json = Encoding.UTF8.GetString(jsonBytes);
                var ks = JsonSerializer.Deserialize<KeysetV2>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

                if (ks is null || ks.keySetVersion != 2)
                    return Result(SecurityUtilityReturnCode.KeysetInvalid, SecurityUtilityResultKind.Abend);

                if (ks.secrets is null || string.IsNullOrWhiteSpace(ks.secrets.dbPassword))
                    return Result(SecurityUtilityReturnCode.RequiredPayloadMissing, SecurityUtilityResultKind.Abend);

                var dbPwChars = KeysetJsonV2.DecodeDbPasswordToChars(ks.secrets.dbPassword);
                try
                {
                    if (dbPwChars.Length == 0)
                        return Result(SecurityUtilityReturnCode.RequiredPayloadMissing, SecurityUtilityResultKind.Abend);
                }
                finally
                {
                    Array.Clear(dbPwChars, 0, dbPwChars.Length);
                }

                var logKeyResult = ValidateOptionalKeysetKey(ks.secrets.logPayloadKey);
                if (!logKeyResult.Succeeded)
                    return logKeyResult;

                var userKeyResult = ValidateOptionalKeysetKey(ks.secrets.userSecretsKey);
                if (!userKeyResult.Succeeded)
                    return userKeyResult;

                if (ks.sql is null || ks.sql.Count == 0)
                    return Result(SecurityUtilityReturnCode.RequiredPayloadMissing, SecurityUtilityResultKind.Abend);

                foreach (var kv in ks.sql)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value is null)
                        return Result(SecurityUtilityReturnCode.RequiredPayloadMissing, SecurityUtilityResultKind.Abend);
                }

                return Result(SecurityUtilityReturnCode.Success, SecurityUtilityResultKind.Success);
            }
            catch (JsonException)
            {
                return Result(SecurityUtilityReturnCode.KeysetInvalid, SecurityUtilityResultKind.Abend);
            }
            catch (FormatException)
            {
                return Result(SecurityUtilityReturnCode.KeysetInvalid, SecurityUtilityResultKind.Abend);
            }
            catch (ArgumentException)
            {
                return Result(SecurityUtilityReturnCode.InvalidInput, SecurityUtilityResultKind.Failure);
            }
            catch
            {
                return Result(SecurityUtilityReturnCode.UnknownSecurityFailure, SecurityUtilityResultKind.Abend);
            }
            finally
            {
                if (jsonBytes != null) SensitiveDataCleaner.WipeByteArray(ref jsonBytes);
            }
        }

        private static SecurityUtilityResult ValidateOptionalKeysetKey(string? base64Key)
        {
            if (string.IsNullOrWhiteSpace(base64Key))
                return Result(SecurityUtilityReturnCode.Success, SecurityUtilityResultKind.Success);

            byte[]? keyBytes = null;
            try
            {
                keyBytes = Convert.FromBase64String(base64Key);
                return keyBytes.Length == 32
                    ? Result(SecurityUtilityReturnCode.Success, SecurityUtilityResultKind.Success)
                    : Result(SecurityUtilityReturnCode.CryptoKeyInvalid, SecurityUtilityResultKind.Abend);
            }
            catch (FormatException)
            {
                return Result(SecurityUtilityReturnCode.KeysetInvalid, SecurityUtilityResultKind.Abend);
            }
            finally
            {
                SensitiveDataCleaner.WipeByteArray(ref keyBytes);
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
