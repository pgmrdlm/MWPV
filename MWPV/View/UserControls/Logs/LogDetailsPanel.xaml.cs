using System;
using System.Globalization;
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

            // Wipe when hidden
            IsVisibleChanged += (_, e) =>
            {
                if (e.NewValue is bool b && !b)
                {
                    Clear();
                }
            };

            // Wipe when unloaded (e.g., overlay closed or view swapped)
            Unloaded += (_, __) =>
            {
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

        // Backing fields for stable meta refresh
        private string _rawCreatedUtc = "";
        private string _rawLevel = "";
        private string _rawSource = "";
        private string _rawEvent = "";

        // ===========================
        // Public API
        // ===========================

        /// <summary>
        /// Load details from a grid row, then refresh canonical meta by Id (NO payload).
        /// Message stays from the row to avoid schema / record mismatches.
        /// </summary>
        public async Task LoadFromAsync(MWPV.Models.Logs row)
        {
            if (row == null)
            {
                Clear();
                return;
            }


            EntryId = row.Id.ToString(CultureInfo.InvariantCulture);
            CreatedText = SafeToUtcText(row.CreatedUtc);

            _rawLevel = row.Level ?? "";
            _rawSource = row.Source ?? "";
            _rawEvent = row.EventCode ?? "";
            _rawCreatedUtc = row.CreatedUtc ?? "";

            MetaText = BuildMeta(_rawLevel, _rawSource, _rawEvent);
            Message = row.MessageText ?? "";

            await RefreshMetaByIdAsync(row.Id).ConfigureAwait(false);

        }

        /// <summary>
        /// Clear the panel UI and backing fields.
        /// </summary>
        public void Clear()
        {
            EntryId = "";
            CreatedText = "";
            MetaText = "";
            Message = "";

            _rawCreatedUtc = "";
            _rawLevel = "";
            _rawSource = "";
            _rawEvent = "";
        }

        // ===========================
        // Internals
        // ===========================

        /// <summary>
        /// Refresh only canonical meta fields by Id.
        /// Intentionally DOES NOT touch Message and DOES NOT deal with payload.
        /// </summary>
        private async Task RefreshMetaByIdAsync(long id)
        {
            try
            {
                var rec = await Task.Run(() => LogCatalogService.SelectById(id)).ConfigureAwait(false);
                if (rec == null) return;

                // CreatedUtc refresh (if present)
                var created = rec.CreatedUtc ?? "";
                if (!string.IsNullOrWhiteSpace(created))
                {
                    _rawCreatedUtc = created;
                    CreatedText = SafeToUtcText(_rawCreatedUtc);
                }

                // Meta refresh (only if present)
                if (!string.IsNullOrWhiteSpace(rec.Level)) _rawLevel = rec.Level!;
                if (!string.IsNullOrWhiteSpace(rec.Source)) _rawSource = rec.Source!;
                if (!string.IsNullOrWhiteSpace(rec.EventCode)) _rawEvent = rec.EventCode!;

                MetaText = BuildMeta(_rawLevel, _rawSource, _rawEvent);
            }
            catch
            {
                // Intentionally quiet: details panel should not throw/pop dialogs
            }
        }

        private static string BuildMeta(string level, string source, string eventCode)
        {
            var l = (level ?? "").Trim();
            var s = (source ?? "").Trim();
            var e = (eventCode ?? "").Trim();

            // Keep it consistent: "LEVEL / SOURCE / EVENT"
            var meta = $"{l} / {s} / {e}";
            return meta.Trim().Trim('/').Trim();
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
    }
}
