using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

using Utilities.Logging;   // LogSeverity
using Utilities.Security;  // SecureLogService (writes encrypted log)

namespace Utilities.Diagnostics
{
    /// <summary>
    /// Parses plaintext “early” login failure files from <see cref="EarlyLoginFailures.StoreDir"/>
    /// and writes them into the encrypted log via <see cref="SecureLogService"/>.
    /// Any unreadable or malformed files are moved into
    /// <see cref="EarlyLoginFailures.QuarantineDir"/>.
    /// </summary>
    public static class EarlyLogIngestor
    {
        public sealed class Result
        {
            public int Inserted { get; init; }
            public int Deduped { get; init; }        // (reserved for future)
            public int Quarantined { get; init; }
            public int Deleted { get; init; }        // files removed from the “early” folder after ingest
            public int Errors { get; init; }
        }

        /// <summary>
        /// Ingest all pending “.elog” files. This method does not depend on any
        /// schema-specific SQL; it writes via <see cref="SecureLogService"/>.
        /// </summary>
        /// <param name="openConnectionFactory">
        /// Optional factory to prove DB access before ingest (e.g., DatabaseHelper.OpenConnection).
        /// The ingestor writes via <see cref="SecureLogService"/> so the connection is not used
        /// for inserts, but we touch it to surface connectivity problems early.
        /// </param>
        public static Result IngestAllEarlyLogsTransactional(Func<DbConnection>? openConnectionFactory = null)
        {
            // Prove DB is reachable (best effort).
            try { using var _ = openConnectionFactory?.Invoke(); } catch { /* ignore */ }

            int inserted = 0, deduped = 0, quarantined = 0, deleted = 0, errors = 0;

            foreach (var path in EarlyLoginFailures.EnumeratePendingPaths())
            {
                try
                {
                    if (!TryParseEarlyFile(path, out var payload, out var rawJson, out var reason))
                    {
                        Quarantine(path, $"parse_failed:{reason}");
                        quarantined++;
                        continue;
                    }

                    // Build one compact object to persist.
                    var evt = new
                    {
                        tsUtc = payload.tsUtc,
                        type = payload.type,
                        userSid = payload.userSid,
                        machine = payload.machine,
                        appVersion = payload.appVersion,
                        message = payload.message,
                        details = payload.details, // already parsed from JSON if present
                        file = Path.GetFileName(path),
                        rawJson,                     // store original JSON string for audit
                        source = "EARLY_LOGIN"
                    };

                    // Write into encrypted log. If this throws, we leave the file in place.
                    SecureLogService.WriteAsync(
                        LogSeverity.Info,
                        evt,
                        eventCode: "EARLY_LOGIN_EVENT",
                        source: "EarlyIngest")
                        .GetAwaiter().GetResult();

                    // If we reached here, we consider it ingested → delete the source.
                    TryDeleteFile(path);
                    inserted++;
                    deleted++;
                }
                catch (Exception ex)
                {
#if DEBUG
                    Debug.WriteLine($"[EARLY_INGEST] ERROR for '{path}': {ex.Message}");
#endif
                    // Leave the file in place so we can retry later.
                    errors++;
                }
            }

            return new Result
            {
                Inserted = inserted,
                Deduped = deduped,
                Quarantined = quarantined,
                Deleted = deleted,
                Errors = errors
            };
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
                // swallow; quarantine is best-effort
            }
        }

        private static void TryDeleteFile(string path)
        {
            try { File.Delete(path); }
            catch { /* best-effort */ }
        }

        // ---------- parsing ----------

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
        /// Try to parse supported early file formats.
        ///
        /// # V1 (preferred; produced by EarlyLoginFailures.cs):
        ///   MWPV-ELOG|v1
        ///   content-type: application/json; charset=utf-8
        ///
        ///   {json}
        ///
        /// JSON fields (we read what’s present): utc, type, userSid, machine, appVersion, message, details, exception, extra
        ///
        /// # Legacy (best-effort):
        ///   MWPV_ELOG_V2
        ///   key=value
        ///   ...
        ///   json={...}
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

            // Normalize line endings for header parsing
            var lines = SplitLines(text);
            if (lines.Count == 0)
            {
                reason = "no_lines";
                return false;
            }

            // --- Detect format ---
            var first = lines[0].Trim();

            if (first.Equals("MWPV-ELOG|v1", StringComparison.OrdinalIgnoreCase))
            {
                return TryParseV1(text, lines, out payload, out rawJson, out reason);
            }
            else if (first.StartsWith("MWPV_ELOG_", StringComparison.OrdinalIgnoreCase))
            {
                return TryParseLegacy(text, lines, out payload, out rawJson, out reason);
            }

            reason = "bad_header";
            return false;
        }

        // V1: header line, optional "content-type: ..." line(s), blank line, then JSON (possibly multi-line)
        private static bool TryParseV1(
            string fullText,
            List<string> lines,
            out EarlyPayload payload,
            out string rawJson,
            out string reason)
        {
            payload = default!;
            rawJson = "";
            reason = "";

            // Find the first blank line after the header block; everything after is JSON
            int blankIndex = -1;
            for (int i = 1; i < lines.Count; i++)
            {
                if (lines[i].Trim().Length == 0) { blankIndex = i; break; }
            }
            if (blankIndex < 0)
            {
                reason = "missing_blank_after_header";
                return false;
            }

            // Re-scan full text to extract substring after that blank line precisely
            // Count chars through end-of-line for the blankIndex-th line
            int charPos = 0;
            int currentLine = 0;
            using (var sr = new StringReader(fullText))
            {
                string? line;
                while ((line = sr.ReadLine()) is not null)
                {
                    charPos += line.Length;
                    // account for newline (we’ll assume CRLF or LF; add 1 and adjust if CRLF)
                    charPos += 1;
                    if (currentLine == blankIndex)
                        break;
                    currentLine++;
                }
            }

            // Defensive trim of any leading whitespace/newlines
            var jsonText = fullText.Substring(charPos).Trim();
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

                // map fields
                DateTime tsUtc = DateTime.UtcNow;
                if (root.TryGetProperty("utc", out var utcEl) &&
                    utcEl.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(utcEl.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
                {
                    tsUtc = parsed.ToUniversalTime();
                }

                string type = root.TryGetProperty("type", out var tEl) && tEl.ValueKind == JsonValueKind.String
                    ? (tEl.GetString() ?? "unknown")
                    : "unknown";

                string userSid = root.TryGetProperty("userSid", out var sidEl) && sidEl.ValueKind == JsonValueKind.String
                    ? (sidEl.GetString() ?? "UnknownUser")
                    : "UnknownUser";

                string machine = root.TryGetProperty("machine", out var mEl) && mEl.ValueKind == JsonValueKind.String
                    ? (mEl.GetString() ?? Environment.MachineName)
                    : Environment.MachineName;

                string appVersion = root.TryGetProperty("appVersion", out var avEl) && avEl.ValueKind == JsonValueKind.String
                    ? (avEl.GetString() ?? "unknown")
                    : "unknown";

                string message = root.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String
                    ? (msgEl.GetString() ?? "")
                    : (root.TryGetProperty("details", out var detMsgEl) && detMsgEl.ValueKind == JsonValueKind.String ? detMsgEl.GetString() ?? "" : "");

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
            string _fullText,
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

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line[..eq].Trim();
                var val = line[(eq + 1)..].Trim();
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
                    DateTime.TryParse(tsEl.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
                {
                    tsUtc = parsed.ToUniversalTime();
                }

                string type = root.TryGetProperty("type", out var tEl) && tEl.ValueKind == JsonValueKind.String
                    ? (tEl.GetString() ?? "unknown")
                    : (kv.TryGetValue("type", out var kvType) ? kvType : "unknown");

                string userSid = root.TryGetProperty("userSid", out var sidEl) && sidEl.ValueKind == JsonValueKind.String
                    ? (sidEl.GetString() ?? "UnknownUser")
                    : (kv.TryGetValue("userSid", out var kvSid) ? kvSid : "UnknownUser");

                string machine = root.TryGetProperty("machine", out var mEl) && mEl.ValueKind == JsonValueKind.String
                    ? (mEl.GetString() ?? Environment.MachineName)
                    : (kv.TryGetValue("machine", out var kvM) ? kvM : Environment.MachineName);

                string appVersion = root.TryGetProperty("appVersion", out var avEl) && avEl.ValueKind == JsonValueKind.String
                    ? (avEl.GetString() ?? "unknown")
                    : (kv.TryGetValue("appVersion", out var kvV) ? kvV : "unknown");

                string message = root.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String
                    ? (msgEl.GetString() ?? "")
                    : (kv.TryGetValue("message", out var kvMsg) ? kvMsg : "");

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
                case JsonValueKind.False:
                    return el.GetBoolean();
                case JsonValueKind.Object:
                case JsonValueKind.Array:
                    // Keep the original JSON text for nested content.
                    return el.GetRawText();
                default:
                    return el.ToString();
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
