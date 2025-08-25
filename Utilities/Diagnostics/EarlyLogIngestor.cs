using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

using Utilities.Logging;    // LogSeverity
using Utilities.Security;   // SecureLogService, SensitiveDataCleaner

namespace Utilities.Diagnostics
{
    /// <summary>
    /// Ingests plaintext “early” failure files from <see cref="EarlyLoginFailures.StoreDir"/>
    /// into the encrypted log (<see cref="SecureLogService"/>).
    /// Unreadable/malformed files are moved to <see cref="EarlyLoginFailures.QuarantineDir"/>.
    /// Successfully consumed files are securely deleted and empty source folders are pruned.
    /// </summary>
    public static class EarlyLogIngestor
    {
        public sealed class Result
        {
            public int Found { get; init; }
            public int Inserted { get; init; }
            public int Deduped { get; init; }        // reserved for future
            public int Quarantined { get; init; }
            public int Deleted { get; init; }        // files removed from the “early” store after ingest
            public int Errors { get; init; }
        }

        /// <summary>
        /// Ingest all pending “.elog” files. Writes via <see cref="SecureLogService"/> only (no schema SQL).
        /// If <paramref name="openConnectionFactory"/> is provided, it’s touched first as a reachability probe.
        /// </summary>
        public static Result IngestAllEarlyLogsTransactional(Func<DbConnection>? openConnectionFactory = null)
        {
            // Best-effort: prove DB reachability early so we fail fast rather than eating files.
            try { using var _ = openConnectionFactory?.Invoke(); } catch { /* ignore */ }

            // Snapshot upfront for deterministic 'Found'.
            var pending = new List<string>(EarlyLoginFailures.EnumeratePendingPaths());
            int found = pending.Count;
            if (found == 0)
            {
                TryLog(LogSeverity.Warn, "EARLY_INGEST_EMPTY", new
                {
                    dir = EarlyLoginFailures.StoreDir,
                    whenUtc = DateTime.UtcNow
                });
                return new Result { Found = 0 };
            }

            int inserted = 0, deduped = 0, quarantined = 0, deleted = 0, errors = 0;

            foreach (var path in pending)
            {
                try
                {
                    if (!TryParseEarlyFile(path, out var payload, out var rawJson, out var reason))
                    {
                        Quarantine(path, $"parse_failed:{reason}");
                        quarantined++;
                        TryPruneParentIfEmpty(path);
                        continue;
                    }

                    var evt = new
                    {
                        tsUtc = payload.tsUtc,
                        type = payload.type,
                        userSid = payload.userSid,
                        machine = payload.machine,
                        appVersion = payload.appVersion,
                        message = payload.message,
                        details = payload.details,   // boxed JSON if present
                        file = Path.GetFileName(path),
                        rawJson,                     // keep original JSON for audit trail
                        source = "EARLY_LOGIN"
                    };

                    // Write to encrypted log; on failure we keep the file to retry later.
                    SecureLogService.WriteAsync(
                        LogSeverity.Info,
                        evt,
                        eventCode: "EARLY_LOGIN_EVENT",
                        source: "EarlyIngest").GetAwaiter().GetResult();

                    // Consumed => securely delete the plaintext source.
                    TrySecureDeleteFile(path);
                    inserted++;
                    deleted++;
                    TryPruneParentIfEmpty(path);
                }
                catch (Exception ex)
                {
#if DEBUG
                    Debug.WriteLine($"[EARLY_INGEST] ERROR for '{path}': {ex.GetType().Name} {ex.Message}");
#endif
                    errors++; // leave file for a later attempt
                }
            }

            TryLog(LogSeverity.Info, "EARLY_INGEST_SUMMARY", new
            {
                dir = EarlyLoginFailures.StoreDir,
                found,
                inserted,
                deduped,
                quarantined,
                deleted,
                errors,
                whenUtc = DateTime.UtcNow
            });

            return new Result
            {
                Found = found,
                Inserted = inserted,
                Deduped = deduped,
                Quarantined = quarantined,
                Deleted = deleted,
                Errors = errors
            };
        }

        // ----------------- helpers -----------------

        private static void TryLog(LogSeverity level, string eventCode, object payload)
        {
            try
            {
                SecureLogService.WriteAsync(level, payload, eventCode: eventCode, source: "EarlyIngest")
                    .GetAwaiter().GetResult();
            }
            catch
            {
                // best-effort — never fail ingestion due to logging issues
            }
        }

        private static void Quarantine(string path, string? why = null)
        {
            try
            {
#if DEBUG
                Debug.WriteLine($"[EARLY_INGEST] Quarantine '{path}' :: {why}");
#endif
                EarlyLoginFailures.Quarantine(path, why);
            }
            catch
            {
                // best-effort
            }
        }

        private static void TrySecureDeleteFile(string path)
        {
            try
            {
                SensitiveDataCleaner.SecureFileDelete(
                    path,
                    overwritePasses: 1,
                    shredName: true,
                    finalZeroPass: true);
            }
            catch
            {
                try { File.Delete(path); } catch { /* ignore */ }
            }
        }

        private static void TryPruneParentIfEmpty(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;

                using var e = Directory.EnumerateFileSystemEntries(dir).GetEnumerator();
                if (!e.MoveNext())
                {
                    SensitiveDataCleaner.SecureDeleteDirectory(
                        dir,
                        overwritePasses: 1,
                        shredNames: true,
                        finalZeroPass: false,
                        removeDirectories: true);
                }
            }
            catch
            {
                // best-effort
            }
        }

        // ----------------- parsing -----------------

        private sealed class EarlyPayload
        {
            public DateTime tsUtc { get; init; }
            public string type { get; init; } = "unknown";
            public string userSid { get; init; } = "UnknownUser";
            public string machine { get; init; } = Environment.MachineName;
            public string appVersion { get; init; } = "unknown";
            public string message { get; init; } = "";
            public object? details { get; init; }
        }

        /// <summary>
        /// Supported formats:
        ///  # V1 (preferred):
        ///     MWPV-ELOG|v1
        ///     content-type: application/json; charset=utf-8
        ///
        ///     {json}
        ///
        ///     JSON fields (optional): utc, type, userSid, machine, appVersion, message, details, exception, extra
        ///
        ///  # Legacy:
        ///     MWPV_ELOG_V2
        ///     key=value
        ///     ...
        ///     json={...}
        /// </summary>
        private static bool TryParseEarlyFile(
            string path,
            out EarlyPayload payload,
            out string rawJson,
            out string reason)
        {
            payload = default!;
            rawJson = "";
            reason = "";

            string text;
            try
            {
                text = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                reason = "read_failed:" + ex.GetType().Name;
                return false;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                reason = "empty";
                return false;
            }

            var lines = SplitLines(text);
            if (lines.Count == 0)
            {
                reason = "no_lines";
                return false;
            }

            var first = lines[0].Trim();
            if (first.Equals("MWPV-ELOG|v1", StringComparison.OrdinalIgnoreCase))
            {
                return TryParseV1(lines, out payload, out rawJson, out reason);
            }
            else if (first.StartsWith("MWPV_ELOG_", StringComparison.OrdinalIgnoreCase))
            {
                return TryParseLegacy(lines, out payload, out rawJson, out reason);
            }

            reason = "bad_header";
            return false;
        }

        // V1: header line, optional "content-type: ..." lines, blank line, then JSON (possibly multi-line).
        // We rebuild the JSON from lines after the first blank line (no LINQ).
        private static bool TryParseV1(
            List<string> lines,
            out EarlyPayload payload,
            out string rawJson,
            out string reason)
        {
            payload = default!;
            rawJson = "";
            reason = "";

            int blankIndex = -1;
            for (int i = 1; i < lines.Count; i++)
            {
                if (lines[i].Trim().Length == 0) { blankIndex = i; break; }
            }
            if (blankIndex < 0 || blankIndex == lines.Count - 1)
            {
                reason = "json_missing";
                return false;
            }

            // Rebuild JSON from the remaining lines to avoid CR/LF position math.
            var sb = new StringBuilder();
            for (int i = blankIndex + 1; i < lines.Count; i++)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(lines[i]);
            }
            var jsonText = sb.ToString().Trim();
            if (jsonText.Length == 0)
            {
                reason = "json_missing";
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(jsonText);
                rawJson = jsonText;

                var root = doc.RootElement;

                DateTime tsUtc = DateTime.UtcNow;
                if (root.TryGetProperty("utc", out var utcEl) &&
                    utcEl.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(utcEl.GetString(), null,
                        System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
                {
                    tsUtc = parsed.ToUniversalTime();
                }

                string type = root.TryGetProperty("type", out var tEl) && tEl.ValueKind == JsonValueKind.String
                    ? (tEl.GetString() ?? "unknown") : "unknown";

                string userSid = root.TryGetProperty("userSid", out var sidEl) && sidEl.ValueKind == JsonValueKind.String
                    ? (sidEl.GetString() ?? "UnknownUser") : "UnknownUser";

                string machine = root.TryGetProperty("machine", out var mEl) && mEl.ValueKind == JsonValueKind.String
                    ? (mEl.GetString() ?? Environment.MachineName) : Environment.MachineName;

                string appVersion = root.TryGetProperty("appVersion", out var avEl) && avEl.ValueKind == JsonValueKind.String
                    ? (avEl.GetString() ?? "unknown") : "unknown";

                string message =
                    (root.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String
                        ? msgEl.GetString() ?? ""
                        : (root.TryGetProperty("details", out var detMsgEl) && detMsgEl.ValueKind == JsonValueKind.String
                            ? detMsgEl.GetString() ?? "" : ""));

                object? detailsObj = null;
                if (root.TryGetProperty("details", out var detEl))
                    detailsObj = JsonToBoxed(detEl);

                payload = new EarlyPayload
                {
                    tsUtc = tsUtc,
                    type = type,
                    userSid = userSid,
                    machine = machine,
                    appVersion = appVersion,
                    message = message,
                    details = detailsObj
                };
                return true;
            }
            catch (Exception ex)
            {
                reason = "json_parse_failed:" + ex.GetType().Name;
                return false;
            }
        }

        // Legacy “V2” best-effort parser:
        //   MWPV_ELOG_V2
        //   key=value
        //   ...
        //   json={...}
        private static bool TryParseLegacy(
            List<string> lines,
            out EarlyPayload payload,
            out string rawJson,
            out string reason)
        {
            payload = default!;
            rawJson = "";
            reason = "";

            var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? json = null;

            for (int i = 1; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("json=", StringComparison.OrdinalIgnoreCase))
                {
                    json = line.Substring(5).Trim();
                    break; // json is last; ignore anything after
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                kv[key] = val;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                reason = "legacy_missing_json";
                return false;
            }

            try
            {
                rawJson = json!;
                using var doc = JsonDocument.Parse(json!);
                var root = doc.RootElement;

                DateTime tsUtc = DateTime.UtcNow;
                if (root.TryGetProperty("tsUtc", out var tsEl) &&
                    tsEl.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(tsEl.GetString(), null,
                        System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
                {
                    tsUtc = parsed.ToUniversalTime();
                }

                string type =
                    (root.TryGetProperty("type", out var tEl) && tEl.ValueKind == JsonValueKind.String
                        ? tEl.GetString() ?? "unknown"
                        : (kv.TryGetValue("type", out var kvType) ? kvType : "unknown"));

                string userSid =
                    (root.TryGetProperty("userSid", out var sidEl) && sidEl.ValueKind == JsonValueKind.String
                        ? sidEl.GetString() ?? "UnknownUser"
                        : (kv.TryGetValue("userSid", out var kvSid) ? kvSid : "UnknownUser"));

                string machine =
                    (root.TryGetProperty("machine", out var mEl) && mEl.ValueKind == JsonValueKind.String
                        ? mEl.GetString() ?? Environment.MachineName
                        : (kv.TryGetValue("machine", out var kvM) ? kvM : Environment.MachineName));

                string appVersion =
                    (root.TryGetProperty("appVersion", out var avEl) && avEl.ValueKind == JsonValueKind.String
                        ? avEl.GetString() ?? "unknown"
                        : (kv.TryGetValue("appVersion", out var kvV) ? kvV : "unknown"));

                string message =
                    (root.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String
                        ? msgEl.GetString() ?? ""
                        : (kv.TryGetValue("message", out var kvMsg) ? kvMsg : ""));

                object? detailsObj = null;
                if (root.TryGetProperty("details", out var detEl))
                    detailsObj = JsonToBoxed(detEl);

                payload = new EarlyPayload
                {
                    tsUtc = tsUtc,
                    type = type,
                    userSid = userSid,
                    machine = machine,
                    appVersion = appVersion,
                    message = message,
                    details = detailsObj
                };
                return true;
            }
            catch (Exception ex)
            {
                reason = "legacy_json_parse_failed:" + ex.GetType().Name;
                return false;
            }
        }

        private static object? JsonToBoxed(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Null: return null;
                case JsonValueKind.String: return el.GetString();
                case JsonValueKind.Number:
                    if (el.TryGetInt64(out var i64)) return i64;
                    if (el.TryGetDouble(out var d)) return d;
                    return el.ToString();
                case JsonValueKind.True:
                case JsonValueKind.False: return el.GetBoolean();
                case JsonValueKind.Object:
                case JsonValueKind.Array: return el.GetRawText(); // preserve nested JSON
                default: return el.ToString();
            }
        }

        private static List<string> SplitLines(string text)
        {
            var list = new List<string>();
            using var sr = new StringReader(text);
            string? line;
            while ((line = sr.ReadLine()) is not null)
                list.Add(line);
            return list;
        }
    }
}
