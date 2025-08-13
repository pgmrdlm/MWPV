// Utilities/Sql/sqlCatagory.cs
using System;
using System.Collections.Generic;
using Utilities.Helpers;      // DatabaseHelper.DbPasswordKey
using Utilities.Security;     // ServiceSetUp
using MWPV.Services;          // ServiceSetUp

namespace Utilities.Sql
{
    /// <summary>
    /// Single source of truth for SQL (and related) assets stored in keys.7z,
    /// plus a tiny helper to load them into memory.
    /// </summary>
    public static class SqlCatagory
    {
        // 🔒 All scripts/keys to be pulled from keys.7z
        private static readonly string[] Files =
        {
            "CatagoryExists.sql",
            DatabaseHelper.DbPasswordKey,   // maps to DB_Password.txt in archive
            "InsertCatagory.sql",
            "Logs_Indexes.sql",
            "Logs_Init.sql",
            "Logs_Insert_V2.sql",
            "Logs_Select_Recent.sql",
            "MWPV_DB_Create.sql",
            "SelectCatagories.sql"
        };

        /// <summary>
        /// Ensures the session keys are loaded, then loads all SQL assets.
        /// </summary>
        public static void EnsureKeysAndLoadAll()
        {
            ServiceSetUp.EnsureKeySetFromArchive();
            LoadAll();
        }

        /// <summary>
        /// Loads all listed files from the encrypted archive into memory/datastore.
        /// </summary>
        public static void LoadAll()
        {
            foreach (var file in Files)
            {
                ServiceSetUp.LoadSqlFromEncryptedArchive(file);
            }
        }

        /// <summary>
        /// Exposes the catalog as a read-only view (handy for diagnostics/tests).
        /// </summary>
        public static IReadOnlyList<string> List => Files;
    }
}
