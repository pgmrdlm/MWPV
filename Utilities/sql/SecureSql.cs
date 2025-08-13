// Utilities/Sql/SecureSql.cs
using System;
using System.Text;
using System.Reflection;

namespace Utilities.Sql
{
    public static class SecureSql
    {
        /// <summary>
        /// Fetch SQL text by filename key from SecureEncryptedDataStore.
        /// Works with GetString(string), Get(string)->byte[], or GetChars(string)->char[].
        /// Throws if the key is missing.
        /// </summary>
        public static string Require(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                throw new ArgumentException("Filename key must be provided.", nameof(filename));

            var t = typeof(Utilities.Security.SecureEncryptedDataStore);

            // 1) Prefer GetString(string)
            var mGetString = t.GetMethod("GetString", BindingFlags.Public | BindingFlags.Static);
            if (mGetString != null)
            {
                var s = mGetString.Invoke(null, new object[] { filename }) as string;
                if (!string.IsNullOrEmpty(s)) return s;
            }

            // 2) Fallback: Get(string) -> byte[]
            var mGetBytes = t.GetMethod("Get", BindingFlags.Public | BindingFlags.Static);
            if (mGetBytes != null)
            {
                if (mGetBytes.Invoke(null, new object[] { filename }) is byte[] bytes && bytes.Length > 0)
                    return Encoding.UTF8.GetString(bytes);
            }

            // 3) Fallback: GetChars(string) -> char[]
            var mGetChars = t.GetMethod("GetChars", BindingFlags.Public | BindingFlags.Static);
            if (mGetChars != null)
            {
                if (mGetChars.Invoke(null, new object[] { filename }) is char[] chars && chars.Length > 0)
                    return new string(chars);
            }

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[SecureSql] Missing script in store: {filename}");
#endif
            throw new InvalidOperationException($"SQL script not found in secure store: {filename}");
        }
    }
}
