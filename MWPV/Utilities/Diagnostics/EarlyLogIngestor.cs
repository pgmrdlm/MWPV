// Utilities/Diagnostics/EarlyLogIngestor.cs — rewritten to use direct DB insert (SmokeTest-style)
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Utilities.Helpers;   // DatabaseHelper
using Utilities.Sql;       // SqlCagegory

namespace Utilities.Diagnostics
{
    /// <summary>
    /// Ingests *.elogp from EarlyLoginFailures.StoreDir into DB, with dedupe & quarantine.
    /// Uses a single synchronous insert path (same as SmokeTester), one connection, prepared cmd.
    /// </summary>
    public static class EarlyLogIngestor
    {
        /// <summary>
        /// Enumerate early files, validate+decrypt, dedupe by SHA256(rawJson), insert, and secure-delete.
        /// Quarantines bad/duplicate files with a .reason.txt note.
        /// </summary>
        public static void IngestAll()
        {
            var dir = EarlyLoginFailures.StoreDir;
            Directory.CreateDirectory(dir);

            var files = Directory.GetFiles(dir, $"*{EarlyLoginFailures.FileExt}", SearchOption.TopDirectoryOnly);
            if (files.Length == 0)
            {
                Debug.WriteLine("[EarlyIngest] No early files found.");
                return;
            }

            var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Open the SAME connection path as categories/smoke test
            using var conn = DatabaseHelper.OpenConnection();

            // Optional: wrap in a transaction for speed/atomicity (fine to keep in debug)
            using var tx = conn.BeginTransaction();

            // Prepare single insert command (same SQL as smoke test path)
            using var insCmd = conn.CreateCommand();
            insCmd.Transaction = tx;
            insCmd.CommandText = SqlCagegory.GetSql("Logs_Insert_V2.sql");

            // Predeclare parameters (reused per file)
            AddWithValue(insCmd, "@WhenUtc", "");
            AddWithValue(insCmd, "@CreatedUtc", "");
            AddWithValue(insCmd, "@Level", "");
            AddWithValue(insCmd, "@Source", "");
            AddWithValue(insCmd, "@EventCode", "");
            AddWithValue(insCmd, "@SessionId", "");
            AddWithValue(insCmd, "@MachineId", Environment.MachineName ?? "");
            AddWithValue(insCmd, "@AppVersion", AppVersion());
            AddWithValue(insCmd, "@IsCrash", 0);

            var pPayload = insCmd.CreateParameter();
            pPayload.ParameterName = "@Payload";
            pPayload.DbType = DbType.Binary;
            pPayload.Value = DBNull.Value; // keep NULL in debug; wire real payload later
            insCmd.Parameters.Add(pPayload);

            var pPayloadFmt = insCmd.CreateParameter();
            pPayloadFmt.ParameterName = "@PayloadFmt";
            pPayloadFmt.DbType = DbType.String;
            pPayloadFmt.Value = DBNull.Value; // keep NULL in debug
            insCmd.Parameters.Add(pPayloadFmt);

            AddWithValue(insCmd, "@StackHash", "");

            // Command to fetch last insert id
            using var lastIdCmd = conn.CreateCommand();
            lastIdCmd.Transaction = tx;
            lastIdCmd.CommandText = SqlCagegory.GetSql("Logs_LastInsertId.sql");

            int total = 0, inserted = 0, dupes = 0, failed = 0, quarantined = 0;

            foreach (var path in files)
            {
                total++;
                try
                {
                    if (!EarlyLoginFailures.TryReadAndDecrypt(path, out var entry, out var reason, out var rawJson, out _))
                    {
                        Quarantine(path, reason ?? "read/decrypt failed");
                        quarantined++;
                        continue;
                    }

                    var hashHex = Sha256Hex(rawJson!);
                    if (!seenHashes.Add(hashHex))
                    {
                        Quarantine(path, "duplicate in same run (hash)");
                        dupes++;
                        continue;
                    }

                    // Bind values for this row (minimal debug-friendly fields)
                    var whenIso = entry!.whenUtc;         // already UTC in your model
                    var createdIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

                    Set(insCmd, "@WhenUtc", whenIso);
                    Set(insCmd, "@CreatedUtc", createdIso);
                    Set(insCmd, "@Level", "ERROR");        // early failures are errors
                    Set(insCmd, "@Source", "EarlyIngest");  // distinct source
                    Set(insCmd, "@EventCode", "EARLY_FAIL");
                    Set(insCmd, "@SessionId", "");             // fill later if desired
                    // @MachineId, @AppVersion, @IsCrash already set with defaults above
                    Set(insCmd, "@StackHash", hashHex);

                    // NOTE: keeping payload NULL during debug; we just need presence
                    // If you want to store the raw JSON now, uncomment:
                    // pPayload.Value = rawJson ?? (object)DBNull.Value;
                    // pPayloadFmt.Value = (rawJson != null) ? "elog-json" : (object)DBNull.Value;

                    var affected = insCmd.ExecuteNonQuery();
                    if (affected == 1)
                    {
                        var lastId = Convert.ToInt64(lastIdCmd.ExecuteScalar());
                        Debug.WriteLine($"[EarlyIngest] Inserted id={lastId} from {Path.GetFileName(path)}");
                        inserted++;

                        SecureDelete(path);
                    }
                    else
                    {
                        Quarantine(path, "insert affected != 1");
                        quarantined++;
                    }
                }
                catch (Exception ex)
                {
                    Quarantine(path, "ingest exception: " + ex.Message);
                    failed++;
                }
            }

            // Commit whatever succeeded
            try
            {
                tx.Commit();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EarlyIngest][TX] Commit failed: {ex.Message}");
                // If commit fails, we purposely do NOT delete/quarantine further; files already handled.
            }

            Debug.WriteLine($"[EarlyIngest] total={total} inserted={inserted} dupes={dupes} quarantined={quarantined} failed={failed}");
        }

        private static string Sha256Hex(byte[] data)
        {
            using var sha = SHA256.Create();
            var b = sha.ComputeHash(data);
            var sb = new StringBuilder(b.Length * 2);
            foreach (var t in b) sb.Append(t.ToString("x2"));
            return sb.ToString();
        }

        private static void SecureDelete(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 0 && fi.Length <= 1024 * 1024)
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
                    var zeros = new byte[8192];
                    long remaining = fi.Length;
                    while (remaining > 0)
                    {
                        var w = (int)Math.Min(zeros.Length, remaining);
                        fs.Write(zeros, 0, w);
                        remaining -= w;
                    }
                    fs.Flush(true);
                }
            }
            catch { }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        private static void Quarantine(string path, string? reason)
        {
            try
            {
                Directory.CreateDirectory(EarlyLoginFailures.QuarantineDir);
                var dest = Path.Combine(EarlyLoginFailures.QuarantineDir, Path.GetFileName(path));
                File.Move(path, dest, overwrite: true);

                if (!string.IsNullOrWhiteSpace(reason))
                    File.WriteAllText(dest + ".reason.txt", reason);
            }
            catch { }
        }

        private static void AddWithValue(IDbCommand cmd, string name, object? value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        private static void Set(IDbCommand cmd, string name, object? value)
        {
            // Update existing parameter value
            if (cmd.Parameters.Contains(name))
                ((IDbDataParameter)cmd.Parameters[name]).Value = value ?? DBNull.Value;
        }

        private static string AppVersion()
        {
            var v = System.Reflection.Assembly.GetEntryAssembly()?.GetName()?.Version;
            return v?.ToString() ?? "dev";
        }
    }
}
