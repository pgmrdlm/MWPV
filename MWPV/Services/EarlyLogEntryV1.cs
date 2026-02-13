// MWPV/Services/EarlyLogEntryV1.cs  (DEDUP FOCUSED REWRITE)
// Scope: Only dedup helpers & file utilities. Ingestion logic is considered complete elsewhere.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Utilities.Helpers;

// Explicit alias to avoid clash with legacy Security.Utility.Sha256Common
using HashSha256 = Security.Utility.Crypto.Hash.Sha256Common;

namespace MWPV.Services
{
    /// <summary>
    /// POCO model for an early-log entry persisted as plaintext JSON (*.elog).
    /// Notes:
    /// - Dedupe prefers logGuid when present; falls back to a content hash.
    /// - Payload can be an arbitrary JSON object or string; for hashing we canonicalize JSON.
    /// </summary>
    public sealed class EarlyLogEntryV1
    {
        public int ver { get; set; } = 1;
        public string logGuid { get; set; } = "";      // optional but preferred for dedupe
        public DateTime whenUtc { get; set; }
        public string level { get; set; } = "INFO";
        public string source { get; set; } = "app";
        public string eventCode { get; set; } = "GENERAL";
        public string sessionId { get; set; } = "";
        public string machineId { get; set; } = "";
        public string appVersion { get; set; } = "";
        public bool isCrash { get; set; } = false;
        public string payloadFmt { get; set; } = "json";
        public object payload { get; set; } = "";

        public static EarlyLogEntryV1 FromJson(string json)
        {
            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var e = JsonSerializer.Deserialize<EarlyLogEntryV1>(json, opts) ?? new EarlyLogEntryV1();
            // sanitize
            if (e.whenUtc == default) e.whenUtc = DateTime.UtcNow;
            e.level = string.IsNullOrWhiteSpace(e.level) ? "INFO" : e.level.Trim();
            e.source = string.IsNullOrWhiteSpace(e.source) ? "app" : e.source.Trim();
            e.eventCode = string.IsNullOrWhiteSpace(e.eventCode) ? "GENERAL" : e.eventCode.Trim();
            e.sessionId = e.sessionId?.Trim() ?? "";
            e.machineId = e.machineId?.Trim() ?? "";
            e.appVersion = e.appVersion?.Trim() ?? "";
            e.payloadFmt = string.IsNullOrWhiteSpace(e.payloadFmt) ? "json" : e.payloadFmt.Trim().ToLowerInvariant();
            e.logGuid = e.logGuid?.Trim() ?? "";
            return e;
        }
    }

    /// <summary>
    /// Dedupe utilities for early logs (one-file-at-a-time).
    /// No DB writes here. Callers may supply DB-lookup delegates to short-circuit duplicates already ingested.
    /// </summary>
    public static class EarlyLogDedupe
    {
        // %LOCALAPPDATA%\MWPV\early
        public static string EarlyDir
        {
            get
            {
                string root = AppPaths.LocalAppDataRoot();
                return Path.Combine(root, "MWPV", "early");
            }
        }


        private static string DedupeDir => Path.Combine(EarlyDir, "dedup");
        private static string QuarantineDir => Path.Combine(EarlyDir, "quarantine");
        private static string IndexDir => Path.Combine(EarlyDir, ".dedupe");
        private static string IndexPath => Path.Combine(IndexDir, "index.json");

        private sealed class DedupeIndex
        {
            public HashSet<string> SeenGuids { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> SeenHashes { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static DedupeIndex LoadIndex()
        {
            try
            {
                Directory.CreateDirectory(IndexDir);
                if (File.Exists(IndexPath))
                {
                    var json = File.ReadAllText(IndexPath, Encoding.UTF8);
                    var idx = JsonSerializer.Deserialize<DedupeIndex>(json);
                    if (idx != null) return idx;
                }
            }
            catch { }
            return new DedupeIndex();
        }

        private static void SaveIndex(DedupeIndex idx)
        {
            try
            {
                Directory.CreateDirectory(IndexDir);
                var json = JsonSerializer.Serialize(idx, new JsonSerializerOptions { WriteIndented = false });
                File.WriteAllText(IndexPath, json, Encoding.UTF8);
            }
            catch { }
        }

        public static string ComputeContentHash(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath, Encoding.UTF8).Trim();
                var entry = EarlyLogEntryV1.FromJson(json);

                string payloadNorm = CanonicalizePayload(entry.payload, entry.payloadFmt);
                var core = new
                {
                    entry.ver,
                    entry.level,
                    entry.source,
                    entry.eventCode,
                    entry.sessionId,
                    entry.machineId,
                    entry.appVersion,
                    entry.isCrash,
                    entry.payloadFmt,
                    payload = payloadNorm
                };
                string canonical = JsonSerializer.Serialize(core);

                // Hash canonicalized representation via common helper
                return HashSha256.Hex(Encoding.UTF8.GetBytes(canonical));
            }
            catch
            {
                // Fallback: hash raw file bytes via common helper
                return HashSha256.Hex(File.ReadAllBytes(filePath));
            }
        }

        private static string CanonicalizePayload(object payload, string fmt)
        {
            if (payload is null) return "";
            try
            {
                if (fmt?.Equals("json", StringComparison.OrdinalIgnoreCase) == true)
                {
                    string payloadJson = payload is JsonElement je
                        ? je.GetRawText()
                        : (payload is string s ? s : JsonSerializer.Serialize(payload));

                    using var doc = JsonDocument.Parse(payloadJson);
                    return JsonSerializer.Serialize(doc.RootElement);
                }
                return payload is string sp ? sp.Trim() : JsonSerializer.Serialize(payload);
            }
            catch
            {
                return payload?.ToString() ?? "";
            }
        }

        // NOTE: all hashing goes through Security.Utility.Crypto.Hash.Sha256Common (aliased as HashSha256)

        public sealed class DedupResult
        {
            public bool IsDuplicate { get; init; }
            public string? Reason { get; init; }
            public string? GuidUsed { get; init; }
            public string? HashUsed { get; init; }
            public string? DispositionPath { get; init; }
        }

        public static DedupResult DedupOne(
            string filePath,
            Func<string, bool>? existsByGuid = null,
            Func<string, bool>? existsByHash = null)
        {
            var fi = new FileInfo(filePath);
            if (!fi.Exists) return new DedupResult { IsDuplicate = false, Reason = "not_found" };

            string raw;
            try { raw = File.ReadAllText(filePath, Encoding.UTF8); }
            catch
            {
                return Quarantine(fi, "io_error");
            }

            EarlyLogEntryV1? entry = null;
            try { entry = EarlyLogEntryV1.FromJson(raw); }
            catch { }

            string? guid = entry?.logGuid;
            string hash = ComputeContentHash(filePath);

            if (!string.IsNullOrWhiteSpace(guid) && existsByGuid != null)
            {
                try
                {
                    if (existsByGuid(guid!))
                    {
                        return MoveDuplicate(fi, guid, hash, "db_guid");
                    }
                }
                catch { }
            }
            if (existsByHash != null)
            {
                try
                {
                    if (existsByHash(hash))
                    {
                        return MoveDuplicate(fi, guid, hash, "db_hash");
                    }
                }
                catch { }
            }

            var index = LoadIndex();
            if (!string.IsNullOrWhiteSpace(guid) && index.SeenGuids.Contains(guid!))
            {
                return MoveDuplicate(fi, guid, hash, "index_guid");
            }
            if (index.SeenHashes.Contains(hash))
            {
                return MoveDuplicate(fi, guid, hash, "index_hash");
            }

            if (!string.IsNullOrWhiteSpace(guid))
            {
                var dup = Directory.EnumerateFiles(fi.DirectoryName ?? EarlyDir, "*.elog", SearchOption.TopDirectoryOnly)
                    .Where(p => !Path.GetFileName(p).Equals(fi.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(p => TryReadGuid(p))
                    .FirstOrDefault(g => string.Equals(g, guid, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(dup))
                {
                    return MoveDuplicate(fi, guid, hash, "sibling_guid");
                }
            }

            var hashMatch = Directory.EnumerateFiles(fi.DirectoryName ?? EarlyDir, "*.elog", SearchOption.TopDirectoryOnly)
                .Where(p => !Path.GetFileName(p).Equals(fi.Name, StringComparison.OrdinalIgnoreCase))
                .Select(p => (p, ComputeContentHash(p)))
                .FirstOrDefault(t => t.Item2 == hash);

            if (!string.IsNullOrEmpty(hashMatch.p))
            {
                return MoveDuplicate(fi, guid, hash, "sibling_hash");
            }

            if (!string.IsNullOrWhiteSpace(guid)) index.SeenGuids.Add(guid!);
            index.SeenHashes.Add(hash);
            SaveIndex(index);

            return new DedupResult
            {
                IsDuplicate = false,
                Reason = "unique",
                GuidUsed = guid,
                HashUsed = hash,
                DispositionPath = fi.FullName
            };
        }

        private static DedupResult MoveDuplicate(FileInfo fi, string? guidUsed, string hashUsed, string reason)
        {
            try
            {
                Directory.CreateDirectory(DedupeDir);
                var dest = Path.Combine(DedupeDir, fi.Name);
                if (File.Exists(dest)) File.Delete(dest);
                fi.MoveTo(dest);
                return new DedupResult
                {
                    IsDuplicate = true,
                    Reason = reason,
                    GuidUsed = guidUsed,
                    HashUsed = hashUsed,
                    DispositionPath = dest
                };
            }
            catch
            {
                return new DedupResult { IsDuplicate = true, Reason = "move_failed", GuidUsed = guidUsed, HashUsed = hashUsed };
            }
        }

        private static DedupResult Quarantine(FileInfo fi, string reason)
        {
            try
            {
                Directory.CreateDirectory(QuarantineDir);
                var dest = Path.Combine(QuarantineDir, fi.Name);
                if (File.Exists(dest)) File.Delete(dest);
                fi.MoveTo(dest);
                return new DedupResult { IsDuplicate = true, Reason = "quarantine:" + reason, DispositionPath = dest };
            }
            catch
            {
                return new DedupResult { IsDuplicate = true, Reason = "quarantine_failed" };
            }
        }

        private static string? TryReadGuid(string path)
        {
            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("logGuid", out var guidEl))
                {
                    var s = guidEl.GetString();
                    return string.IsNullOrWhiteSpace(s) ? null : s!.Trim();
                }
            }
            catch { }
            return null;
        }
    }
}
