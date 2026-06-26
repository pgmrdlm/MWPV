using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MWPV.Services;
using MWPV.Utilities.Helpers;
using MWPV.Utilities.UI;
using Security.Utility.Storage;

namespace MWPV.View.UserControls.CategoryItems
{
    public partial class CategoryItemSecurityQuestionsPanel : UserControl
    {
        public event EventHandler<SecurityQuestionsCommitEventArgs>? SaveAndExitRequested;
        public event EventHandler? CancelAndExitRequested;

        private readonly ObservableCollection<SecurityQuestionRow> _securityQuestionRows = new();
        private readonly List<SecurityQuestionRow> _baselineRows = new();
        private SecurityQuestionRow? _editingRow;

        private bool _hasChanges;
        private bool _hasErrors;
        private bool _suppressDirty;
        private bool _entryDisabled;
        private bool _newQuestionSessionStarted;
        private bool _isAnswerRevealed;
        private bool _isSelectedProtectedViewActive;
        private bool _hostRequestedCloseWipe;
        private bool _uiEventsHooked;

        private readonly AutoHideTimer _revealAutoHide;

        private const int MaxQuestionChars = 500;
        private const int MaxAnswerChars = 500;
        private const string SedsKey_SecurityQuestionSelectedRowId = "SQ.Selected.QuestionId";
        private const string SedsKey_SecurityQuestionSelectedAnswer = "SQ.Selected.Answer";

        public ObservableCollection<SecurityQuestionRow> SecurityQuestionRows => _securityQuestionRows;
        public bool HasChanges => _hasChanges;
        public bool HasErrors => _hasErrors;

        public CategoryItemSecurityQuestionsPanel()
        {
            InitializeComponent();

            DataContext = this;
            Loaded += CategoryItemSecurityQuestionsPanel_Loaded;
            Unloaded += CategoryItemSecurityQuestionsPanel_Unloaded;

            _revealAutoHide = new AutoHideTimer(
                interval: TimeSpan.FromSeconds(20),
                onTimeout: OnRevealTimeout);

            _securityQuestionRows.CollectionChanged += SecurityQuestionRows_CollectionChanged;
        }

        public void LoadFromHostRows(IEnumerable<CategoryItemSecurityQuestionsService.SecurityQuestionListRow>? rows)
        {
            _suppressDirty = true;
            try
            {
                ClearNewQuestionSessionTracking("LoadFromHostRows");
                HideRevealsAndStopTimer(clearRevealOverlays: true);
                WipeSensitiveEntryFields();
                ClearSecurityQuestionError();
                ResetSecurityQuestionFieldBackgrounds();
                ClearSelectedSecurityQuestionDetailSedsBestEffort();
                _isSelectedProtectedViewActive = false;

                if (SecurityQuestionGrid != null)
                    SecurityQuestionGrid.SelectedItem = null;

                DetachRowHandlers(_securityQuestionRows);
                WipeAndClearSecurityQuestionRows();

                if (rows != null)
                {
                    foreach (var r in rows)
                    {
                        var ui = new SecurityQuestionRow
                        {
                            Id = r.Id,
                            ItemId = r.ItemId,
                            Seq = r.Seq,
                            QuestionPlain = r.QuestionPlain ?? string.Empty,
                            AnswerRaw = string.Empty,
                            AnswerMasked = r.AnswerMasked ?? string.Empty,
                            IsActive = r.IsActive,
                            CreatedAtUtcSeconds = r.CreatedAtUtcSeconds,
                            UpdatedAtUtcSeconds = r.UpdatedAtUtcSeconds
                        };

                        AttachRowHandlers(ui);
                        _securityQuestionRows.Add(ui);
                    }
                }

                CaptureBaselineFromCurrent();
                SetDirty(false);
                SetErrors(_entryDisabled);
                SetEditingMode(null);
                UpdateTabButtons();
            }
            finally
            {
                _suppressDirty = false;
            }
        }

        public void WipeAllForHostClose()
        {
            _hostRequestedCloseWipe = true;
            ClearNewQuestionSessionTracking("WipeAllForHostClose");
            HideRevealsAndStopTimer(clearRevealOverlays: true);
            WipeSensitiveEntryFields();
            WipeAndClearSecurityQuestionRows();
            ClearSelectedSecurityQuestionDetailSedsBestEffort();
            _isSelectedProtectedViewActive = false;
            ClearSecurityQuestionError();
            ResetSecurityQuestionFieldBackgrounds();
            _editingRow = null;
            SetDirty(false);
            SetErrors(false);
            UpdateTabButtons();
        }

        public bool TryAutoCommitAndWipe()
        {
            if (EntryLineHasAnyInput())
            {
                ShowSecurityQuestionError("Finish Add/Update or Clear Row before leaving this tab.");
                SetErrors(true);
                UpdateTabButtons();
                return false;
            }

            if (!ValidateTabStateOnly(showErrors: true))
            {
                SetErrors(true);
                UpdateTabButtons();
                return false;
            }

            ClearSecurityQuestionError();
            ClearNewQuestionSessionTracking("TryAutoCommitAndWipe");
            ClearEntryFields();
            ClearSelectedSecurityQuestionDetailSedsBestEffort();
            _isSelectedProtectedViewActive = false;
            SetErrors(false);
            UpdateTabButtons();
            return true;
        }

        public bool HasHostCloseSessionWork()
        {
            return _newQuestionSessionStarted ||
                   _hasChanges ||
                   EntryLineHasAnyInput() ||
                   _editingRow != null;
        }

        public bool TryBuildHostCloseSavePayload(out IReadOnlyList<SecurityQuestionRow> rows)
        {
            rows = Array.Empty<SecurityQuestionRow>();
            ClearSecurityQuestionError();

            if (_entryDisabled)
            {
                ShowSecurityQuestionError("Security Question entry is disabled for this session.");
                SetErrors(true);
                UpdateTabButtons();
                return false;
            }

            if (EntryLineHasAnyInput())
            {
                ShowSecurityQuestionError("Finish Add/Update or Clear Row before saving.");
                SetErrors(true);
                UpdateTabButtons();
                return false;
            }

            if (HasPendingBlankAddAttempt())
            {
                ShowSecurityQuestionError("Finish Add/Update or Clear Row before saving.");
                SetErrors(true);
                UpdateTabButtons();
                return false;
            }

            if (!ValidateTabStateOnly(showErrors: true))
            {
                SetErrors(true);
                UpdateTabButtons();
                return false;
            }

            ClearSecurityQuestionError();
            SetErrors(false);
            UpdateTabButtons();
            rows = _securityQuestionRows.Select(CloneRow).ToList();
            return true;
        }

        public bool TryPrepareHostCloseDiscard()
        {
            try
            {
                ClearNewQuestionSessionTracking("TryPrepareHostCloseDiscard");
                ClearSecurityQuestionError();
                SetErrors(false);
                UpdateTabButtons();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void CategoryItemSecurityQuestionsPanel_Loaded(object? sender, RoutedEventArgs e)
        {
            HookUiEventsOnce();

            if (QuestionTextBox != null)
                QuestionTextBox.MaxLength = MaxQuestionChars;
            if (AnswerBox != null)
                AnswerBox.MaxLength = MaxAnswerChars;

            CaptureBaselineFromCurrent();
            SetDirty(false);
            SetErrors(_entryDisabled);
            UpdateTabButtons();
        }

        private void CategoryItemSecurityQuestionsPanel_Unloaded(object? sender, RoutedEventArgs e)
        {
            _revealAutoHide.Stop();
            UnhookUiEvents();
            HideRevealsAndStopTimer(clearRevealOverlays: true);
            WipeSensitiveEntryFields();
            ClearSelectedSecurityQuestionDetailSedsBestEffort();
            _isSelectedProtectedViewActive = false;

            if (_hostRequestedCloseWipe)
                WipeAndClearSecurityQuestionRows();
        }

        private void SecurityQuestionRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (SecurityQuestionRow r in e.OldItems)
                    DetachRowHandlers(r);

            if (e.NewItems != null)
                foreach (SecurityQuestionRow r in e.NewItems)
                    AttachRowHandlers(r);

            MarkDirty();
        }

        private void AttachRowHandlers(SecurityQuestionRow row)
        {
            row.PropertyChanged -= Row_PropertyChanged;
            row.PropertyChanged += Row_PropertyChanged;
        }

        private void DetachRowHandlers(SecurityQuestionRow row)
        {
            row.PropertyChanged -= Row_PropertyChanged;
        }

        private void DetachRowHandlers(IEnumerable<SecurityQuestionRow> rows)
        {
            foreach (var r in rows)
                DetachRowHandlers(r);
        }

        private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            MarkDirty();
        }

        private void MarkDirty()
        {
            if (_suppressDirty)
                return;

            _hasChanges = true;
            UpdateTabButtons();
        }

        private void SetDirty(bool value) => _hasChanges = value;
        private void SetErrors(bool value) => _hasErrors = value;

        private void UpdateTabButtons()
        {
            ApplyProtectedViewControlState();

            if (BtnTabSave != null)
            {
                BtnTabSave.IsEnabled =
                    !_entryDisabled &&
                    _hasChanges &&
                    !_hasErrors &&
                    !EntryLineHasAnyInput();
            }

            if (BtnTabCancel != null)
                BtnTabCancel.IsEnabled = true;

            if (BtnCopyAnswer != null)
                BtnCopyAnswer.Visibility = _isSelectedProtectedViewActive ? Visibility.Visible : Visibility.Collapsed;

            if (BtnSecurityQuestionEditSelected != null)
            {
                bool hasSelectedRow = SecurityQuestionGrid?.SelectedItem is SecurityQuestionRow;
                BtnSecurityQuestionEditSelected.IsEnabled =
                    !_entryDisabled &&
                    _isSelectedProtectedViewActive &&
                    _editingRow == null &&
                    hasSelectedRow;
            }
        }

        private void ApplyProtectedViewControlState()
        {
            bool editable = !_entryDisabled && (!_isSelectedProtectedViewActive || _editingRow != null);

            if (QuestionTextBox != null) QuestionTextBox.IsEnabled = editable;
            if (AnswerBox != null) AnswerBox.IsEnabled = editable;
            if (ChkSecurityQuestionActive != null) ChkSecurityQuestionActive.IsEnabled = editable;
            if (BtnSecurityQuestionAddOrUpdate != null) BtnSecurityQuestionAddOrUpdate.IsEnabled = editable;
            if (BtnSecurityQuestionClearRow != null) BtnSecurityQuestionClearRow.IsEnabled = editable;
            if (BtnViewAnswer != null) BtnViewAnswer.IsEnabled = !_entryDisabled;
        }

        private void CaptureBaselineFromCurrent()
        {
            _baselineRows.Clear();
            foreach (var r in _securityQuestionRows)
                _baselineRows.Add(CloneRow(r));
        }

        private static SecurityQuestionRow CloneRow(SecurityQuestionRow r)
        {
            return new SecurityQuestionRow
            {
                Id = r.Id,
                ItemId = r.ItemId,
                Seq = r.Seq,
                QuestionPlain = r.QuestionPlain ?? string.Empty,
                AnswerRaw = r.AnswerRaw ?? string.Empty,
                AnswerMasked = r.AnswerMasked ?? string.Empty,
                IsActive = r.IsActive,
                CreatedAtUtcSeconds = r.CreatedAtUtcSeconds,
                UpdatedAtUtcSeconds = r.UpdatedAtUtcSeconds
            };
        }

        private void OnRevealTimeout()
        {
            HideRevealsAndStopTimer(clearRevealOverlays: true);
            UpdateTabButtons();
        }

        private void TouchRevealTimerIfNeeded()
        {
            _revealAutoHide.Touch(_isAnswerRevealed);
        }

        private void HideRevealsAndStopTimer(bool clearRevealOverlays)
        {
            _revealAutoHide.Stop();
            HideAnswer(clearOverlay: clearRevealOverlays);
        }

        private static void ClearRevealOverlayTextOnly(TextBox? tb)
        {
            if (tb == null) return;
            tb.Text = string.Empty;
        }

        private void ShowAnswer()
        {
            if (AnswerBox == null || AnswerPlainTextBox == null || BtnViewAnswer == null)
                return;

            _isAnswerRevealed = true;
            string trimmed = TrimToMaxChars(AnswerBox.Password, MaxAnswerChars);
            if (!string.Equals(trimmed, AnswerBox.Password, StringComparison.Ordinal))
                AnswerBox.Password = trimmed;

            MaskedRevealOverlayHelper.ShowPlainOverlay(AnswerBox, AnswerPlainTextBox, trimmed);
            BtnViewAnswer.ToolTip = "Hide answer";
            TouchRevealTimerIfNeeded();
        }

        private void HideAnswer(bool clearOverlay)
        {
            if (AnswerBox == null || AnswerPlainTextBox == null || BtnViewAnswer == null)
                return;

            if (!_isAnswerRevealed && AnswerBox.Visibility == Visibility.Visible)
                return;

            _isAnswerRevealed = false;
            AnswerBox.Visibility = Visibility.Visible;

            if (clearOverlay)
                ClearRevealOverlayTextOnly(AnswerPlainTextBox);

            MaskedRevealOverlayHelper.RestoreMaskedOverlay(AnswerBox, AnswerPlainTextBox);
            BtnViewAnswer.ToolTip = "Show answer";
            TouchRevealTimerIfNeeded();
        }

        private void BtnViewAnswer_Click(object sender, RoutedEventArgs e)
        {
            ClearSecurityQuestionError();
            ResetSecurityQuestionFieldBackgrounds();

            if (_isAnswerRevealed) HideAnswer(clearOverlay: true);
            else ShowAnswer();
        }

        private void BtnCopyAnswer_Click(object sender, RoutedEventArgs e)
        {
            string value = ReadSelectedProtectedFieldFromSeds(SedsKey_SecurityQuestionSelectedAnswer);
            if (string.IsNullOrEmpty(value) && AnswerBox != null)
                value = AnswerBox.Password ?? string.Empty;

            if (string.IsNullOrEmpty(value))
            {
                ShowSecurityQuestionError("No answer is available to copy.");
                return;
            }

            if (!ClipboardHelper.TryCopySensitiveText(value, out _, tag: "SecurityQuestionAnswer"))
                ShowSecurityQuestionError("Unable to copy answer to clipboard.");
            else
                ClearSecurityQuestionError();
        }

        private void QuestionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressDirty) return;
            MarkDirty();
            UpdateTabButtons();
        }

        private void AnswerBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressDirty) return;
            HideAnswer(clearOverlay: true);
            MarkDirty();
            UpdateTabButtons();
        }

        private void SecurityQuestionActive_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressDirty) return;
            MarkDirty();
            UpdateTabButtons();
        }

        private void OnSecurityQuestionAddOrUpdateClick(object sender, RoutedEventArgs e)
        {
            _isSelectedProtectedViewActive = false;
            ClearSecurityQuestionError();

            bool isTrueAddMode = _editingRow == null;
            bool isUpdate = _editingRow != null;
            bool isExistingPersistedUpdate = _editingRow != null && _editingRow.Id > 0;

            if (isTrueAddMode)
                BeginAddNewQuestionSession();

            if (_entryDisabled)
            {
                ShowSecurityQuestionError("Security Question entry is disabled for this session.");
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            if (!ValidateSecurityQuestionFields(showErrors: true))
            {
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            string question = GetCurrentQuestion();
            string answer = GetCurrentAnswer();
            bool isActive = ChkSecurityQuestionActive?.IsChecked == true;
            string masked = MaskAnswer(answer);

            if (isTrueAddMode)
            {
                var row = new SecurityQuestionRow
                {
                    Id = 0,
                    ItemId = TryGetActiveCategoryItemIdFromSeds() ?? 0,
                    Seq = GetNextSeqForDraft(),
                    QuestionPlain = question,
                    AnswerRaw = answer,
                    AnswerMasked = masked,
                    IsActive = isActive
                };

                _securityQuestionRows.Add(row);
            }
            else if (_editingRow != null)
            {
                _editingRow.QuestionPlain = question;
                _editingRow.AnswerRaw = answer;
                _editingRow.AnswerMasked = masked;
                _editingRow.IsActive = isActive;
            }

            ClearNewQuestionSessionTracking("OnSecurityQuestionAddOrUpdateClick");
            if (isUpdate)
                TeardownAfterSuccessfulUpdate();
            else
                ClearEntryFields();

            SetErrors(false);
            MarkDirty();
            UpdateTabButtons();

            if (isExistingPersistedUpdate || isTrueAddMode)
                RaiseSaveAndExitRequestOnly();
        }

        private void OnSecurityQuestionClearRowClick(object sender, RoutedEventArgs e)
        {
            ClearNewQuestionSessionTracking("OnSecurityQuestionClearRowClick");
            _isSelectedProtectedViewActive = false;
            ClearSecurityQuestionError();
            ClearEntryFields();
            SetErrors(false);
            UpdateTabButtons();
        }

        private void OnSecurityQuestionFieldLostFocus(object sender, RoutedEventArgs e)
        {
            if (_entryDisabled)
            {
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            if (!EntryLineHasAnyInput())
            {
                ClearSecurityQuestionError();
                SetErrors(false);
                UpdateTabButtons();
                return;
            }

            SetErrors(!ValidateSecurityQuestionFields(showErrors: false));
            UpdateTabButtons();
        }

        private void SecurityQuestionGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressDirty)
                return;

            if (SecurityQuestionGrid?.SelectedItem is not SecurityQuestionRow selected)
            {
                ClearSelectedSecurityQuestionDetailSedsBestEffort();
                _isSelectedProtectedViewActive = false;
                UpdateTabButtons();
                return;
            }

            if (selected.Id <= 0)
            {
                StoreSelectedSecurityQuestionDetailSedsBestEffort(selected.Id, selected.AnswerRaw ?? string.Empty);
                PopulateProtectedViewFromSelectedDetail(selected, selected.AnswerRaw ?? string.Empty, editMode: false);
                return;
            }

            int? activeItemId = TryGetActiveCategoryItemIdFromSeds();
            if (!activeItemId.HasValue || activeItemId.Value <= 0)
            {
                ClearSelectedSecurityQuestionDetailSedsBestEffort();
                _isSelectedProtectedViewActive = false;
                UpdateTabButtons();
#if DEBUG
                Debug.WriteLine("[SECURITY-QUESTIONS-PANEL][SELECT] Missing active ItemId context; targeted detail load skipped.");
#endif
                return;
            }

            var detail = CategoryItemSecurityQuestionsService.LoadSecurityQuestionDetailByItemIdAndId(
                activeItemId.Value,
                selected.Id);

            if (detail == null)
            {
                ClearSelectedSecurityQuestionDetailSedsBestEffort();
                _isSelectedProtectedViewActive = false;
                UpdateTabButtons();
                return;
            }

            _suppressDirty = true;
            try
            {
                selected.QuestionPlain = detail.QuestionPlain;
                selected.AnswerMasked = MaskAnswer(detail.AnswerPlain);
                selected.IsActive = detail.IsActive;
            }
            finally
            {
                _suppressDirty = false;
            }

            StoreSelectedSecurityQuestionDetailSedsBestEffort(selected.Id, detail.AnswerPlain);
            PopulateProtectedViewFromSelectedDetail(selected, detail.AnswerPlain, editMode: false);
        }

        private void PopulateProtectedViewFromSelectedDetail(SecurityQuestionRow row, string answer, bool editMode)
        {
            _suppressDirty = true;
            try
            {
                _isSelectedProtectedViewActive = true;
                SetEditingMode(editMode ? row : null);

                if (QuestionTextBox != null)
                    QuestionTextBox.Text = TrimToMaxChars(row.QuestionPlain, MaxQuestionChars);

                if (AnswerBox != null)
                    AnswerBox.Password = TrimToMaxChars(answer, MaxAnswerChars);

                HideAnswer(clearOverlay: true);

                if (ChkSecurityQuestionActive != null)
                    ChkSecurityQuestionActive.IsChecked = row.IsActive;

                ClearSecurityQuestionError();
                ResetSecurityQuestionFieldBackgrounds();
                SetErrors(false);
            }
            finally
            {
                _suppressDirty = false;
            }

            UpdateTabButtons();
        }

        private void OnSecurityQuestionEditClick(object sender, RoutedEventArgs e)
        {
            if (SecurityQuestionGrid?.SelectedItem is not SecurityQuestionRow row)
                return;

            string answerForEdit = row.AnswerRaw ?? string.Empty;
            if (row.Id > 0 && !TryReadSelectedProtectedAnswerForEdit(row.Id, out answerForEdit))
            {
                ShowSecurityQuestionError("Unable to open edit mode. Reselect the row and try again.");
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            PopulateProtectedViewFromSelectedDetail(row, answerForEdit, editMode: true);
        }

        private void OnTabSaveClick(object sender, RoutedEventArgs e)
        {
            ClearSecurityQuestionError();

            if (_entryDisabled)
            {
                ShowSecurityQuestionError("Security Question entry is disabled for this session.");
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            if (EntryLineHasAnyInput())
            {
                ShowSecurityQuestionError("Finish Add/Update or Clear Row before saving.");
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            if (!ValidateTabStateOnly(showErrors: true))
            {
                SetErrors(true);
                UpdateTabButtons();
                return;
            }

            RaiseSaveAndExitRequestOnly();
        }

        private void RaiseSaveAndExitRequestOnly()
        {
            var payload = _securityQuestionRows.Select(CloneRow).ToList();
            SaveAndExitRequested?.Invoke(this, new SecurityQuestionsCommitEventArgs(payload));
        }

        private void OnTabCancelClick(object sender, RoutedEventArgs e)
        {
            ClearNewQuestionSessionTracking("OnTabCancelClick");
            ClearSecurityQuestionError();
            HideRevealsAndStopTimer(clearRevealOverlays: true);
            WipeSensitiveEntryFields();
            ResetSecurityQuestionFieldBackgrounds();
            ClearSelectedSecurityQuestionDetailSedsBestEffort();
            _isSelectedProtectedViewActive = false;
            CancelAndExitRequested?.Invoke(this, EventArgs.Empty);
        }

        private bool ValidateTabStateOnly(bool showErrors)
        {
            if (EntryLineHasAnyInput())
            {
                if (showErrors) ShowSecurityQuestionError("Finish Add/Update or Clear Row before saving.");
                return false;
            }

            foreach (var row in _securityQuestionRows)
            {
                if (string.IsNullOrWhiteSpace(row.QuestionPlain))
                {
                    if (showErrors) ShowSecurityQuestionError("Question is required.");
                    return false;
                }

                if (row.Id == 0 && string.IsNullOrEmpty(row.AnswerRaw))
                {
                    if (showErrors) ShowSecurityQuestionError("Answer is required.");
                    return false;
                }
            }

            return true;
        }

        private bool ValidateSecurityQuestionFields(bool showErrors)
        {
            if (showErrors)
                ResetSecurityQuestionFieldBackgrounds();

            if (string.IsNullOrWhiteSpace(GetCurrentQuestion()))
            {
                if (showErrors) ShowSecurityQuestionError("Question is required.", QuestionTextBox);
                return false;
            }

            if (string.IsNullOrEmpty(GetCurrentAnswer()))
            {
                if (showErrors) ShowSecurityQuestionError("Answer is required.", AnswerBox);
                return false;
            }

            if (showErrors)
                ClearSecurityQuestionError();

            return true;
        }

        private void ShowSecurityQuestionError(string message, Control? field = null)
        {
            if (SecurityQuestionErrorTextBlock != null)
                SecurityQuestionErrorTextBlock.Text = message ?? string.Empty;

            if (field != null)
                field.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x99, 0x99));
        }

        private void ClearSecurityQuestionError()
        {
            if (SecurityQuestionErrorTextBlock != null)
                SecurityQuestionErrorTextBlock.Text = string.Empty;

            ResetSecurityQuestionFieldBackgrounds();
        }

        private void ResetSecurityQuestionFieldBackgrounds()
        {
            QuestionTextBox?.ClearValue(BackgroundProperty);
            AnswerBox?.ClearValue(BackgroundProperty);
            AnswerPlainTextBox?.ClearValue(BackgroundProperty);
        }

        private void WipeSensitiveEntryFields()
        {
            HideRevealsAndStopTimer(clearRevealOverlays: true);
            if (QuestionTextBox != null) UICleaner.Clear(QuestionTextBox);
            if (AnswerBox != null) UICleaner.Clear(AnswerBox);
            ClearRevealOverlayTextOnly(AnswerPlainTextBox);
        }

        private void ClearEntryFields()
        {
            _suppressDirty = true;
            try
            {
                if (QuestionTextBox != null) UICleaner.Clear(QuestionTextBox);
                if (AnswerBox != null) UICleaner.Clear(AnswerBox);
                HideAnswer(clearOverlay: true);

                if (ChkSecurityQuestionActive != null)
                    ChkSecurityQuestionActive.IsChecked = true;

                _isSelectedProtectedViewActive = false;
                SetEditingMode(null);
                ClearSecurityQuestionError();
                ResetSecurityQuestionFieldBackgrounds();
            }
            finally
            {
                _suppressDirty = false;
            }
        }

        private void TeardownAfterSuccessfulUpdate()
        {
            ClearEntryFields();
            ClearSelectedSecurityQuestionDetailSedsBestEffort();
            _isSelectedProtectedViewActive = false;
            if (SecurityQuestionGrid != null)
                SecurityQuestionGrid.SelectedItem = null;
            SetEditingMode(null);
        }

        private void WipeAndClearSecurityQuestionRows()
        {
            foreach (var row in _securityQuestionRows.ToList())
                row.Wipe();

            _securityQuestionRows.Clear();
        }

        private void SetEditingMode(SecurityQuestionRow? row)
        {
            _editingRow = row;

            if (BtnSecurityQuestionAddOrUpdate != null)
                BtnSecurityQuestionAddOrUpdate.Content = (_editingRow == null) ? "Add" : "Update";
        }

        private bool EntryLineHasAnyInput()
        {
            if (_isSelectedProtectedViewActive && _editingRow == null)
                return false;

            return (QuestionTextBox != null && !string.IsNullOrWhiteSpace(QuestionTextBox.Text)) ||
                   (AnswerBox != null && !string.IsNullOrEmpty(AnswerBox.Password)) ||
                   _editingRow != null;
        }

        private string GetCurrentQuestion()
        {
            return TrimToMaxChars(QuestionTextBox?.Text ?? string.Empty, MaxQuestionChars).Trim();
        }

        private string GetCurrentAnswer()
        {
            return TrimToMaxChars(AnswerBox?.Password ?? string.Empty, MaxAnswerChars).Trim();
        }

        public void BeginAddNewQuestionSession()
        {
            if (_entryDisabled || _isSelectedProtectedViewActive || _editingRow != null)
                return;

            _newQuestionSessionStarted = true;
        }

        private bool HasPendingBlankAddAttempt()
        {
            return _newQuestionSessionStarted &&
                   !_hasChanges &&
                   !EntryLineHasAnyInput() &&
                   _editingRow == null;
        }

        private void ClearNewQuestionSessionTracking(string source)
        {
            _newQuestionSessionStarted = false;
        }

        private int GetNextSeqForDraft()
        {
            int maxSeq = 0;
            foreach (var row in _securityQuestionRows)
            {
                if (row.Seq > maxSeq)
                    maxSeq = row.Seq;
            }

            return maxSeq + 1;
        }

        private static string TrimToMaxChars(string? value, int maxChars)
        {
            var s = value ?? string.Empty;
            return s.Length <= maxChars ? s : s.Substring(0, maxChars);
        }

        private static string MaskAnswer(string? answerPlain)
        {
            return string.IsNullOrEmpty(answerPlain) ? string.Empty : "***";
        }

        private static int? TryGetActiveCategoryItemIdFromSeds()
        {
            return CategoryItemSedsContextHelper.TryGetCurrentCategoryItemId();
        }

        private static void ClearSelectedSecurityQuestionDetailSedsBestEffort()
        {
            try { SecureEncryptedDataStore.Clear(SedsKey_SecurityQuestionSelectedAnswer); } catch { }
            try { SecureEncryptedDataStore.Clear(SedsKey_SecurityQuestionSelectedRowId); } catch { }
        }

        private static void StoreSelectedSecurityQuestionDetailSedsBestEffort(long rowId, string answer)
        {
            ClearSelectedSecurityQuestionDetailSedsBestEffort();
            try { SecureEncryptedDataStore.SetString(SedsKey_SecurityQuestionSelectedRowId, rowId.ToString(CultureInfo.InvariantCulture)); } catch { }
            try { SecureEncryptedDataStore.SetString(SedsKey_SecurityQuestionSelectedAnswer, answer ?? string.Empty); } catch { }
        }

        private static string ReadSelectedProtectedFieldFromSeds(string sedsKey)
        {
            try
            {
                if (!SecureEncryptedDataStore.TryGetBytes(sedsKey, out var valueBytes) || valueBytes.Length == 0)
                    return string.Empty;

                try
                {
                    return Encoding.UTF8.GetString(valueBytes);
                }
                finally
                {
                    Array.Clear(valueBytes, 0, valueBytes.Length);
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TryReadSelectedProtectedAnswerForEdit(long expectedRowId, out string answer)
        {
            answer = string.Empty;

            string selectedRowIdRaw = ReadSelectedProtectedFieldFromSeds(SedsKey_SecurityQuestionSelectedRowId);
            if (!long.TryParse(selectedRowIdRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long selectedRowId))
                return false;

            if (selectedRowId != expectedRowId)
                return false;

            answer = ReadSelectedProtectedFieldFromSeds(SedsKey_SecurityQuestionSelectedAnswer);
            return !string.IsNullOrEmpty(answer);
        }

        private void HookUiEventsOnce()
        {
            if (_uiEventsHooked)
                return;

            if (AnswerBox != null)
            {
                AnswerBox.PasswordChanged -= AnswerBox_PasswordChanged;
                AnswerBox.PasswordChanged += AnswerBox_PasswordChanged;
            }

            _uiEventsHooked = true;
        }

        private void UnhookUiEvents()
        {
            if (!_uiEventsHooked)
                return;

            if (AnswerBox != null)
                AnswerBox.PasswordChanged -= AnswerBox_PasswordChanged;

            _uiEventsHooked = false;
        }

        public sealed class SecurityQuestionsCommitEventArgs : EventArgs
        {
            public SecurityQuestionsCommitEventArgs(IReadOnlyList<SecurityQuestionRow> rows) => Rows = rows;
            public IReadOnlyList<SecurityQuestionRow> Rows { get; }
        }

        public sealed class SecurityQuestionRow : INotifyPropertyChanged
        {
            public long Id { get; set; }
            public long ItemId { get; set; }
            public long CreatedAtUtcSeconds { get; set; }
            public long UpdatedAtUtcSeconds { get; set; }

            private int _seq;
            public int Seq
            {
                get => _seq;
                set { if (_seq != value) { _seq = value; OnPropertyChanged(nameof(Seq)); } }
            }

            private string _questionPlain = string.Empty;
            public string QuestionPlain
            {
                get => _questionPlain;
                set { if (_questionPlain != value) { _questionPlain = value ?? string.Empty; OnPropertyChanged(nameof(QuestionPlain)); } }
            }

            private string _answerRaw = string.Empty;
            public string AnswerRaw
            {
                get => _answerRaw;
                set { if (_answerRaw != value) { _answerRaw = value ?? string.Empty; OnPropertyChanged(nameof(AnswerRaw)); } }
            }

            private string _answerMasked = string.Empty;
            public string AnswerMasked
            {
                get => _answerMasked;
                set { if (_answerMasked != value) { _answerMasked = value ?? string.Empty; OnPropertyChanged(nameof(AnswerMasked)); } }
            }

            private bool _isActive = true;
            public bool IsActive
            {
                get => _isActive;
                set { if (_isActive != value) { _isActive = value; OnPropertyChanged(nameof(IsActive)); } }
            }

            public void Wipe()
            {
                Seq = 0;
                QuestionPlain = string.Empty;
                AnswerRaw = string.Empty;
                AnswerMasked = string.Empty;
                IsActive = false;
                Id = 0;
                ItemId = 0;
                CreatedAtUtcSeconds = 0;
                UpdatedAtUtcSeconds = 0;
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string propertyName) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
