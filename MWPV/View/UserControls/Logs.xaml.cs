using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using MWPV.Services;
using MWPV.View.Windows;
using LogModel = MWPV.Models.Logs;

namespace MWPV.View.UserControls
{
    public partial class Logs : UserControl
    {
        public event EventHandler? CloseRequested;

        private readonly ObservableCollection<LogModel> _rows = new();

        public Logs()
        {
            InitializeComponent();

            dgLogs.ItemsSource = _rows;
            btnClose.Click += (_, __) => CloseRequested?.Invoke(this, EventArgs.Empty);
            btnRefresh.Click += (_, __) => LoadRecent();

            Loaded += (_, __) => LoadRecent();
        }

        private void LoadRecent()
        {
            _rows.Clear();
            var list = LogCatalogService.SelectRecent(200);
            foreach (var r in list ?? Enumerable.Empty<LogModel>())
                _rows.Add(r);
        }

        private void ViewLog_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is LogModel row)
                ShowDetails(row);
        }

        private void dgLogs_MouseDoubleClick(object? sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (dgLogs.SelectedItem is LogModel row)
                ShowDetails(row);
        }

        private void ShowDetails(LogModel row)
        {
            var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;

            var id = SafeId(row);
            if (id <= 0)
            {
                MessageBox.Show(owner!, "Invalid log id.", "Log Details",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var full = LogCatalogService.SelectById(id);
            if (full == null)
            {
                MessageBox.Show(owner!, "Could not load details for this log row.", "Log Details",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var entry = new LogEntry
            {
                Id = id.ToString(CultureInfo.InvariantCulture),
                CreatedUtc = ConvertCreatedUtc(row),
                Level = TryGet<string>(row, "Level") ?? full.Level,
                Source = TryGet<string>(row, "Source") ?? full.Source,
                EventCode = TryGet<string>(row, "EventCode") ?? full.EventCode,
                Message = null, // schema has no Message column
                PayloadFmt = full.PayloadFmt ?? "none",
                PayloadSize = full.PayloadSize,
                Payload = full.Payload
            };

            LogDetailsWindow.ShowSingle(owner!, entry);
        }

        private static long SafeId(object row)
        {
            var pi = row.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
            var val = pi?.GetValue(row);
            try
            {
                return val switch
                {
                    null => 0L,
                    long l => l,
                    int i => i,
                    string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l2) => l2,
                    IConvertible c => Convert.ToInt64(c, CultureInfo.InvariantCulture),
                    _ => 0L
                };
            }
            catch { return 0L; }
        }

        private static DateTime ConvertCreatedUtc(object row)
        {
            var pi = row.GetType().GetProperty("CreatedUtc", BindingFlags.Public | BindingFlags.Instance);
            if (pi == null) return DateTime.MinValue;
            var val = pi.GetValue(row);

            if (val is DateTime dt)
            {
                if (dt.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
            }

            if (val is string s && !string.IsNullOrWhiteSpace(s))
            {
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var iso))
                    return iso.Kind == DateTimeKind.Utc ? iso : iso.ToUniversalTime();

                if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var assumed))
                    return assumed.ToUniversalTime();

                if (DateTime.TryParse(s, out var any))
                    return any.Kind == DateTimeKind.Utc ? any : any.ToUniversalTime();
            }

            return DateTime.MinValue;
        }

        private static T? TryGet<T>(object obj, string propertyName)
        {
            var pi = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (pi == null) return default;
            var val = pi.GetValue(obj);
            if (val is null) return default;

            try
            {
                if (val is T tVal) return tVal;
                var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
                return (T?)Convert.ChangeType(val, target, CultureInfo.InvariantCulture);
            }
            catch
            {
                return default;
            }
        }
    }
}
