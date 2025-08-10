using Microsoft.Data.Sqlite;
using System;
using System.IO;
using Utilities.Security;

namespace Utilities.Helpers
{
    public static class DatabaseHelper
    {
        // Keep your existing key name if your code expects it to match a filename
        private const string DbPasswordKey = "DB_Password.txt";

        public static string GetAppDbPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MWPV",
                "MWPV.db");
        }

        /// <summary>
        /// Persist the DB password securely. Accepts char[] and never stores a string.
        /// Wipes the caller buffer after storing.
        /// </summary>
        public static void StoreDatabasePassword(char[] generatedPassword)
        {
            if (generatedPassword == null || generatedPassword.Length == 0)
                throw new ArgumentException("Password cannot be null/empty.", nameof(generatedPassword));

            // If caller might still need their buffer, we copy; SetAndWipe will wipe the copy.
            var copy = new char[generatedPassword.Length];
            Array.Copy(generatedPassword, copy, generatedPassword.Length);

            try
            {
                SecureEncryptedDataStore.SetAndWipe(DbPasswordKey, copy);
            }
            finally
            {
                // Wipe the original UI/source buffer
                SensitiveDataCleaner.WipeCharArray(ref generatedPassword);
            }
        }

        /// <summary>
        /// Read the DB password as a char[]. Caller MUST wipe the returned array.
        /// </summary>
        public static char[] ReadDatabasePassword()
        {
            // Pull directly as char[] from the datastore; caller wipes it.
            return SecureEncryptedDataStore.GetChars(DbPasswordKey);
        }

        /// <summary>
        /// Execute an action that needs the password as a string.
        /// Converts to string just-in-time; wipes the char[] afterward.
        /// </summary>
        public static void WithDatabasePasswordString(Action<string> use)
        {
            if (use == null) throw new ArgumentNullException(nameof(use));

            char[] pwd = null;
            string pwStr = null;

            try
            {
                pwd = ReadDatabasePassword();
                if (pwd == null || pwd.Length == 0)
                    throw new InvalidOperationException("Database password is missing or empty.");

                pwStr = new string(pwd);
                use(pwStr);
            }
            finally
            {
                // Can't deterministically wipe strings, but call your helper if you implemented one
                SensitiveDataCleaner.WipeString(ref pwStr);
                SensitiveDataCleaner.WipeCharArray(ref pwd);
            }
        }

        /// <summary>
        /// Open (or create) the app DB using the stored password. Wipes transients.
        /// </summary>
        public static SqliteConnection GetAppOpenConnection()
        {
            string dbPath = GetAppDbPath();
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            SqliteConnection conn = null;

            WithDatabasePasswordString(pw =>
            {
                var csb = new SqliteConnectionStringBuilder
                {
                    DataSource = dbPath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Password = pw
                };

                conn = new SqliteConnection(csb.ToString());
                conn.Open();
            });

            return conn;
        }
    }
}
