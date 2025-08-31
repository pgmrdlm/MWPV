// Utilities/Diagnostics/EarlyLogIngestor.cs
using System;
using System.Data.Common;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Security.Utility; // SecureEncryptedDataStore

namespace Utilities.Diagnostics
{
    public sealed class EarlyIngestResult
    {
        public int Found { get; init; }
        public int Inserted { get; set; }
        public int Deduped { get; set; }
        public int Quarantined { get; set; }
        public int Deleted { get; set; }
        public int Errors { get; set; }
    }

    /// <summary>
    /// Reads *.elogp files written before login, validates header + JSON,
    /// and inserts rows into Logs inside a single DB transaction.
    /// Good files can be deleted after commit; bad ones are quarantined.
    /// </summary>
    public static class EarlyLogIngestor
    {
        private static readonly Regex HeaderRegex =
            new(@"^\s*ELOGJSON\|(?<ver>\d+)\|plain\s*$",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private const string QuarantineFolderName = "quarantine";

        /// <summary>
        /// Batch-ingest in ONE DB transaction.
        /// </summary>
        public static EarlyIngestResult IngestAllEarlyLogsTransactionalToLogs(
            string earlyDir,
            string sessionId,
            string appVersion,
            Func<DbConnection> openConnection,
            string pattern = "*.elogp",
            bool deleteOnSuccess = true,
            string source = "EarlyIngest",
            string level = "WARN")
        {
            Directory.CreateDirectory(earlyDir);

            var files = SafeGetFiles(earlyDir, pattern);
            var result = new EarlyIngestResult { Found = files.Length };

            if (files.Length == 0)
                return result;

            using var conn = openConnection();
            using var tx = conn.BeginTransaction();

            string insertSql = SecureEncryptedDataStore.GetString("Logs_Insert_V2.sql");
            if (string.IsNullOrWhiteSpace(insertSql))
                throw new InvalidOperationException("Logs_Insert_V2.sql not found in SecureEncryptedDataStore.");

            // Optional best-effort dedupe support (skip silently if script is absent or incompatible)
            string existsSql = SecureEncryptedDataStore.GetString("Logs_Exists_BySig.sql");
            bool canDedupe = !string.IsNullOrWhiteSpace(existsSql);

            foreach (var path in files)
            {
                try
                {
                    // 1) header + payload
                    string jsonText;
                    using (var sr = new StreamReader(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: true))
                    {
                        var header = ReadFirstNonEmptyLine(sr);
                        if (header is null)
                        {
                            Quarantine(path, "parse_failed_header_missing");
                            result.Quarantined++;
                            continue;
                        }

                        var m = HeaderRegex.Match(header);
                        if (!m.Success)
                        {
                            Quarantine(path, "parse_failed_header_mismatch");
                            result.Quarantined++;
                            continue;
                        }

                        if (!int.TryParse(m.Groups["ver"].Value, out var ver) || ver != 1)
                        {
                            Quarantine(path, $"parse_failed_unsupported_version_{m.Groups["ver"].Value}");
                            result.Quarantined++;
                            continue;
                        }

                        jsonText = sr.ReadToEnd()?.Trim() ?? string.Empty;
                    }

                    if (jsonText.Length == 0)
                    {
                        Quarantine(path, "parse_failed_json_missing");
                        result.Quarantined++;
                        continue;
                    }

                    using var doc = JsonDocument.Parse(jsonText);
                    var root = doc.RootElement;

                    // 2) optional dedupe (by payload hash)
                    if (canDedupe)
                    {
                        try
                        {
                            var sig = Sha256Hex(jsonText);
                            using var existsCmd = conn.CreateCommand();
                            existsCmd.Transaction = tx;
                            existsCmd.CommandText = existsSql;

                            var pSig = existsCmd.CreateParameter();
                            pSig.ParameterName = "@Sig";
                            pSig.Value = sig;
                            existsCmd.Parameters.Add(pSig);

                            var scalar = existsCmd.ExecuteScalar();
                            if (scalar != null && Convert.ToInt32(scalar) > 0)
                            {
                                result.Deduped++;
                                if (deleteOnSuccess && TryDelete(path)) result.Deleted++;
                                continue;
                            }
                        }
                        catch
                        {
                            // if dedupe fails, proceed without dedupe
                        }
                    }

                    // 3) map + insert
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = insertSql;

                        static string? S(JsonElement e, string name)
                            => e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetString() : null;

                        // WhenUtc from payload if present; else now UTC
                        var whenUtc = DateTime.UtcNow;
                        var whenUtcStr = S(root, "WhenUtc");
                        if (!string.IsNullOrWhiteSpace(whenUtcStr) && DateTime.TryParse(whenUtcStr, out var parsed))
                            whenUtc = parsed.ToUniversalTime();

                        // optional stack hash (based on ExStack)
                        string? stackHash = null;
                        var exStack = S(root, "ExStack");
                        if (!string.IsNullOrWhiteSpace(exStack))
                            stackHash = Sha256Hex(exStack);

                        void P(string name, object? val)
                        {
                            var p = cmd.CreateParameter();
                            p.ParameterName = name;
                            p.Value = val ?? DBNull.Value;
                            cmd.Parameters.Add(p);
                        }

                        P("@WhenUtc", whenUtc);
                        P("@CreatedUtc", DateTime.UtcNow);
                        P("@Level", level);
                        P("@Source", source);
                        P("@EventCode", S(root, "Category") ?? "EARLY_FAILURE");
                        P("@SessionId", sessionId);
                        P("@MachineId", Environment.MachineName);
                        P("@AppVersion", appVersion);
                        P("@IsCrash", 0);
                        P("@Payload", jsonText);
                        P("@PayloadFmt", "json");
                        P("@StackHash", (object?)stackHash ?? DBNull.Value);

                        _ = cmd.ExecuteNonQuery();
                        result.Inserted++;
                    }

                    // 4) delete on success (outside SQL transaction; best-effort)
                    if (deleteOnSuccess && TryDelete(path)) result.Deleted++;
                }
                catch
                {
                    result.Errors++;
                    Quarantine(path, "unexpected_exception");
                }
            }

            tx.Commit();

            // Console summary (optional)
            var when = DateTime.UtcNow.ToString("O");
            Console.WriteLine($"[EarlyIngest EARLY_INGEST_SUMMARY :: {{\"dir\":\"{EscapeJson(earlyDir)}\",\"found\":{result.Found},\"inserted\":{result.Inserted},\"deduped\":{result.Deduped},\"quarantined\":{result.Quarantined},\"deleted\":{result.Deleted},\"errors\":{result.Errors},\"whenUtc\":\"{when}\"}}]");

            return result;
        }

        // ------------------ helpers ------------------

        private static string ReadFirstNonEmptyLine(StreamReader sr)
        {
            while (!sr.EndOfStream)
            {
                var line = sr.ReadLine();
                if (!string.IsNullOrWhiteSpace(line))
                    return line;
            }
            return null;
        }

        private static bool TryDelete(string path)
        {
            try { File.Delete(path); return true; }
            catch { return false; }
        }

        private static bool Quarantine(string path, string reasonTag)
        {
            try
            {
                var dir = Path.GetDirectoryName(path)!;
                var qdir = Path.Combine(dir, QuarantineFolderName);
                Directory.CreateDirectory(qdir);

                var name = Path.GetFileNameWithoutExtension(path);
                var ext = Path.GetExtension(path);
                var dst = Path.Combine(qdir, $"{name}_{SanitizeForFile(reasonTag)}{ext}");

                if (File.Exists(dst)) File.Delete(dst);
                File.Move(path, dst);
            }
            catch
            {
                // best-effort
            }
            return false;
        }

        private static string Sha256Hex(string text)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(text);
            var hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static string[] SafeGetFiles(string dir, string pattern)
        {
            try { return Directory.GetFiles(dir, pattern); }
            catch { return Array.Empty<string>(); }
        }

        private static string SanitizeForFile(string s)
        {
            foreach (var ch in Path.GetInvalidFileNameChars())
                s = s.Replace(ch, '_');
            return s;
        }

        private static string EscapeJson(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
