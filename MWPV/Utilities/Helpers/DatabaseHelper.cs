using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Threading.Tasks;
using Security.Utility;     // SecureEncryptedDataStore, SensitiveDataCleaner

namespace Utilities.Helpers
{
    public static class DatabaseHelper
    {
        // SINGLE SOURCE OF TRUTH:
        // Loader/extractor matches this exact filename inside the key archive.
        // Do NOT rename or strip ".txt".
        public const string DbPasswordKey = "DB_Password.txt";

        /// <summary>%LOCALAPPDATA%/MWPV/MWPV.db</summary>
        public static string GetAppDbPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MWPV",
                "MWPV.db");
        }

        /// <summary>Optional convenience: %LOCALAPPDATA%/MWPV/sql</summary>
        public static string GetSqlFolderPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MWPV",
                "sql");
        }

        /// <summary>
        /// Store the DB password in the SecureEncryptedDataStore (as char[]).
        /// Callers should pass in a freshly created char[] and NOT reuse it.
        /// This method wipes the provided array after storing.
        /// </summary>
        public static void StoreDatabasePassword(char[] password)
        {
            if (password == null || password.Length == 0)
                throw new ArgumentException("Password must not be empty.", nameof(password));

            SecureEncryptedDataStore.SetAndWipe(DbPasswordKey, password);
        }

        /// <summary>
        /// Retrieve the DB password as a char[] from the SecureEncryptedDataStore.
        /// Caller MUST wipe the returned array when done.
        /// </summary>
        public static char[] ReadDatabasePassword()
        {
            return SecureEncryptedDataStore.GetChars(DbPasswordKey);
        }

        /// <summary>
        /// Execute an action that needs the password as a string.
        /// Creates a string just-in-time and wipes both the string and the char[] afterward.
        /// </summary>
        public static void WithDatabasePasswordString(Action<string> use)
        {
            if (use == null) throw new ArgumentNullException(nameof(use));

            char[]? pwChars = null;
            string? pwString = null;
            try
            {
                pwChars = ReadDatabasePassword();
                if (pwChars == null || pwChars.Length == 0)
                    throw new InvalidOperationException("Database password not loaded.");

                pwString = new string(pwChars);
                use(pwString);
            }
            finally
            {
                if (pwString != null) SensitiveDataCleaner.WipeString(ref pwString);
                if (pwChars != null) SensitiveDataCleaner.WipeCharArray(pwChars);
            }
        }

        /// <summary>
        /// Open an application DB connection (read/write/create) using the stored password.
        /// Returns an **OPEN** SqliteConnection. Caller is responsible for disposing it.
        /// IMPORTANT: This method performs **no logging** to avoid recursion with SecureLogService.
        /// </summary>
        public static SqliteConnection GetAppOpenConnection()
        {
            var dbPath = GetAppDbPath();

            // Make sure the folder exists; avoids surprises on first run.
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            SqliteConnection? conn = null;
            Exception? captured = null;

            WithDatabasePasswordString(pw =>
            {
                try
                {
                    var csb = new SqliteConnectionStringBuilder
                    {
                        DataSource = dbPath,
                        Mode = SqliteOpenMode.ReadWriteCreate,
                        // NOTE: Requires Microsoft.Data.Sqlite build with SQLCipher support.
                        Password = pw
                    };

                    // Create + open a fresh connection every call.
                    conn = new SqliteConnection(csb.ToString());
                    conn.Open();

                    // Centralized PRAGMAs (security + integrity).
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
PRAGMA foreign_keys = ON;
PRAGMA secure_delete = ON;      -- overwrite deleted content
PRAGMA journal_mode = WAL;      -- performance; WAL is encrypted under SQLCipher
";
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
            });

            if (captured != null)
                throw captured;

            if (conn == null)
                throw new InvalidOperationException("Failed to open encrypted database connection.");

            return conn;
        }

        /// <summary>
        /// Convenience alias so call sites can use DatabaseHelper.OpenConnection().
        /// </summary>
        public static SqliteConnection OpenConnection() => GetAppOpenConnection();

        /// <summary>
        /// Safe pattern helper: creates, opens, and disposes a connection around your work.
        /// Ensures we always close after use.
        /// </summary>
        public static async Task<T> WithConnectionAsync<T>(Func<SqliteConnection, Task<T>> work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            await using var conn = GetAppOpenConnection();
            return await work(conn);
        }

        public static async Task WithConnectionAsync(Func<SqliteConnection, Task> work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            await using var conn = GetAppOpenConnection();
            await work(conn);
        }
    }
}
