
// File: MWPV/Services/TemplateLogWriter.cs
//
// NEW FILE
//
// Purpose:
// - Centralize template lookup (LogMessageTemplate), token expansion, and log row insert (Logs.SubjectText/MessageText).
// - Best-effort logging: callers can choose to swallow failures without blocking UX.
//
// Notes:
// - Does NOT write any JSON payload.
// - Uses existing LogCatalogService.SelectActiveLogMessageTemplates() + LogCatalogService.Insert(...).
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MWPV.Services
{
    public static class TemplateLogWriter
    {
        // ----------------------------
        // Public API
        // ----------------------------

        public sealed class WriteRequest
        {
            public string Level { get; set; } = "INFO";
            public string Source { get; set; } = "";
            public string EventCode { get; set; } = "";

            // Optional foreign key for item-based logs (CategoryItem, etc.)
            public long? ItemId { get; set; } = null;

            // Rendered UI fields
            public string? SubjectText { get; set; } = null;
            public string? MessageText { get; set; } = null;

            // Required by s_Logs_Insert.sql (per existing LogCatalogService.Insert)
            public int KeySetVersion { get; set; } = 1;

            // Optional timestamps (UTC). If null, defaults to DateTime.UtcNow.
            public DateTime? WhenUtc { get; set; } = null;
            public DateTime? CreatedUtc { get; set; } = null;
        }

        /// <summary>
        /// Builds MessageText from LogMessageTemplate rows for a single UpdateForm using Seq numbers,
        /// expands tokens, and inserts into Logs via LogCatalogService.Insert.
        /// </summary>
        public static long InsertFromTemplates(
            string updateForm,
            IEnumerable<int> seqsInOrder,
            IReadOnlyDictionary<string, string?> tokens,
            WriteRequest write)
        {
            if (write == null) throw new ArgumentNullException(nameof(write));
            if (string.IsNullOrWhiteSpace(updateForm)) throw new ArgumentException("updateForm is required.", nameof(updateForm));
            if (seqsInOrder == null) throw new ArgumentNullException(nameof(seqsInOrder));

            var message = BuildMessageFromTemplates(updateForm, seqsInOrder, tokens);

            if (string.IsNullOrWhiteSpace(message))
                return -1;

            write.MessageText = message;

            return InsertRendered(write);
        }

        /// <summary>
        /// Same as InsertFromTemplates, but it never throws. Returns -1 on failure.
        /// </summary>
        public static long InsertFromTemplates_BestEffort(
            string updateForm,
            IEnumerable<int> seqsInOrder,
            IReadOnlyDictionary<string, string?> tokens,
            WriteRequest write)
        {
            try
            {
                return InsertFromTemplates(updateForm, seqsInOrder, tokens, write);
            }
            catch (Exception ex)
            {
                return -1;
            }
        }

        /// <summary>
        /// Inserts a log row when SubjectText/MessageText are already rendered.
        /// </summary>
        public static long InsertRendered(WriteRequest write)
        {
            if (write == null) throw new ArgumentNullException(nameof(write));

            var createdIso = (write.CreatedUtc ?? DateTime.UtcNow).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            var whenIso = (write.WhenUtc ?? write.CreatedUtc ?? DateTime.UtcNow).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

            var req = new LogCatalogService.RequestV3
            {
                Level = write.Level ?? "INFO",
                Source = write.Source ?? "",
                EventCode = write.EventCode ?? "",
                CreatedUtc = createdIso,
                WhenUtc = whenIso,

                ItemId = write.ItemId,

                SubjectText = write.SubjectText,
                MessageText = write.MessageText,

                KeySetVersion = write.KeySetVersion
            };

            return LogCatalogService.Insert(req);
        }

        /// <summary>
        /// Same as InsertRendered, but it never throws. Returns -1 on failure.
        /// </summary>
        public static long InsertRendered_BestEffort(WriteRequest write)
        {
            try
            {
                return InsertRendered(write);
            }
            catch (Exception ex)
            {
                return -1;
            }
        }

        /// <summary>
        /// Renders one template by UpdateForm+Seq and expands tokens.
        /// Returns null if not found/active.
        /// </summary>
        public static string? RenderTemplate(string updateForm, int seq, IReadOnlyDictionary<string, string?> tokens)
        {
            if (string.IsNullOrWhiteSpace(updateForm)) return null;

            var map = GetTemplateMap(updateForm);
            if (!map.TryGetValue(seq, out var template) || string.IsNullOrWhiteSpace(template))
                return null;

            return ExpandTokens(template, tokens);
        }

        /// <summary>
        /// Same as RenderTemplate, but it never throws. Returns null on failure.
        /// </summary>
        public static string? RenderTemplate_BestEffort(string updateForm, int seq, IReadOnlyDictionary<string, string?> tokens)
        {
            try
            {
                return RenderTemplate(updateForm, seq, tokens);
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        /// <summary>
        /// Builds a message (multi-line) from templates for updateForm + seq list.
        /// Returns null if no lines were produced.
        /// </summary>
        public static string? BuildMessageFromTemplates(
            string updateForm,
            IEnumerable<int> seqsInOrder,
            IReadOnlyDictionary<string, string?> tokens)
        {
            if (string.IsNullOrWhiteSpace(updateForm)) throw new ArgumentException("updateForm is required.", nameof(updateForm));
            if (seqsInOrder == null) throw new ArgumentNullException(nameof(seqsInOrder));

            var templates = GetTemplateMap(updateForm);

            var lines = new List<string>();
            foreach (var seq in seqsInOrder)
            {
                if (!templates.TryGetValue(seq, out var raw) || string.IsNullOrWhiteSpace(raw))
                    continue;

                var expanded = ExpandTokens(raw, tokens);
                if (!string.IsNullOrWhiteSpace(expanded))
                    lines.Add(expanded);
            }

            if (lines.Count == 0)
                return null;

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Same as BuildMessageFromTemplates, but it never throws. Returns null on failure / no lines.
        /// </summary>
        public static string? BuildMessageFromTemplates_BestEffort(
            string updateForm,
            IEnumerable<int> seqsInOrder,
            IReadOnlyDictionary<string, string?> tokens)
        {
            try
            {
                return BuildMessageFromTemplates(updateForm, seqsInOrder, tokens);
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        // ----------------------------
        // Internals
        // ----------------------------

        private static Dictionary<int, string> GetTemplateMap(string updateForm)
        {
            var dict = new Dictionary<int, string>();

            var rows = LogCatalogService.SelectActiveLogMessageTemplates();

            foreach (var r in rows)
            {
                if (!r.Active) continue;
                if (!string.Equals(r.UpdateForm, updateForm, StringComparison.Ordinal)) continue;

                // First wins to avoid duplicates causing drift
                if (!dict.ContainsKey(r.Seq))
                    dict.Add(r.Seq, r.LogMessage ?? string.Empty);
            }

            return dict;
        }

        /// <summary>
        /// Token expansion:
        /// - Replaces occurrences of #TokenName# using provided tokens dictionary.
        /// - Unknown tokens remain unchanged.
        /// </summary>
        private static string ExpandTokens(string template, IReadOnlyDictionary<string, string?> tokens)
        {
            if (string.IsNullOrEmpty(template) || tokens == null || tokens.Count == 0)
                return template ?? string.Empty;

            string s = template;

            // Replace longer keys first (avoids partial overlap issues)
            foreach (var kv in tokens.OrderByDescending(k => k.Key?.Length ?? 0))
            {
                var key = kv.Key ?? string.Empty;
                if (key.Length == 0) continue;

                var placeholder = $"#{key}#";
                var value = kv.Value ?? string.Empty;

                s = s.Replace(placeholder, value);
            }

            return s;
        }
    }
}
