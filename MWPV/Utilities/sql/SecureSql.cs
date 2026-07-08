// Utilities/Sql/SecureSql.cs
using System;
using System.Text;
using Security.Utility;   // <- from the external Security.Utility.dll

namespace Utilities.Sql
{
    public static class SecureSql
    {
        /// <summary>
        /// Fetch SQL text by filename key from SecureEncryptedDataStore.
        /// Prefers GetString; falls back to GetChars. Throws if the key is missing/empty.
        /// </summary>
        public static string Require(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                throw new ArgumentException("Filename key must be provided.", nameof(filename));

            // 1) Prefer string
            try
            {
                var s = SecureEncryptedDataStore.GetString(filename);
                if (!string.IsNullOrEmpty(s))
                    return s;
            }
            catch (KeyNotFoundException)
            {
                // fall through to char[] fallback
            }

            // 2) Fallback: char[]
            try
            {
                var chars = SecureEncryptedDataStore.GetChars(filename);
                if (chars is { Length: > 0 })
                    return new string(chars);
            }
            catch (KeyNotFoundException)
            {
                // will throw below
            }
            throw new InvalidOperationException($"SQL script not found in secure store: {filename}");
        }

        /// <summary>
        /// Non-throwing variant. Returns null if not present.
        /// </summary>
        public static string? TryGet(string filename)
        {
            try { return Require(filename); }
            catch { return null; }
        }
    }
}
