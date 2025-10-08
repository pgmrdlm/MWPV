// File: Utilities/Security/KeyArchiveIntegrityService.cs
using System;
using System.Data;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Utilities.Helpers;   // DatabaseHelper
using Utilities.Sql;       // SecureSql.Require(...)

namespace Utilities.Security
{
    /// <summary>
    /// Records and validates integrity of the encrypted key archive using
    /// file size (bytes) and SHA-256 of the entire archive.
    /// - Single authoritative row (kai_Id = 1) in KeyArchiveIntegrity.
    /// - Assumes DDL already created the table (no create logic here).
    /// - All SQL comes from SqlCatalog via SecureSql.Require(...).
    /// </summary>
    internal static class KeyArchiveIntegrityService
    {
        private const string TableName = "KeyArchiveIntegrity";

        /// <summary>
        /// Compute size + SHA-256 for the given archive and upsert into KeyArchiveIntegrity.
        /// Call immediately after successfully creating/replacing the key archive.
        /// </summary>
        public static void UpsertFromArchivePath(string archivePath)
        {
            if (string.IsNullOrWhiteSpace(archivePath))
                throw new ArgumentException("Archive path is required.", nameof(archivePath));
            if (!File.Exists(archivePath))
                throw new FileNotFoundException("Archive file not found.", archivePath);

            long size = new FileInfo(archivePath).Length;
            string sha = ComputeSha256Hex(archivePath);

            using var cn = DatabaseHelper.OpenConnection();
            using var cmd = cn.CreateCommand();

            cmd.CommandText = SecureSql.Require("s_KeyArchiveIntegrity_upsert.sql");

            var pSha = cmd.CreateParameter(); pSha.ParameterName = "@sha"; pSha.DbType = DbType.String; pSha.Value = sha;
            var pSize = cmd.CreateParameter(); pSize.ParameterName = "@size"; pSize.DbType = DbType.Int64; pSize.Value = size;

            cmd.Parameters.Add(pSha);
            cmd.Parameters.Add(pSize);

            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Validate the current archive against the stored size + SHA-256.
        /// Returns true when both size and sha match the stored record.
        /// If the record does not exist, returns false (with reason).
        /// </summary>
        public static bool ValidateAgainstStored(string archivePath, out string? reason)
        {
            reason = null;

            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            {
                reason = "Archive path not found.";
                return false;
            }

            long? storedSize = null;
            string? storedSha = null;

            using (var cn = DatabaseHelper.OpenConnection())
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = SecureSql.Require("s_KeyArchiveIntegrity_select.sql");
                using var rd = cmd.ExecuteReader(CommandBehavior.SingleRow);
                if (rd.Read())
                {
                    storedSize = rd.IsDBNull(0) ? null : rd.GetInt64(0);
                    storedSha = rd.IsDBNull(1) ? null : rd.GetString(1);
                }
            }

            if (storedSize is null || string.IsNullOrWhiteSpace(storedSha))
            {
                reason = "No integrity record found.";
                return false;
            }

            long actualSize = new FileInfo(archivePath).Length;
            if (actualSize != storedSize.Value)
            {
                reason = $"Size mismatch (expected {storedSize.Value}, actual {actualSize}).";
                return false;
            }

            string actualSha = ComputeSha256Hex(archivePath);
            if (!actualSha.Equals(storedSha, StringComparison.OrdinalIgnoreCase))
            {
                reason = "SHA-256 mismatch.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns true if the single-row integrity record exists.
        /// </summary>
        public static bool HasIntegrityRecord()
        {
            using var cn = DatabaseHelper.OpenConnection();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = SecureSql.Require("s_KeyArchiveIntegrity_exists.sql");
            var val = cmd.ExecuteScalar();
            return (val != null && val != DBNull.Value);
        }

        // --- helpers ---

        private static string ComputeSha256Hex(string filePath)
        {
            using var sha = SHA256.Create();
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = sha.ComputeHash(fs);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
