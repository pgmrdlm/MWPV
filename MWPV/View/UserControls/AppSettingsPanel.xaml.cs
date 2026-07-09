using System;
using System.Windows;
using System.Windows.Controls;
using MWPV.Services;

namespace MWPV.View.UserControls
{
    public partial class AppSettingsPanel : UserControl
    {
        public event EventHandler? SaveRequested;
        public event EventHandler? CancelRequested;

        private bool _isLoading;
        private bool _isDirty;

        public bool IsDirty => _isDirty;

        public AppSettingsPanel()
        {
            InitializeComponent();
            txtPasswordMinimum.TextChanged += (_, __) => MarkDirty();
            txtClipboardClearSeconds.TextChanged += (_, __) => MarkDirty();
            txtLogRetentionDays.TextChanged += (_, __) => MarkDirty();
            txtBackupRetentionCount.TextChanged += (_, __) => MarkDirty();
            chkIncludeSymbols.Checked += (_, __) => MarkDirty();
            chkIncludeSymbols.Unchecked += (_, __) => MarkDirty();
            Loaded += (_, __) => LoadSettings();
        }

        public void LoadSettings()
        {
            _isLoading = true;
            try
            {
                ApplyToUi(AppSettingsService.LoadEditableSettings());
                SetStatus(string.Empty);
                _isDirty = false;
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void ApplyToUi(EditableAppSettings settings)
        {
            txtPasswordMinimum.Text = settings.SavedItemPasswordMinimum.ToString();
            chkIncludeSymbols.IsChecked = settings.IncludeSymbols;
            txtClipboardClearSeconds.Text = settings.ClipboardClearSeconds.ToString();
            txtLogRetentionDays.Text = settings.LogRetentionDays.ToString();
            txtBackupRetentionCount.Text = settings.BackupRetentionCount.ToString();
        }

        private void MarkDirty()
        {
            if (_isLoading)
                return;

            _isDirty = true;
            SetStatus(string.Empty);
        }

        private void ResetPasswordMinimum()
        {
            txtPasswordMinimum.Text = EditableAppSettings.Defaults().SavedItemPasswordMinimum.ToString();
            MarkDirty();
        }

        private void ResetIncludeSymbols()
        {
            chkIncludeSymbols.IsChecked = EditableAppSettings.Defaults().IncludeSymbols;
            MarkDirty();
        }

        private void ResetClipboard()
        {
            txtClipboardClearSeconds.Text = EditableAppSettings.Defaults().ClipboardClearSeconds.ToString();
            MarkDirty();
        }

        private void ResetLogRetention()
        {
            txtLogRetentionDays.Text = EditableAppSettings.Defaults().LogRetentionDays.ToString();
            MarkDirty();
        }

        private void ResetBackupRetention()
        {
            txtBackupRetentionCount.Text = EditableAppSettings.Defaults().BackupRetentionCount.ToString();
            MarkDirty();
        }

        private void btnResetPasswordMinimum_Click(object sender, RoutedEventArgs e)
        {
            ResetPasswordMinimum();
            ResetIncludeSymbols();
        }

        private void btnResetClipboard_Click(object sender, RoutedEventArgs e) => ResetClipboard();

        private void btnResetLogRetention_Click(object sender, RoutedEventArgs e) => ResetLogRetention();

        private void btnResetBackupRetention_Click(object sender, RoutedEventArgs e) => ResetBackupRetention();

        private void btnResetAll_Click(object sender, RoutedEventArgs e)
        {
            ResetPasswordMinimum();
            ResetIncludeSymbols();
            ResetClipboard();
            ResetLogRetention();
            ResetBackupRetention();
        }

        private bool TryReadPendingSettings(out EditableAppSettings settings, out string message)
        {
            settings = new EditableAppSettings();
            message = string.Empty;

            if (!TryReadInt(txtPasswordMinimum.Text, "Saved-item password minimum", out int passwordMinimum, out message) ||
                !TryReadInt(txtClipboardClearSeconds.Text, "Clipboard auto-clear", out int clipboardSeconds, out message) ||
                !TryReadInt(txtLogRetentionDays.Text, "Log retention", out int logRetentionDays, out message) ||
                !TryReadInt(txtBackupRetentionCount.Text, "Backup retention", out int backupRetentionCount, out message))
            {
                return false;
            }

            settings.SavedItemPasswordMinimum = passwordMinimum;
            settings.IncludeSymbols = chkIncludeSymbols.IsChecked == true;
            settings.ClipboardClearSeconds = clipboardSeconds;
            settings.LogRetentionDays = logRetentionDays;
            settings.BackupRetentionCount = backupRetentionCount;

            return AppSettingsService.TryValidateEditableSettings(settings, out message);
        }

        private static bool TryReadInt(string? text, string label, out int value, out string message)
        {
            if (int.TryParse((text ?? string.Empty).Trim(), out value))
            {
                message = string.Empty;
                return true;
            }

            message = $"{label} must be a whole number.";
            return false;
        }

        private void SetStatus(string message)
        {
            if (txtStatus != null)
                txtStatus.Text = message ?? string.Empty;
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadPendingSettings(out var settings, out var validationMessage))
            {
                SetStatus(validationMessage);
                return;
            }

            try
            {
                AppSettingsService.SaveEditableSettings(settings);
                _isDirty = false;
                const string savedMessage = "App settings saved.";
                SetStatus(savedMessage);
                AppStatusMessageService.Publish(
                    savedMessage,
                    AppStatusMessageKind.Success,
                    TimeSpan.FromSeconds(5),
                    clearOnUserInput: true);
            }
            catch
            {
                SetStatus("App settings save failed.");
                return;
            }

            SaveRequested?.Invoke(this, EventArgs.Empty);
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            _isDirty = false;
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
