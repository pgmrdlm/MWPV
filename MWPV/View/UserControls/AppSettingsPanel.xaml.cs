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
        private bool _validationAttempted;
        private EditableAppSettings? _loadedBaseline;
        private readonly HashSet<string> _pendingIndividualResetKeys = new(StringComparer.Ordinal);
        private bool _pendingResetAll;

        private const string SettingPasswordMinimum = "AS_PW_Minimum";
        private const string SettingIncludeSymbols = "AS_PW_IncludeSymbols";
        private const string SettingClipboardClearSeconds = "SensitiveClipboardClearSeconds";
        private const string SettingLogRetentionDays = "AS_LogRetentionDays";
        private const string SettingBackupRetentionCount = "AS_BackupRetentionCount";
        private const string SettingBackupPromptOnExitAfterChanges = "AS_BackupPromptOnExitAfterChanges";

        private const string LogUpdateForm = "AppSettings";
        private const string LogEventUpdated = "APP_SETTING_UPDATED";
        private const string LogEventReset = "APP_SETTING_RESET";
        private const string LogEventResetAll = "APP_SETTING_RESET_ALL";

        public bool IsDirty => _isDirty;

        public AppSettingsPanel()
        {
            InitializeComponent();
            txtPasswordMinimum.TextChanged += (_, __) => HandleFieldEdited(ValidationCard.PasswordMinimum);
            txtClipboardClearSeconds.TextChanged += (_, __) => HandleFieldEdited(ValidationCard.ClipboardClearSeconds);
            txtLogRetentionDays.TextChanged += (_, __) => HandleFieldEdited(ValidationCard.LogRetentionDays);
            txtBackupRetentionCount.TextChanged += (_, __) => HandleFieldEdited(ValidationCard.BackupRetentionCount);
            chkIncludeSymbols.Checked += (_, __) => MarkDirty();
            chkIncludeSymbols.Unchecked += (_, __) => MarkDirty();
            chkBackupPromptOnExitAfterChanges.Checked += (_, __) => MarkDirty();
            chkBackupPromptOnExitAfterChanges.Unchecked += (_, __) => MarkDirty();
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
                ClearValidationErrors();
                _validationAttempted = false;
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
            chkBackupPromptOnExitAfterChanges.IsChecked = settings.BackupPromptOnExitAfterChanges;
        }

        private void MarkDirty()
        {
            if (_isLoading)
                return;

            _isDirty = true;
            SetStatus(string.Empty);
        }

        private void HandleFieldEdited(ValidationCard card)
        {
            MarkDirty();

            if (!_isLoading && _validationAttempted)
                ValidateSingleCard(card);
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

        private void ResetBackupPromptOnExitAfterChanges()
        {
            chkBackupPromptOnExitAfterChanges.IsChecked = EditableAppSettings.Defaults().BackupPromptOnExitAfterChanges;
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
            ResetBackupPromptOnExitAfterChanges();
            _pendingIndividualResetKeys.Add(SettingBackupRetentionCount);
            _pendingIndividualResetKeys.Add(SettingBackupPromptOnExitAfterChanges);
        }

        private void btnResetAll_Click(object sender, RoutedEventArgs e)
        {
            ResetPasswordMinimum();
            ResetIncludeSymbols();
            ResetClipboard();
            ResetLogRetention();
            ResetBackupRetention();
            ResetBackupPromptOnExitAfterChanges();
            _pendingResetAll = true;
            _pendingIndividualResetKeys.Clear();
            _pendingIndividualResetKeys.Add(SettingPasswordMinimum);
            _pendingIndividualResetKeys.Add(SettingIncludeSymbols);
            _pendingIndividualResetKeys.Add(SettingClipboardClearSeconds);
            _pendingIndividualResetKeys.Add(SettingLogRetentionDays);
            _pendingIndividualResetKeys.Add(SettingBackupRetentionCount);
            _pendingIndividualResetKeys.Add(SettingBackupPromptOnExitAfterChanges);
            ClearValidationErrors();
        }

        private bool ValidateAllPendingSettings(out EditableAppSettings settings)
        {
            settings = new EditableAppSettings();
            ClearValidationErrors();

            bool passwordParsed = TryReadInt(txtPasswordMinimum.Text, "Saved-item password minimum", out int passwordMinimum, out var passwordError);
            bool clipboardParsed = TryReadInt(txtClipboardClearSeconds.Text, "Clipboard auto-clear", out int clipboardSeconds, out var clipboardError);
            bool logParsed = TryReadInt(txtLogRetentionDays.Text, "Log retention", out int logRetentionDays, out var logError);
            bool backupParsed = TryReadInt(txtBackupRetentionCount.Text, "Backup retention", out int backupRetentionCount, out var backupError);

            SetCardError(txtPasswordMinimumError, passwordError);
            SetCardError(txtClipboardClearSecondsError, clipboardError);
            SetCardError(txtLogRetentionDaysError, logError);
            SetCardError(txtBackupRetentionCountError, backupError);

            var rangeValidation = AppSettingsService.ValidateEditableSettings(
                passwordParsed ? passwordMinimum : null,
                clipboardParsed ? clipboardSeconds : null,
                logParsed ? logRetentionDays : null,
                backupParsed ? backupRetentionCount : null);

            if (passwordParsed) SetCardError(txtPasswordMinimumError, rangeValidation.PasswordMinimumError);
            if (clipboardParsed) SetCardError(txtClipboardClearSecondsError, rangeValidation.ClipboardClearSecondsError);
            if (logParsed) SetCardError(txtLogRetentionDaysError, rangeValidation.LogRetentionDaysError);
            if (backupParsed) SetCardError(txtBackupRetentionCountError, rangeValidation.BackupRetentionCountError);

            settings.SavedItemPasswordMinimum = passwordMinimum;
            settings.IncludeSymbols = chkIncludeSymbols.IsChecked == true;
            settings.ClipboardClearSeconds = clipboardSeconds;
            settings.LogRetentionDays = logRetentionDays;
            settings.BackupRetentionCount = backupRetentionCount;
            settings.BackupPromptOnExitAfterChanges = chkBackupPromptOnExitAfterChanges.IsChecked == true;

            return !HasValidationErrors();
        }

        private void ValidateSingleCard(ValidationCard card)
        {
            switch (card)
            {
                case ValidationCard.PasswordMinimum:
                    ValidateSingleValue(txtPasswordMinimum, txtPasswordMinimumError, "Saved-item password minimum", card);
                    break;
                case ValidationCard.ClipboardClearSeconds:
                    ValidateSingleValue(txtClipboardClearSeconds, txtClipboardClearSecondsError, "Clipboard auto-clear", card);
                    break;
                case ValidationCard.LogRetentionDays:
                    ValidateSingleValue(txtLogRetentionDays, txtLogRetentionDaysError, "Log retention", card);
                    break;
                case ValidationCard.BackupRetentionCount:
                    ValidateSingleValue(txtBackupRetentionCount, txtBackupRetentionCountError, "Backup retention", card);
                    break;
            }
        }

        private static void ValidateSingleValue(TextBox input, TextBlock errorControl, string label, ValidationCard card)
        {
            if (!TryReadInt(input.Text, label, out int value, out var parseError))
            {
                SetCardError(errorControl, parseError);
                return;
            }

            var result = AppSettingsService.ValidateEditableSettings(
                card == ValidationCard.PasswordMinimum ? value : null,
                card == ValidationCard.ClipboardClearSeconds ? value : null,
                card == ValidationCard.LogRetentionDays ? value : null,
                card == ValidationCard.BackupRetentionCount ? value : null);

            string rangeError = card switch
            {
                ValidationCard.PasswordMinimum => result.PasswordMinimumError,
                ValidationCard.ClipboardClearSeconds => result.ClipboardClearSecondsError,
                ValidationCard.LogRetentionDays => result.LogRetentionDaysError,
                ValidationCard.BackupRetentionCount => result.BackupRetentionCountError,
                _ => string.Empty
            };

            SetCardError(errorControl, rangeError);
        }

        private void ClearValidationErrors()
        {
            SetCardError(txtPasswordMinimumError, string.Empty);
            SetCardError(txtClipboardClearSecondsError, string.Empty);
            SetCardError(txtLogRetentionDaysError, string.Empty);
            SetCardError(txtBackupRetentionCountError, string.Empty);
        }

        private static void SetCardError(TextBlock control, string? message)
        {
            control.Text = message ?? string.Empty;
            control.Visibility = string.IsNullOrEmpty(control.Text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private bool HasValidationErrors() =>
            !string.IsNullOrEmpty(txtPasswordMinimumError.Text) ||
            !string.IsNullOrEmpty(txtClipboardClearSecondsError.Text) ||
            !string.IsNullOrEmpty(txtLogRetentionDaysError.Text) ||
            !string.IsNullOrEmpty(txtBackupRetentionCountError.Text);

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
            _validationAttempted = true;
            if (!ValidateAllPendingSettings(out var settings))
                return;

            try
            {
                var changes = BuildChanges(_loadedBaseline, settings);
                AppSettingsService.SaveEditableSettings(settings);
                if (changes.Count > 0)
                    VaultSessionStateService.MarkChanged();
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
            ClearValidationErrors();
            _validationAttempted = false;
            _isDirty = false;
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        private static EditableAppSettings CopySettings(EditableAppSettings settings) => new()
        {
            SavedItemPasswordMinimum = settings.SavedItemPasswordMinimum,
            IncludeSymbols = settings.IncludeSymbols,
            ClipboardClearSeconds = settings.ClipboardClearSeconds,
            LogRetentionDays = settings.LogRetentionDays,
            BackupRetentionCount = settings.BackupRetentionCount,
            BackupPromptOnExitAfterChanges = settings.BackupPromptOnExitAfterChanges
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

            if (baseline.BackupPromptOnExitAfterChanges != pending.BackupPromptOnExitAfterChanges)
                changes.Add(new SettingChange(
                    SettingBackupPromptOnExitAfterChanges,
                    pending.BackupPromptOnExitAfterChanges == EditableAppSettings.Defaults().BackupPromptOnExitAfterChanges,
                    pending.BackupPromptOnExitAfterChanges ? "changed to enabled" : "changed to disabled"));

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
            left.BackupRetentionCount == right.BackupRetentionCount &&
            left.BackupPromptOnExitAfterChanges == right.BackupPromptOnExitAfterChanges;

        private void ClearResetTracking()
        {
            _pendingIndividualResetKeys.Clear();
            _pendingResetAll = false;
        }

        private sealed record SettingChange(
            string SettingName,
            bool IsDefaultValue,
            string ChangeDescription);

        private enum ValidationCard
        {
            PasswordMinimum,
            ClipboardClearSeconds,
            LogRetentionDays,
            BackupRetentionCount
        }
    }
}
