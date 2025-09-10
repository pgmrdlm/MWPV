using System;
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

            txtId.Text = entry.Id;
            txtCreated.Text = entry.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss");
            txtLevel.Text = entry.Level;
            txtSource.Text = entry.Source;
            txtEvent.Text = entry.EventCode;

            txtFmt.Text = entry.PayloadFmt ?? "none";
            txtSize.Text = entry.PayloadSize.ToString();

            // Pretty print JSON if applicable
            if (!string.IsNullOrWhiteSpace(entry.Payload) &&
                string.Equals(entry.PayloadFmt, "json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var doc = JsonDocument.Parse(entry.Payload);
                    txtPayload.Text = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
                }
                catch
                {
                    // if it isn't valid json, just show the raw text
                    txtPayload.Text = entry.Payload;
                }
            }
            else
            {
                txtPayload.Text = entry.Payload ?? "";
            }
        }

        private void btnCopy_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(txtPayload.Text ?? ""); }
            catch { /* ignore */ }
        }

        private void btnClose_Click(object? sender, RoutedEventArgs e) => Close();
    }
}
