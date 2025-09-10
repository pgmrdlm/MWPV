// File: MWPV/View/Windows/LogDetailsWindow.cs
using System;
using System.Globalization;
using System.Text.Json;
using System.Windows;

namespace MWPV.View.Windows
{
    public partial class LogDetailsWindow : Window
    {
        private LogEntry? _entry;

        public LogDetailsWindow()
        {
            InitializeComponent();
        }

        public static void ShowSingle(Window owner, LogEntry entry)
        {
            var w = new LogDetailsWindow { Owner = owner };
            w.Load(entry);
            w.ShowDialog();
        }

        public void Load(LogEntry entry)
        {
            _entry = entry;

            txtId.Text = entry.Id ?? string.Empty;
            txtCreated.Text = entry.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            txtLevel.Text = entry.Level ?? string.Empty;
            txtSource.Text = entry.Source ?? string.Empty;
            txtEvent.Text = entry.EventCode ?? string.Empty;

            txtFmt.Text = string.IsNullOrWhiteSpace(entry.PayloadFmt) ? "none" : entry.PayloadFmt!;
            txtSize.Text = entry.PayloadSize.ToString(CultureInfo.InvariantCulture);

            txtPayload.Text = PrettyOrRaw(entry.Payload ?? string.Empty, entry.PayloadFmt);
        }

        private static string PrettyOrRaw(string payload, string? fmt)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return "(none)";

            if (!string.Equals(fmt, "json", StringComparison.OrdinalIgnoreCase))
                return payload;

            try
            {
                using var doc = JsonDocument.Parse(payload);
                return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                return payload; // not JSON — show raw
            }
        }

        private void btnCopy_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(txtPayload.Text ?? string.Empty); } catch { }
        }

        private void btnClose_Click(object? sender, RoutedEventArgs e) => Close();
    }
}
