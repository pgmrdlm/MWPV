using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MWPV.Services;

namespace MWPV.View.UserControls
{
    public partial class LogDetailsPanel : UserControl
    {
        public LogDetailsPanel()
        {
            InitializeComponent();
            btnCopy.Click += BtnCopy_Click;
            btnClear.Click += BtnClear_Click;

            // NEW: wipe when hidden
            IsVisibleChanged += (_, e) =>
            {
                if (e.NewValue is bool b && !b)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine("[LOGS][Details] IsVisible=false -> Clear()");
#endif
                    Clear();
                }
            };

            // NEW: wipe when unloaded (e.g., overlay closed or view swapped)
            Unloaded += (_, __) =>
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[LOGS][Details] Unloaded -> Clear()");
#endif
                Clear();
            };
        }

        // ===========================
        // Dependency Properties
        // ===========================
        public string EntryId
        {
            get => (string)GetValue(EntryIdProperty);
            set => SetValue(EntryIdProperty, value);
        }
        public static readonly DependencyProperty EntryIdProperty =
            DependencyProperty.Register(nameof(EntryId), typeof(string), typeof(LogDetailsPanel), new PropertyMetadata(""));

        public string CreatedText
        {
            get => (string)GetValue(CreatedTextProperty);
            set => SetValue(CreatedTextProperty, value);
        }
        public static readonly DependencyProperty CreatedTextProperty =
            DependencyProperty.Register(nameof(CreatedText), typeof(string), typeof(LogDetailsPanel), new PropertyMetadata(""));

        public string MetaText
        {
            get => (string)GetValue(MetaTextProperty);
            set => SetValue(MetaTextProperty, value);
        }
        public static readonly DependencyProperty MetaTextProperty =
            DependencyProperty.Register(nameof(MetaText), typeof(string), typeof(LogDetailsPanel), new PropertyMetadata(""));

        public string Message
        {
            get => (string)GetValue(MessageProperty);
            set => SetValue(MessageProperty, value);
        }
        public static readonly DependencyProperty MessageProperty =
            DependencyProperty.Register(nameof(Message), typeof(string), typeof(LogDetailsPanel), new PropertyMetadata(""));

        public string PayloadText
        {
            get => (string)GetValue(PayloadTextProperty);
            set => SetValue(PayloadTextProperty, value);
        }
        public static readonly DependencyProperty PayloadTextProperty =
            DependencyProperty.Register(nameof(PayloadText), typeof(string), typeof(LogDetailsPanel), new PropertyMetadata(""));

        public string PayloadFmt
        {
            get => (string)GetValue(PayloadFmtProperty);
            set => SetValue(PayloadFmtProperty, value);
        }
        public static readonly DependencyProperty PayloadFmtProperty =
            DependencyProperty.Register(nameof(PayloadFmt), typeof(string), typeof(LogDetailsPanel), new PropertyMetadata("none"));

        public int? PayloadSize
        {
            get => (int?)GetValue(PayloadSizeProperty);
            set => SetValue(PayloadSizeProperty, value);
        }
        public static readonly DependencyProperty PayloadSizeProperty =
            DependencyProperty.Register(nameof(PayloadSize), typeof(int?), typeof(LogDetailsPanel), new PropertyMetadata(null));

        // Keep the raw payload for copy/format operations if needed
        private string _rawPayload = "";
        private string _rawCreatedUtc = "";
        private string _rawLevel = "";
        private string _rawSource = "";
        private string _rawEvent = "";

        // ===========================
        // Public API
        // ===========================

        /// <summary>
        /// Load details metadata from a row already listed in the grid,
        /// then fetch full payload/info by Id.
        /// </summary>
        public async Task LoadFromAsync(MWPV.Models.Logs row)
        {
            if (row == null) { Clear(); return; }

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[LOGS][Details] Begin LoadFromAsync id={row.Id}");
#endif

            EntryId = row.Id.ToString(CultureInfo.InvariantCulture);
            CreatedText = SafeToUtcText(row.CreatedUtc);
            MetaText = $"{(row.Level ?? "").Trim()} / {(row.Source ?? "").Trim()} / {(row.EventCode ?? "").Trim()}".Trim().Trim('/').Trim();
            Message = row.Message ?? "";

            _rawLevel = row.Level ?? "";
            _rawSource = row.Source ?? "";
            _rawEvent = row.EventCode ?? "";

            PayloadFmt = NormalizeFmt(row.PayloadFmt);
            PayloadSize = row.PayloadSize;

            await LoadPayloadAsync(row.Id);

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[LOGS][Details] End LoadFromAsync id={row.Id}");
#endif
        }

        /// <summary>
        /// Explicitly clear the panel UI and backing fields.
        /// </summary>
        public void Clear()
        {
            EntryId = "";
            CreatedText = "";
            MetaText = "";
            Message = "";

            // Defensive: overwrite large strings first where feasible
            if (!string.IsNullOrEmpty(_rawPayload))
            {
                try
                {
                    // Replace visible payload text with cheap placeholder before dropping reference
                    PayloadText = "";
                }
                catch { /* ignore */ }
            }

            PayloadText = "";
            PayloadFmt = "none";
            PayloadSize = null;

            _rawPayload = "";
            _rawCreatedUtc = "";
            _rawLevel = _rawSource = _rawEvent = "";
        }

        // ===========================
        // Internals
        // ===========================

        private async Task LoadPayloadAsync(long id)
        {
            try
            {
                var rec = await Task.Run(() => LogCatalogService.SelectById(id));
                if (rec == null)
                {
                    _rawPayload = "";
                    PayloadText = "(not found)";
                    return;
                }

                // Canonical meta refresh
                _rawCreatedUtc = rec.CreatedUtc ?? "";
                CreatedText = SafeToUtcText(_rawCreatedUtc);
                _rawLevel = rec.Level ?? _rawLevel;
                _rawSource = rec.Source ?? _rawSource;
                _rawEvent = rec.EventCode ?? _rawEvent;
                MetaText = $"{_rawLevel.Trim()} / {_rawSource.Trim()} / {_rawEvent.Trim()}".Trim().Trim('/').Trim();

                // Payload
                _rawPayload = rec.Payload ?? "";
                var fmt = NormalizeFmt(rec.PayloadFmt);
                PayloadFmt = fmt;
                PayloadSize = rec.PayloadSize;

                PayloadText = FriendlyOrRaw(_rawPayload, fmt);
            }
            catch (Exception ex)
            {
                _rawPayload = "";
                PayloadText = $"(error loading payload)\r\n{ex.Message}";
            }
        }

        private static string NormalizeFmt(string? fmt)
        {
            if (string.IsNullOrWhiteSpace(fmt)) return "none";
            var f = fmt.Trim();
            // Treat anything containing "json" as json-ish
            return f.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0 ? "json" : f.ToLowerInvariant();
        }

        /// <summary>
        /// Render JSON in a friendlier, non-braced, non-quoted, capitalized-keys way.
        /// Fallback to raw for non-JSON formats.
        /// </summary>
        private static string FriendlyOrRaw(string payload, string? fmt)
        {
            if (string.IsNullOrWhiteSpace(payload)) return "(none)";
            var isJson = string.Equals(fmt, "json", StringComparison.OrdinalIgnoreCase);

            if (!isJson) return payload;

            try
            {
                using var doc = JsonDocument.Parse(payload);
                var sb = new StringBuilder();
                AppendFriendlyJson(doc.RootElement, sb, 0);
                return sb.ToString();
            }
            catch
            {
                return payload; // not valid JSON — show raw
            }
        }

        private static void AppendFriendlyJson(JsonElement el, StringBuilder sb, int indent)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var prop in el.EnumerateObject())
                    {
                        var label = CapitalizeFirst(prop.Name);
                        if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        {
                            AppendLine(sb, indent, $"{label}:");
                            AppendFriendlyJson(prop.Value, sb, indent + 2);
                        }
                        else
                        {
                            AppendLine(sb, indent, $"{label}: {ScalarToText(prop.Value)}");
                        }
                    }
                    break;

                case JsonValueKind.Array:
                    int i = 1;
                    foreach (var item in el.EnumerateArray())
                    {
                        if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        {
                            AppendLine(sb, indent, $"- Item {i}:");
                            AppendFriendlyJson(item, sb, indent + 2);
                        }
                        else
                        {
                            AppendLine(sb, indent, $"- {ScalarToText(item)}");
                        }
                        i++;
                    }
                    break;

                default:
                    AppendLine(sb, indent, ScalarToText(el));
                    break;
            }
        }

        private static string ScalarToText(JsonElement v)
        {
            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? "",
                JsonValueKind.Number => v.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "(null)",
                _ => v.ToString()
            };
        }

        private static void AppendLine(StringBuilder sb, int indent, string text)
        {
            if (indent > 0) sb.Append(' ', indent);
            sb.AppendLine(text);
        }

        private static string CapitalizeFirst(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (char.IsUpper(s[0])) return s;
            if (s.Length == 1) return s.ToUpperInvariant();
            return char.ToUpperInvariant(s[0]) + s[1..];
        }

        private static string SafeToUtcText(string? iso)
        {
            if (!string.IsNullOrWhiteSpace(iso))
            {
                if (DateTimeOffset.TryParse(
                        iso, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var dto))
                {
                    return dto.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                }

                if (DateTime.TryParse(
                        iso, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var dt))
                {
                    return DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                                   .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                }
            }
            return "";
        }

        // ===========================
        // UI Events
        // ===========================
        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Compose a friendly details block for copy
                // Id / Created / Meta / Message / Payload (friendly)
                var sb = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(EntryId)) sb.AppendLine($"Id: {EntryId}");
                if (!string.IsNullOrWhiteSpace(CreatedText)) sb.AppendLine($"Created (UTC): {CreatedText}");
                if (!string.IsNullOrWhiteSpace(MetaText)) sb.AppendLine($"Level / Source / Event: {MetaText}");
                if (!string.IsNullOrWhiteSpace(Message)) sb.AppendLine($"Message: {Message}");

                if (!string.IsNullOrEmpty(_rawPayload))
                {
                    sb.AppendLine();
                    sb.AppendLine("Payload");
                    sb.AppendLine("----------------------------------------");
                    sb.Append(FriendlyOrRaw(_rawPayload, PayloadFmt));
                }

                Clipboard.SetText(sb.ToString());
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[LOGS][Details] Copied details to clipboard.");
#endif
            }
            catch
            {
                // ignore clipboard exceptions
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine("[LOGS][Details] Clear button clicked.");
#endif
            Clear();
        }
    }
}
