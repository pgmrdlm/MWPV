using System;
using System.Collections.Generic;
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
        private EditableAppSettings? _loadedBaseline;
        private readonly HashSet<string> _pendingIndividualResetKeys = new(StringComparer.Ordinal);
        private bool _pendingResetAll;

        private const string SettingPasswordMinimum = "AS_PW_Minimum";
        private const string SettingIncludeSymbols = "AS_PW_IncludeSymbols";
        private const string SettingClipboardClearSeconds = "SensitiveClipboardClearSeconds";
        private const string SettingLogRetentionDays = "AS_LogRetentionDays";
        private const string SettingBackupRetentionCount = "AS_BackupRetentionCount";

        private const string LogUpdateForm = "AppSettings";
        private const string LogEventUpdated = "APP_SETTING_UPDATED";
        private const string LogEventReset = "APP_SETTING_RESET";
        private const string LogEventResetAll = "APP_SETTING_RESET_ALL";

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
                var loaded = AppSettingsService.LoadEditableSettings();
                ApplyToUi(loaded);
                _loadedBaseline = CopySettings(loaded);
                ClearResetTracking();
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
            _pendingIndividualResetKeys.Add(SettingPasswordMinimum);
            _pendingIndividualResetKeys.Add(SettingIncludeSymbols);
        }

        private void btnResetClipboard_Click(object sender, RoutedEventArgs e)
        {
            ResetClipboard();
            _pendingIndividualResetKeys.Add(SettingClipboardClearSeconds);
        }

        private void btnResetLogRetention_Click(object sender, RoutedEventArgs e)
        {
            ResetLogRetention();
            _pendingIndividualResetKeys.Add(SettingLogRetentionDays);
        }

        private void btnResetBackupRetention_Click(object sender, RoutedEventArgs e)
        {
            ResetBackupRetention();
            _pendingIndividualResetKeys.Add(SettingBackupRetentionCount);
        }

        private void btnResetAll_Click(object sender, RoutedEventArgs e)
        {
            ResetPasswordMinimum();
            ResetIncludeSymbols();
            ResetClipboard();
            ResetLogRetention();
            ResetBackupRetention();
            _pendingResetAll = true;
            _pendingIndividualResetKeys.Clear();
            _pendingIndividualResetKeys.Add(SettingPasswordMinimum);
            _pendingIndividualResetKeys.Add(SettingIncludeSymbols);
            _pendingIndividualResetKeys.Add(SettingClipboardClearSeconds);
            _pendingIndividualResetKeys.Add(SettingLogRetentionDays);
            _pendingIndividualResetKeys.Add(SettingBackupRetentionCount);
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
                var changes = BuildChanges(_loadedBaseline, settings);
                AppSettingsService.SaveEditableSettings(settings);
                try
                {
                    WriteChangeLogsBestEffort(changes, settings);
                }
                catch
                {
                    // Logging is best-effort and must not fail a committed settings save.
                }
                _loadedBaseline = CopySettings(settings);
                ClearResetTracking();
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
            ClearResetTracking();
            _isDirty = false;
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        private static EditableAppSettings CopySettings(EditableAppSettings settings) => new()
        {
            SavedItemPasswordMinimum = settings.SavedItemPasswordMinimum,
            IncludeSymbols = settings.IncludeSymbols,
            ClipboardClearSeconds = settings.ClipboardClearSeconds,
            LogRetentionDays = settings.LogRetentionDays,
            BackupRetentionCount = settings.BackupRetentionCount
        };

        private static List<SettingChange> BuildChanges(
            EditableAppSettings? baseline,
            EditableAppSettings pending)
        {
            var changes = new List<SettingChange>();
            if (baseline == null)
                return changes;

            if (baseline.SavedItemPasswordMinimum != pending.SavedItemPasswordMinimum)
                changes.Add(new SettingChange(
                    SettingPasswordMinimum,
                    pending.SavedItemPasswordMinimum == EditableAppSettings.Defaults().SavedItemPasswordMinimum,
                    $"changed to {pending.SavedItemPasswordMinimum}"));

            if (baseline.IncludeSymbols != pending.IncludeSymbols)
                changes.Add(new SettingChange(
                    SettingIncludeSymbols,
                    pending.IncludeSymbols == EditableAppSettings.Defaults().IncludeSymbols,
                    pending.IncludeSymbols ? "changed to enabled" : "changed to disabled"));

            if (baseline.ClipboardClearSeconds != pending.ClipboardClearSeconds)
                changes.Add(new SettingChange(
                    SettingClipboardClearSeconds,
                    pending.ClipboardClearSeconds == EditableAppSettings.Defaults().ClipboardClearSeconds,
                    $"changed to {pending.ClipboardClearSeconds} seconds"));

            if (baseline.LogRetentionDays != pending.LogRetentionDays)
                changes.Add(new SettingChange(
                    SettingLogRetentionDays,
                    pending.LogRetentionDays == EditableAppSettings.Defaults().LogRetentionDays,
                    $"changed to {pending.LogRetentionDays} days"));

            if (baseline.BackupRetentionCount != pending.BackupRetentionCount)
                changes.Add(new SettingChange(
                    SettingBackupRetentionCount,
                    pending.BackupRetentionCount == EditableAppSettings.Defaults().BackupRetentionCount,
                    $"changed to {pending.BackupRetentionCount}"));

            return changes;
        }

        private void WriteChangeLogsBestEffort(
            IReadOnlyList<SettingChange> changes,
            EditableAppSettings saved)
        {
            if (changes.Count == 0)
                return;

            if (_pendingResetAll && SettingsEqual(saved, EditableAppSettings.Defaults()))
            {
                WriteLogBestEffort(LogEventResetAll, 3, "AppSettings", new Dictionary<string, string?>());
                return;
            }

            var updatedDescriptions = new List<string>();
            var resetDescriptions = new List<string>();

            foreach (var change in changes)
            {
                bool isReset = _pendingIndividualResetKeys.Contains(change.SettingName) && change.IsDefaultValue;
                if (isReset)
                    resetDescriptions.Add(change.SettingName);
                else
                    updatedDescriptions.Add($"{change.SettingName} {change.ChangeDescription}");
            }

            if (updatedDescriptions.Count > 0)
                WriteSummaryLogBestEffort(LogEventUpdated, 1, updatedDescriptions);

            if (resetDescriptions.Count > 0)
                WriteSummaryLogBestEffort(LogEventReset, 2, resetDescriptions);
        }

        private static void WriteSummaryLogBestEffort(
            string eventCode,
            int templateSeq,
            IReadOnlyList<string> descriptions)
        {
            var tokens = new Dictionary<string, string?>
            {
                ["ChangeDescription"] = string.Join("; ", descriptions)
            };

            WriteLogBestEffort(eventCode, templateSeq, "AppSettings", tokens);
        }

        private static void WriteLogBestEffort(
            string eventCode,
            int templateSeq,
            string subjectText,
            IReadOnlyDictionary<string, string?> tokens)
        {
            var write = new TemplateLogWriter.WriteRequest
            {
                Level = "INFO",
                Source = "AppSettings",
                EventCode = eventCode,
                SubjectText = subjectText,
                KeySetVersion = 1
            };

            TemplateLogWriter.InsertFromTemplates_BestEffort(
                updateForm: LogUpdateForm,
                seqsInOrder: new[] { templateSeq },
                tokens: tokens,
                write: write);
        }

        private static bool SettingsEqual(EditableAppSettings left, EditableAppSettings right) =>
            left.SavedItemPasswordMinimum == right.SavedItemPasswordMinimum &&
            left.IncludeSymbols == right.IncludeSymbols &&
            left.ClipboardClearSeconds == right.ClipboardClearSeconds &&
            left.LogRetentionDays == right.LogRetentionDays &&
            left.BackupRetentionCount == right.BackupRetentionCount;

        private void ClearResetTracking()
        {
            _pendingIndividualResetKeys.Clear();
            _pendingResetAll = false;
        }

        private sealed record SettingChange(
            string SettingName,
            bool IsDefaultValue,
            string ChangeDescription);
    }
}
