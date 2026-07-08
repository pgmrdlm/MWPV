// File: View/UserControls/CategoryItemEditorTabs.xaml.cs
//
// FULL REWRITE (Edit-tabs coordinator only)
// ----------------------------------------
// Corrections made here:
// 1) BasicPanel "Save" must NOT close the editor anymore.
//    - It persists, refreshes the grid, then forces Basic tab + switches BasicPanel to VIEW-only (best-effort).
// 2) BankCards "Save & Exit" still closes (existing behavior).
// 3) Cancel still closes (existing behavior).
// 4) Leave-Basic tab switching rules stay as previously implemented.
//
// Change in this rewrite:
// - Fix compile errors caused by invalid "in[]" syntax (must be "in new[]").
// - Keep host-callable ForceCancelFromHost() delegating to BasicPanel.ForceCancelFromHost().
// - IMPORTANT (THIS BUG): Wire AppStatus.IsBasicOpen based on the selected tab index,
//   so the inactivity timer can detect when Basic is open.
//
// NOTE: No other behavior changes.

using MWPV.Services;
using MWPV.Session;                        // AppStatus (IsBasicOpen)
using MWPV.View.UserControls.CategoryItems;
using MWPV.View.UserControls.Popup;
using Security.Utility.Storage;            // SecureEncryptedDataStore (SEDS)
using Security.Utility.Crypto.Signatures;  // SensitiveValueSignature (HMAC signature)
using Security.Utility.Crypto.Fields;      // FieldAesCrypto (SEDS key constant)
using Security.Utility.Wiping;             // SensitiveDataCleaner (central wiping)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;        // CryptographicOperations.FixedTimeEquals
using System.Reflection;                  // BindingFlags
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;               // VisualTreeHelper
using System.Windows.Threading;

namespace MWPV.View.UserControls
{
    public partial class CategoryItemEditorTabs : UserControl
    {
        public event EventHandler? Submitted;
        public event EventHandler? Canceled;
        public event EventHandler<string>? PasswordValidationFailed;

        private int _categoryKey;
        private string _categoryName = string.Empty;

        // Existence only (PK present)
        private bool _hasPersistedId;

        private bool _panelsHooked;
        private bool _handlingTabSelection;
        private int _lastTabIndex;
        private bool _isClosing;

        // Tab-switch helper:
        // When leaving Basic, we temporarily force selection back to Basic while we validate/persist.
        // Then we programmatically switch to the requested tab. That second selection change should NOT
        // re-run the leave-basic persistence logic (it already ran).
        private bool _suppressLeaveBasicOnce;
        private bool _suppressLeaveBankCardsOnce;
        private bool _suppressLeaveSecurityQuestionsOnce;
        private bool _bankCardsLoaded;
        private int _bankCardsLoadedItemId;
        private bool _accountsLoaded;
        private int _accountsLoadedItemId;
        private bool _securityQuestionsLoaded;
        private int _securityQuestionsLoadedItemId;

        public IReadOnlyList<CategoryItemBankCardsPanel.BankCardRow> BankCardsDraftRows { get; private set; }
            = Array.Empty<CategoryItemBankCardsPanel.BankCardRow>();

        public IReadOnlyList<CategoryItemAccountsPanel.AccountRow> AccountsDraftRows { get; private set; }
            = Array.Empty<CategoryItemAccountsPanel.AccountRow>();

        public IReadOnlyList<CategoryItemSecurityQuestionsPanel.SecurityQuestionRow> SecurityQuestionsDraftRows { get; private set; }
            = Array.Empty<CategoryItemSecurityQuestionsPanel.SecurityQuestionRow>();

        private const int TabIndexBasic = 0;
        private const int TabIndexAccounts = 1;
        private const int TabIndexBankCards = 2;
        private const int TabIndexSecurityQuestions = 3;
        private const string DuplicateAccountNumberMessage = "Duplicate account number is not allowed for this item.";

        private const string EntityKind_CategoryItem = "CategoryItem";
        private static readonly string SedsKey_EntityKind = SecureEncryptedDataStore.ContextKeys.CurrentEntityKind;
        private static readonly string SedsKey_EntityId = SecureEncryptedDataStore.ContextKeys.CurrentEntityId;

        // ============================================================
        // AFTER SIGNATURES (Email/Phone/PIN only - existing behavior)
        // ============================================================

        // Purposes: domain separation, stable forever
        private const string Purpose_CI_Email_AfterSig = "CI.Email.AfterSig.V1";
        private const string Purpose_CI_Phone_AfterSig = "CI.Phone.AfterSig.V1";
        private const string Purpose_CI_Pin_AfterSig = "CI.Pin.AfterSig.V1";

        // SEDS keys for AFTER signatures (Base64 strings)
        private const string SedsKey_AfterSig_Email = "CI.AfterSig.Email";
        private const string SedsKey_AfterSig_Phone = "CI.AfterSig.Phone";
        private const string SedsKey_AfterSig_Pin = "CI.AfterSig.Pin";

        // ============================================================
        // PASSWORD FINGERPRINT COMPARE (THIS SESSION)
        // ============================================================
        private const string Purpose_PwFingerprint = SensitiveValueSignature.DefaultPurpose; // MUST match DB stored sig algorithm

        // ============================================================
        // LOGGING
        // ============================================================
        private const string LogSource_CategoryItem = "CategoryItem";

        private const string LogEvent_CategoryItemCreated = "CATEGORYITEM_CREATED";
        private const string LogEvent_CategoryItemChanged = "CATEGORYITEM_CHANGED";
        private const string LogEvent_AccountsCreated = "ACCOUNTS_CREATED";
        private const string LogEvent_AccountsChanged = "ACCOUNTS_CHANGED";
        private const string LogEvent_BankCardCreated = "BANKCARD_CREATED";
        private const string LogEvent_BankCardChanged = "BANKCARD_CHANGED";
        private const string LogEvent_BankCardDeactivated = "BANKCARD_DEACTIVATED";
        private const string LogEvent_SecurityQuestionCreated = "SECURITYQUESTION_CREATED";
        private const string LogEvent_SecurityQuestionChanged = "SECURITYQUESTION_CHANGED";
        private const string LogEvent_SecurityQuestionDeactivated = "SECURITYQUESTION_DEACTIVATED";

        private const string Template_NewItemCreated =
            "Category Item #CategoryItemName# has been created for Category #CategoryName#";

        private const string TemplateForm_BasicTab = "BasicTab";
        private const string TemplateForm_AccountsTab = "AccountsTab";
        private const string TemplateForm_BankCardsTab = "BankCardsTab";
        private const string TemplateForm_SecurityQuestionsTab = "SecurityQuestionsTab";

        public CategoryItemEditorTabs()
        {
            InitializeComponent();

            Loaded += CategoryItemEditorTabs_Loaded;
            Unloaded += CategoryItemEditorTabs_Unloaded;
        }

        /* ======================= AppStatus bridge (inactivity timer) ======================= */

        private void UpdateIsBasicOpenFromUi_BestEffort()
        {
            try
            {
                int idx = ItemTabs?.SelectedIndex ?? -1;
                bool isBasic = idx == TabIndexBasic;
                AppStatus.IsBasicOpen = isBasic;

            }
            catch
            {
                // swallow: status update must never break tab logic
            }
        }

        private void SetIsBasicOpen_BestEffort(bool isBasic)
        {
            try
            {
                AppStatus.IsBasicOpen = isBasic;

            }
            catch
            {
                // swallow
            }
        }

        private void ClearIsBasicOpen_BestEffort()
        {
            try
            {
                AppStatus.IsBasicOpen = false;
            }
            catch
            {
            }
        }        /* ======================= PANEL GRID REFRESH BRIDGE ======================= */
        /// <summary>
        /// Best-effort signal to Panel that the CategoryItemGrid must refresh.
        /// Panel owns the actual grid refresh call.
        /// </summary>
        private void NotifyPanel_RefreshCategoryItemGrid_BestEffort()
        {
            try
            {
                var hostPanel = FindPanelHost();
                if (hostPanel == null) return;
                hostPanel.RequestCategoryItemGridRefresh();
            }
            catch
            {
                // swallow: refresh request must never break save/tab-switch/close
            }
        }

        private void NotifyPanel_RefreshCategoryGrid_BestEffort()
        {
            try
            {
                var hostPanel = FindPanelHost();
                if (hostPanel == null) return;
                hostPanel.RequestCategoryGridRefresh();
            }
            catch
            {
                // swallow: refresh request must never break save/tab-switch/close
            }
        }
        private void EnsureBankCardsLoadedForActiveItem(bool forceReload = false)
        {
            if (BankCardsPanel == null)
                return;
            var activeId = TryGetActiveCategoryItemId();
            if (!activeId.HasValue || activeId.Value <= 0)
            {
                _bankCardsLoaded = false;
                _bankCardsLoadedItemId = 0;
                return;
            }
            int itemId = activeId.Value;
            if (!forceReload && _bankCardsLoaded && _bankCardsLoadedItemId == itemId)
                return;
            try
            {
                var rows = CategoryItemService.LoadBankCardsByItemId(itemId);
                BankCardsPanel.LoadFromHostRows(rows);
                _bankCardsLoaded = true;
                _bankCardsLoadedItemId = itemId;
            }
            catch (Exception ex)
            {
                _bankCardsLoaded = false;
                _bankCardsLoadedItemId = 0;
                SetStatus("Unable to load Bank Cards.");
            }
        }
        private void EnsureAccountsLoadedForActiveItem(bool forceReload = false)
        {
            if (AccountsPanel == null)
                return;
            var activeId = TryGetActiveCategoryItemId();
            if (!activeId.HasValue || activeId.Value <= 0)
            {
                _accountsLoaded = false;
                _accountsLoadedItemId = 0;
                return;
            }
            int itemId = activeId.Value;
            if (!forceReload && _accountsLoaded && _accountsLoadedItemId == itemId)
                return;
            try
            {
                var rows = tmp_CategoryItemAccountsService.LoadAccountListRowsByItemId(itemId);
                AccountsPanel.LoadFromHostRows(rows);
                _accountsLoaded = true;
                _accountsLoadedItemId = itemId;
            }
            catch (Exception ex)
            {
                _accountsLoaded = false;
                _accountsLoadedItemId = 0;
                SetStatus("Unable to load Accounts.");
            }
        }

        private void EnsureSecurityQuestionsLoadedForActiveItem(bool forceReload = false)
        {
            if (SecurityQuestionsPanel == null)
                return;

            var activeId = TryGetActiveCategoryItemId();
            if (!activeId.HasValue || activeId.Value <= 0)
            {
                _securityQuestionsLoaded = false;
                _securityQuestionsLoadedItemId = 0;
                return;
            }

            int itemId = activeId.Value;
            if (!forceReload && _securityQuestionsLoaded && _securityQuestionsLoadedItemId == itemId)
                return;

            try
            {
                var rows = CategoryItemSecurityQuestionsService.LoadSecurityQuestionListRowsByItemId(itemId);
                SecurityQuestionsPanel.LoadFromHostRows(rows);
                _securityQuestionsLoaded = true;
                _securityQuestionsLoadedItemId = itemId;
            }
            catch (Exception ex)
            {
                _securityQuestionsLoaded = false;
                _securityQuestionsLoadedItemId = 0;
                SetStatus("Unable to load Security Questions.");
            }
        }
        /* ======================= SEDS helpers ======================= */

        private static int? TryGetActiveCategoryItemId()
        {
            try
            {
                if (!SecureEncryptedDataStore.TryGetBytes(SedsKey_EntityKind, out var kindBytes) || kindBytes.Length == 0)
                    return null;

                string kind;
                try { kind = Encoding.UTF8.GetString(kindBytes); }
                finally { SensitiveDataCleaner.Zero(kindBytes); }

                if (!string.Equals(kind, EntityKind_CategoryItem, StringComparison.Ordinal))
                    return null;

                if (SecureEncryptedDataStore.TryGetInt32(SedsKey_EntityId, out int id) && id > 0)
                    return id;

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static bool HasPersistedIdFromSeds() => TryGetActiveCategoryItemId().HasValue;

        private static void SetSedsContextForCategoryItem(int id)
        {
            SecureEncryptedDataStore.SetString(SedsKey_EntityKind, EntityKind_CategoryItem);
            SecureEncryptedDataStore.SetInt32(SedsKey_EntityId, id);
        }

        private static void ClearSedsContext()
        {
            try { SecureEncryptedDataStore.Clear(SedsKey_EntityId); } catch { }
            try { SecureEncryptedDataStore.Clear(SedsKey_EntityKind); } catch { }
        }

        /* ======================= EXISTING ITEM VIEW-MODE TAB SWITCH ======================= */

        /// <summary>
        /// If the active item exists (PK in SEDS) AND BasicPanel is in VIEW mode,
        /// we allow switching tabs with NO validation and NO writes.
        ///
        /// Reflection is used so this file compiles regardless of BasicPanel API.
        /// It checks properties/fields using Public + NonPublic.
        /// </summary>
        private bool IsExistingItemAndBasicPanelIsViewMode()
        {
            try
            {
                if (_isClosing) return false;
                if (BasicPanel == null) return false;

                // Truth from SEDS
                var activeId = TryGetActiveCategoryItemId();
                bool isExisting = activeId.HasValue && activeId.Value > 0;
                if (!isExisting) return false;

                var t = BasicPanel.GetType();
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                bool TryReadBool(string memberName, out bool value)
                {
                    value = false;

                    var p = t.GetProperty(memberName, flags);
                    if (p != null && p.PropertyType == typeof(bool))
                    {
                        value = (bool)p.GetValue(BasicPanel)!;
                        return true;
                    }

                    var f = t.GetField(memberName, flags);
                    if (f != null && f.FieldType == typeof(bool))
                    {
                        value = (bool)f.GetValue(BasicPanel)!;
                        return true;
                    }

                    return false;
                }

                bool TryReadObject(string memberName, out object? value)
                {
                    value = null;

                    var p = t.GetProperty(memberName, flags);
                    if (p != null)
                    {
                        value = p.GetValue(BasicPanel);
                        return true;
                    }

                    var f = t.GetField(memberName, flags);
                    if (f != null)
                    {
                        value = f.GetValue(BasicPanel);
                        return true;
                    }

                    return false;
                }

                foreach (var name in new[]
                {
                    "IsViewMode", "IsInViewMode", "ViewMode",
                    "ViewOnly", "IsViewOnly",
                    "IsReadOnly", "ReadOnly"
                })
                {
                    if (TryReadBool(name, out bool view))
                    {
                        return view;
                    }
                }

                foreach (var name in new[] { "IsEditMode", "IsInEditMode", "EditUnlocked" })
                {
                    if (TryReadBool(name, out bool edit))
                    {
                        bool view = !edit;
                        return view;
                    }
                }

                foreach (var name in new[] { "Mode", "PanelMode", "EditorMode" })
                {
                    if (TryReadObject(name, out object? v) && v != null)
                    {
                        string s = v.ToString() ?? "";
                        if (string.Equals(s, "View", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(s, "VIEW", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(s, "ViewOnly", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(s, "ReadOnly", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /* ======================= BASIC PANEL: FORCE VIEW MODE (BEST-EFFORT) ======================= */

        /// <summary>
        /// After a successful Basic "Save" (stay open), we want BasicPanel to switch to VIEW-only.
        /// We do this best-effort via reflection so we don't require a specific BasicPanel API.
        /// </summary>
        private void TrySetBasicPanelToViewMode_BestEffort()
        {
            try
            {
                if (BasicPanel == null) return;

                var t = BasicPanel.GetType();
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                bool called = false;

                // 1) Try common methods first
                foreach (var methodName in new[]
                {
                    "SwitchToViewMode",
                    "EnterViewMode",
                    "SetViewMode",
                    "GoToViewMode",
                    "SetReadOnly",
                    "EnterReadOnlyMode",
                    "LockForView",
                    "LockEditing",
                    "ResetUiState"
                })
                {
                    var m0 = t.GetMethod(methodName, flags, binder: null, types: Type.EmptyTypes, modifiers: null);
                    if (m0 != null)
                    {
                        m0.Invoke(BasicPanel, null);
                        called = true;
                        break;
                    }

                    // SetReadOnly(bool) / LockEditing(bool) style
                    var m1 = t.GetMethod(methodName, flags, binder: null, types: new[] { typeof(bool) }, modifiers: null);
                    if (m1 != null)
                    {
                        m1.Invoke(BasicPanel, new object[] { true });
                        called = true;
                        break;
                    }
                }

                // 2) Try setting common bool properties/fields
                void TrySetBool(string memberName, bool value)
                {
                    var p = t.GetProperty(memberName, flags);
                    if (p != null && p.PropertyType == typeof(bool) && p.CanWrite)
                    {
                        p.SetValue(BasicPanel, value);
                        called = true;
                        return;
                    }

                    var f = t.GetField(memberName, flags);
                    if (f != null && f.FieldType == typeof(bool))
                    {
                        f.SetValue(BasicPanel, value);
                        called = true;
                    }
                }

                TrySetBool("IsViewMode", true);
                TrySetBool("IsInViewMode", true);
                TrySetBool("ViewOnly", true);
                TrySetBool("IsViewOnly", true);
                TrySetBool("IsReadOnly", true);
                TrySetBool("ReadOnly", true);

                TrySetBool("IsEditMode", false);
                TrySetBool("IsInEditMode", false);
                TrySetBool("EditUnlocked", false);

                // 3) Try setting Mode-like members to "View"
                void TrySetModeLike(string memberName)
                {
                    var p = t.GetProperty(memberName, flags);
                    if (p != null && p.CanWrite)
                    {
                        var pt = p.PropertyType;

                        if (pt == typeof(string))
                        {
                            p.SetValue(BasicPanel, "View");
                            called = true;
                            return;
                        }

                        if (pt.IsEnum)
                        {
                            object? viewEnum = null;

                            foreach (var enumName in new[] { "View", "VIEW", "ViewOnly", "ReadOnly" })
                            {
                                try
                                {
                                    viewEnum = Enum.Parse(pt, enumName, ignoreCase: true);
                                    break;
                                }
                                catch { }
                            }

                            if (viewEnum != null)
                            {
                                p.SetValue(BasicPanel, viewEnum);
                                called = true;
                            }
                        }
                    }

                    var f = t.GetField(memberName, flags);
                    if (f != null)
                    {
                        var ft = f.FieldType;

                        if (ft == typeof(string))
                        {
                            f.SetValue(BasicPanel, "View");
                            called = true;
                            return;
                        }

                        if (ft.IsEnum)
                        {
                            object? viewEnum = null;

                            foreach (var enumName in new[] { "View", "VIEW", "ViewOnly", "ReadOnly" })
                            {
                                try
                                {
                                    viewEnum = Enum.Parse(ft, enumName, ignoreCase: true);
                                    break;
                                }
                                catch { }
                            }

                            if (viewEnum != null)
                            {
                                f.SetValue(BasicPanel, viewEnum);
                                called = true;
                            }
                        }
                    }
                }

                TrySetModeLike("Mode");
                TrySetModeLike("PanelMode");
                TrySetModeLike("EditorMode");

            }
            catch
            {
                // swallow: must never break Save flow
            }
        }

        /* ======================= AFTER SIGNATURE HELPERS ======================= */

        private static void ClearAfterSignatureSedsKeys_BestEffort()
        {
            try { SecureEncryptedDataStore.Clear(SedsKey_AfterSig_Email); } catch { }
            try { SecureEncryptedDataStore.Clear(SedsKey_AfterSig_Phone); } catch { }
            try { SecureEncryptedDataStore.Clear(SedsKey_AfterSig_Pin); } catch { }
        }

        private static void CaptureAfterSignatureToSeds(string sedsKey, string purpose, string? plain)
        {
            string value = plain ?? string.Empty;

            byte[] keyBytes = SecureEncryptedDataStore.GetBytes(FieldAesCrypto.SedsKey_UserSecretsKey);

            try
            {
                byte[] sig = SensitiveValueSignature.Compute(value, keyBytes, purpose: purpose);

                try
                {
                    string b64 = Convert.ToBase64String(sig);
                    SecureEncryptedDataStore.SetString(sedsKey, b64);

                }
                finally
                {
                    SensitiveDataCleaner.Zero(sig);
                }
            }
            finally
            {
                SensitiveDataCleaner.Zero(keyBytes);
            }
        }

        private bool TryCaptureAfterSignaturesFromUi(string? emailPlain, string? phonePlain, string? pinPlain)
        {
            try
            {
                CaptureAfterSignatureToSeds(SedsKey_AfterSig_Email, Purpose_CI_Email_AfterSig, emailPlain);
                CaptureAfterSignatureToSeds(SedsKey_AfterSig_Phone, Purpose_CI_Phone_AfterSig, phonePlain);
                CaptureAfterSignatureToSeds(SedsKey_AfterSig_Pin, Purpose_CI_Pin_AfterSig, pinPlain);

                return true;
            }
            catch (Exception ex)
            {
                ClearAfterSignatureSedsKeys_BestEffort();
                return false;
            }
        }

        /* ======================= PASSWORD COMPARE HELPERS ======================= */

        private static byte[] ComputePasswordFingerprint(string passwordPlain)
        {
            byte[] keyBytes = SecureEncryptedDataStore.GetBytes(FieldAesCrypto.SedsKey_UserSecretsKey);
            try
            {
                return SensitiveValueSignature.Compute(passwordPlain ?? string.Empty, keyBytes, purpose: Purpose_PwFingerprint);
            }
            finally
            {
                SensitiveDataCleaner.Zero(keyBytes);
            }
        }

        private static bool SigEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            return CryptographicOperations.FixedTimeEquals(a, b);
        }

        private bool ShouldInsertPasswordHistoryForExistingItem(bool isBookmarkOnly, string? passwordPlain)
        {
            if (isBookmarkOnly) return false;
            if (BasicPanel == null) return false;
            if (string.IsNullOrWhiteSpace(passwordPlain)) return false;

            byte[]? before = null;
            try { before = BasicPanel.GetOriginalPasswordFingerprintCopy(); }
            catch { before = null; }

            if (before == null || before.Length == 0)
            {
                return true;
            }

            byte[] after = ComputePasswordFingerprint(passwordPlain!);

            try
            {
                bool same = SigEquals(before, after);

                return !same;
            }
            finally
            {
                SensitiveDataCleaner.Zero(before);
                SensitiveDataCleaner.Zero(after);
            }
        }

        /* ======================= NON-SENSITIVE COMPARES ======================= */

        private sealed class BasicTabChanges
        {
            public bool Any =>
                PasswordUpdated ||
                BookmarkToggled ||
                PinUpdated ||
                UsernameUpdated ||
                UrlUpdated ||
                PhoneUpdated ||
                EmailUpdated ||
                NotesUpdated ||
                CategoryItemDeactivated ||
                CategoryItemActivated ||
                CategoryItemNameChanged ||
                CategoryChanged;

            public bool PasswordUpdated { get; init; }
            public bool BookmarkToggled { get; init; }
            public bool PinUpdated { get; init; }
            public bool UsernameUpdated { get; init; }
            public bool UrlUpdated { get; init; }
            public bool PhoneUpdated { get; init; }
            public bool EmailUpdated { get; init; }
            public bool NotesUpdated { get; init; }
            public bool CategoryItemDeactivated { get; init; }
            public bool CategoryItemActivated { get; init; }
            public bool CategoryItemNameChanged { get; init; }
            public string? BeforeCategoryItemName { get; init; }
            public string? AfterCategoryItemName { get; init; }
            public bool CategoryChanged { get; init; }
            public string? BeforeCategoryName { get; init; }
            public string? AfterCategoryName { get; init; }
        }

        private static string N(string? s) => (s ?? string.Empty).Trim();

        private static bool StrEq(string? a, string? b)
            => string.Equals(N(a), N(b), StringComparison.Ordinal);

        private static bool BoolEqBookmark(int? dbValue, bool isBookmarkOnly)
        {
            int ui = isBookmarkOnly ? 1 : 0;
            int db = dbValue ?? 0;
            return db == ui;
        }

        private BasicTabChanges ComputeBasicTabChanges_ExistingItem(
            CategoryItemService.CategoryItemBasicRow beforeRow,
            bool isBookmarkOnly,
            string? afterCategoryItemName,
            string? afterNotes,
            string? afterUsername,
            string? afterUrl,
            string? afterPhone,
            string? afterEmail,
            string? afterPin,
            int? afterIsActive,
            int afterCategoryKey,
            bool passwordChangedByFingerprint)
        {
            bool bookmarkSame = BoolEqBookmark(beforeRow.BookMarkOnly, isBookmarkOnly);

            bool itemNameSame = StrEq(beforeRow.Name, afterCategoryItemName);
            bool pinSame = StrEq(beforeRow.PinPlain, afterPin);
            bool userSame = StrEq(beforeRow.Username, afterUsername);
            bool urlSame = StrEq(beforeRow.SignInUrl, afterUrl);
            bool phoneSame = StrEq(beforeRow.AccountPhonePlain, afterPhone);
            bool emailSame = StrEq(beforeRow.AccountEmailPlain, afterEmail);
            bool notesSame = StrEq(beforeRow.Description, afterNotes);
            bool wasActive = !beforeRow.IsActive.HasValue || beforeRow.IsActive.Value == 1;
            bool isActiveNow = !afterIsActive.HasValue || afterIsActive.Value == 1;
            bool categoryChanged = beforeRow.CategoryKey != afterCategoryKey;
            string? beforeCategoryName = categoryChanged ? TryLoadCategoryName(beforeRow.CategoryKey) : null;
            string? afterCategoryName = categoryChanged ? TryLoadCategoryName(afterCategoryKey) : null;

            var changes = new BasicTabChanges
            {
                PasswordUpdated = passwordChangedByFingerprint,
                BookmarkToggled = !bookmarkSame,
                PinUpdated = !pinSame,
                UsernameUpdated = !userSame,
                UrlUpdated = !urlSame,
                PhoneUpdated = !phoneSame,
                EmailUpdated = !emailSame,
                NotesUpdated = !notesSame,
                CategoryItemDeactivated = wasActive && !isActiveNow,
                CategoryItemActivated = !wasActive && isActiveNow,
                CategoryItemNameChanged = !itemNameSame,
                BeforeCategoryItemName = itemNameSame ? null : beforeRow.Name,
                AfterCategoryItemName = itemNameSame ? null : afterCategoryItemName,
                CategoryChanged = categoryChanged,
                BeforeCategoryName = beforeCategoryName,
                AfterCategoryName = afterCategoryName
            };


            return changes;
        }

        private static string? TryLoadCategoryName(int categoryKey)
        {
            try
            {
                return CategoryService.LoadCategoryByKey(categoryKey)?.Name;
            }
            catch
            {
                return null;
            }
        }

        /* ======================= LOGGING HELPERS ======================= */

        private static IReadOnlyDictionary<string, string?> BuildCommonTokens(
            string categoryName,
            string categoryItemName,
            string? beforeCategoryItemName = null,
            string? afterCategoryItemName = null,
            string? beforeCategoryName = null,
            string? afterCategoryName = null)
        {
            return new Dictionary<string, string?>
            {
                ["CategoryName"] = categoryName,
                ["CategoryItemName"] = categoryItemName,
                ["BeforeCategoryItemName"] = beforeCategoryItemName,
                ["AfterCategoryItemName"] = afterCategoryItemName,
                ["BeforeCategoryName"] = beforeCategoryName,
                ["AfterCategoryName"] = afterCategoryName
            };
        }

        private static IReadOnlyDictionary<string, string?> BuildAccountsTokens(
            string categoryItemName,
            string accountTypeDisplay,
            string accountNumberMasked)
        {
            return new Dictionary<string, string?>
            {
                ["CategoryItemName"] = categoryItemName,
                ["AccountTypeDisplay"] = accountTypeDisplay,
                ["AccountNumberMasked"] = accountNumberMasked
            };
        }

        private static IReadOnlyDictionary<string, string?> BuildBankCardTokens(
            string categoryItemName,
            CategoryItemService.BankCardSaveLogEntry entry)
        {
            return new Dictionary<string, string?>
            {
                ["CategoryItemName"] = categoryItemName,
                ["BankCardDisplayName"] = entry.BankCardDisplayName
            };
        }

        private static IReadOnlyDictionary<string, string?> BuildSecurityQuestionTokens(string categoryItemName)
        {
            return new Dictionary<string, string?>
            {
                ["CategoryItemName"] = categoryItemName
            };
        }

        private static IReadOnlyList<int> BuildBankCardTemplateSeqs(CategoryItemService.BankCardSaveLogEntry entry)
        {
            if (entry.Action == CategoryItemService.BankCardSaveLogAction.Created)
                return new[] { 4 };

            if (entry.Action == CategoryItemService.BankCardSaveLogAction.Deactivated)
                return new[] { 6 };

            if (entry.Action != CategoryItemService.BankCardSaveLogAction.Changed)
                return Array.Empty<int>();

            var seqs = new List<int> { 5 };

            if (entry.CardTypeChanged) seqs.Add(7);
            if (entry.CardholderChanged) seqs.Add(8);
            if (entry.ExpirationChanged) seqs.Add(9);
            if (entry.ActiveChanged) seqs.Add(10);
            if (entry.CardNumberChanged) seqs.Add(11);
            if (entry.CvvChanged) seqs.Add(12);
            if (entry.PinChanged) seqs.Add(13);
            if (entry.BillingZipChanged) seqs.Add(14);

            return seqs.Count > 1 ? seqs : Array.Empty<int>();
        }

        private static string BuildCategoryItemCreatedMessage(string categoryName, string categoryItemName)
        {
            return Template_NewItemCreated
                .Replace("#CategoryItemName#", categoryItemName ?? string.Empty)
                .Replace("#CategoryName#", categoryName ?? string.Empty);
        }

        private void TryWriteNewItemCreatedLog_BestEffort(long itemId, string categoryName, string itemName)
        {
            try
            {
                var write = new TemplateLogWriter.WriteRequest
                {
                    Level = "INFO",
                    Source = LogSource_CategoryItem,
                    EventCode = LogEvent_CategoryItemCreated,
                    CreatedUtc = DateTime.UtcNow,
                    WhenUtc = DateTime.UtcNow,
                    ItemId = itemId,

                    SubjectText = itemName,
                    MessageText = BuildCategoryItemCreatedMessage(categoryName, itemName),

                    KeySetVersion = 1
                };

                var logId = TemplateLogWriter.InsertRendered(write);

            }
            catch (Exception ex)
            {
            }
        }

        private void TryWriteExistingItemChangedLog_BestEffort(
            long itemId,
            string categoryName,
            string itemName,
            BasicTabChanges changes)
        {
            if (changes == null || !changes.Any)
                return;

            try
            {
                var seqs = new List<int>(capacity: 11) { 2 };

                if (changes.PasswordUpdated) seqs.Add(3);
                if (changes.BookmarkToggled) seqs.Add(4);
                if (changes.PinUpdated) seqs.Add(5);
                if (changes.UsernameUpdated) seqs.Add(6);
                if (changes.UrlUpdated) seqs.Add(7);
                if (changes.PhoneUpdated) seqs.Add(8);
                if (changes.EmailUpdated) seqs.Add(9);
                if (changes.NotesUpdated) seqs.Add(10);
                if (changes.CategoryItemDeactivated) seqs.Add(12);
                if (changes.CategoryItemActivated) seqs.Add(13);
                if (changes.CategoryItemNameChanged) seqs.Add(15);
                if (changes.CategoryChanged) seqs.Add(14);

                if (seqs.Count <= 1)
                    return;

                var write = new TemplateLogWriter.WriteRequest
                {
                    Level = "INFO",
                    Source = LogSource_CategoryItem,
                    EventCode = LogEvent_CategoryItemChanged,
                    CreatedUtc = DateTime.UtcNow,
                    WhenUtc = DateTime.UtcNow,
                    ItemId = itemId,

                    SubjectText = itemName,
                    KeySetVersion = 1
                };

                string beforeCategory = string.IsNullOrWhiteSpace(changes.BeforeCategoryName)
                    ? categoryName
                    : changes.BeforeCategoryName!;
                string afterCategory = string.IsNullOrWhiteSpace(changes.AfterCategoryName)
                    ? "selected category"
                    : changes.AfterCategoryName!;
                string beforeCategoryItemName = string.IsNullOrWhiteSpace(changes.BeforeCategoryItemName)
                    ? itemName
                    : changes.BeforeCategoryItemName!;
                string afterCategoryItemName = string.IsNullOrWhiteSpace(changes.AfterCategoryItemName)
                    ? itemName
                    : changes.AfterCategoryItemName!;

                var tokens = BuildCommonTokens(
                    categoryName,
                    itemName,
                    beforeCategoryItemName: beforeCategoryItemName,
                    afterCategoryItemName: afterCategoryItemName,
                    beforeCategoryName: beforeCategory,
                    afterCategoryName: afterCategory);

                string? message = TemplateLogWriter.BuildMessageFromTemplates_BestEffort(
                    updateForm: TemplateForm_BasicTab,
                    seqsInOrder: seqs,
                    tokens: tokens);

                if (string.IsNullOrWhiteSpace(message))
                    return;

                write.MessageText = message;
                var logId = TemplateLogWriter.InsertRendered_BestEffort(write);

            }
            catch (Exception ex)
            {
            }
        }

        private void TryWriteAccountCreatedLog_BestEffort(
            long itemId,
            string itemName,
            string accountTypeDisplay,
            string accountNumberMasked)
        {
            try
            {
                var write = new TemplateLogWriter.WriteRequest
                {
                    Level = "INFO",
                    Source = LogSource_CategoryItem,
                    EventCode = LogEvent_AccountsCreated,
                    CreatedUtc = DateTime.UtcNow,
                    WhenUtc = DateTime.UtcNow,
                    ItemId = itemId,
                    SubjectText = itemName,
                    KeySetVersion = 1
                };

                var tokens = BuildAccountsTokens(itemName, accountTypeDisplay, accountNumberMasked);

                var logId = TemplateLogWriter.InsertFromTemplates_BestEffort(
                    updateForm: TemplateForm_AccountsTab,
                    seqsInOrder: new[] { 1 },
                    tokens: tokens,
                    write: write);

            }
            catch (Exception ex)
            {
            }
        }

        private void TryWriteAccountDeactivatedLog_BestEffort(
            long itemId,
            string itemName,
            string accountTypeDisplay,
            string accountNumberMasked)
        {
            try
            {
                var write = new TemplateLogWriter.WriteRequest
                {
                    Level = "INFO",
                    Source = LogSource_CategoryItem,
                    EventCode = LogEvent_AccountsChanged,
                    CreatedUtc = DateTime.UtcNow,
                    WhenUtc = DateTime.UtcNow,
                    ItemId = itemId,
                    SubjectText = itemName,
                    KeySetVersion = 1
                };

                var tokens = BuildAccountsTokens(itemName, accountTypeDisplay, accountNumberMasked);

                var logId = TemplateLogWriter.InsertFromTemplates_BestEffort(
                    updateForm: TemplateForm_AccountsTab,
                    seqsInOrder: new[] { 2 },
                    tokens: tokens,
                    write: write);

            }
            catch (Exception ex)
            {
            }
        }

        private void TryWriteBankCardLog_BestEffort(
            long itemId,
            string itemName,
            CategoryItemService.BankCardSaveLogEntry entry)
        {
            if (entry == null || entry.Action == CategoryItemService.BankCardSaveLogAction.Unchanged)
                return;

            try
            {
                string eventCode;

                switch (entry.Action)
                {
                    case CategoryItemService.BankCardSaveLogAction.Created:
                        eventCode = LogEvent_BankCardCreated;
                        break;

                    case CategoryItemService.BankCardSaveLogAction.Deactivated:
                        eventCode = LogEvent_BankCardDeactivated;
                        break;

                    case CategoryItemService.BankCardSaveLogAction.Changed:
                        eventCode = LogEvent_BankCardChanged;
                        break;

                    default:
                        return;
                }

                var write = new TemplateLogWriter.WriteRequest
                {
                    Level = "INFO",
                    Source = LogSource_CategoryItem,
                    EventCode = eventCode,
                    CreatedUtc = DateTime.UtcNow,
                    WhenUtc = DateTime.UtcNow,
                    ItemId = itemId,
                    SubjectText = itemName,
                    KeySetVersion = 1
                };

                var seqs = BuildBankCardTemplateSeqs(entry);
                if (seqs.Count == 0)
                    return;

                var tokens = BuildBankCardTokens(itemName, entry);

                var logId = TemplateLogWriter.InsertFromTemplates_BestEffort(
                    updateForm: TemplateForm_BankCardsTab,
                    seqsInOrder: seqs,
                    tokens: tokens,
                    write: write);

            }
            catch (Exception ex)
            {
            }
        }

        private void TryWriteSecurityQuestionLog_BestEffort(
            long itemId,
            string itemName,
            string eventCode,
            int templateSeq)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(eventCode) || templateSeq <= 0)
                    return;

                var write = new TemplateLogWriter.WriteRequest
                {
                    Level = "INFO",
                    Source = LogSource_CategoryItem,
                    EventCode = eventCode,
                    CreatedUtc = DateTime.UtcNow,
                    WhenUtc = DateTime.UtcNow,
                    ItemId = itemId,
                    SubjectText = itemName,
                    KeySetVersion = 1
                };

                var tokens = BuildSecurityQuestionTokens(itemName);

                var logId = TemplateLogWriter.InsertFromTemplates_BestEffort(
                    updateForm: TemplateForm_SecurityQuestionsTab,
                    seqsInOrder: new[] { templateSeq },
                    tokens: tokens,
                    write: write);

            }
            catch (Exception ex)
            {
            }
        }

        /* ======================= Public Open API ======================= */

        public void ConfigureForOpen(int categoryKey, string categoryName)
        {
            _categoryKey = categoryKey;
            _categoryName = categoryName;

            _hasPersistedId = HasPersistedIdFromSeds();


            InitializeUiForOpen();
        }

        public void ConfigureForAdd(int categoryKey, string categoryName)
        {
            _categoryKey = categoryKey;
            _categoryName = categoryName;

            ClearSedsContext();
            _hasPersistedId = false;


            InitializeUiForOpen();
        }

        public void ConfigureForEdit(int categoryKey, string categoryName, object existingItem)
        {
            _categoryKey = categoryKey;
            _categoryName = categoryName;

            _hasPersistedId = HasPersistedIdFromSeds();


            InitializeUiForOpen();
        }

        private void InitializeUiForOpen()
        {
            HookPanelsOnce();
            BankCardsDraftRows = Array.Empty<CategoryItemBankCardsPanel.BankCardRow>();
            AccountsDraftRows = Array.Empty<CategoryItemAccountsPanel.AccountRow>();
            SecurityQuestionsDraftRows = Array.Empty<CategoryItemSecurityQuestionsPanel.SecurityQuestionRow>();
            _bankCardsLoaded = false;
            _bankCardsLoadedItemId = 0;
            _accountsLoaded = false;
            _accountsLoadedItemId = 0;
            _securityQuestionsLoaded = false;
            _securityQuestionsLoadedItemId = 0;

            if (ItemTabs != null)
            {
                ItemTabs.SelectionChanged -= ItemTabs_SelectionChanged;
                ItemTabs.SelectionChanged += ItemTabs_SelectionChanged;

                if (ItemTabs.SelectedIndex < 0)
                    ItemTabs.SelectedIndex = TabIndexBasic;

                _lastTabIndex = ItemTabs.SelectedIndex;
            }

            // IMPORTANT: sync status for inactivity timer
            UpdateIsBasicOpenFromUi_BestEffort();

            _hasPersistedId = HasPersistedIdFromSeds();

            if (_hasPersistedId)
            {
                try { BasicPanel?.ResetUiState(); } catch { }
            }
            else
            {
                try { BasicPanel?.ClearForm(); } catch { }
                try { BasicPanel?.ResetUiState(); } catch { }
            }

            SetStatus("");
        }

        /* ======================= Lifecycle ======================= */

        private void CategoryItemEditorTabs_Loaded(object? sender, RoutedEventArgs e)
        {
            HookPanelsOnce();

            if (ItemTabs != null)
            {
                ItemTabs.SelectionChanged -= ItemTabs_SelectionChanged;
                ItemTabs.SelectionChanged += ItemTabs_SelectionChanged;
                _lastTabIndex = ItemTabs.SelectedIndex;
            }

            _hasPersistedId = HasPersistedIdFromSeds();
            InitializeUiForOpen();

            // IMPORTANT: sync status for inactivity timer
            UpdateIsBasicOpenFromUi_BestEffort();

            SetStatus("");
        }

        private void CategoryItemEditorTabs_Unloaded(object? sender, RoutedEventArgs e)
        {
            UnhookPanels();

            if (ItemTabs != null)
                ItemTabs.SelectionChanged -= ItemTabs_SelectionChanged;

            try
            {
                BasicPanel?.WipeAllForHostClose();
                BankCardsPanel?.WipeAllForHostClose();
                AccountsPanel?.WipeAllForHostClose();
                SecurityQuestionsPanel?.WipeAllForHostClose();
            }
            catch { }

            ResetStateForCloseCleanup();
            SetStatus("");
        }

        /* ======================= Host API ======================= */

        public void WipeAllForHostClose()
        {
            if (_isClosing)
                return;

            _isClosing = true;

            try
            {
                TryPreparePanelsForHostClose();
                SetStatus("");
            }
            finally
            {
                ResetStateForCloseCleanup();
            }
        }

        private void ResetStateForCloseCleanup()
        {
            try { ClearSedsContext(); } catch { }
            BankCardsDraftRows = Array.Empty<CategoryItemBankCardsPanel.BankCardRow>();
            AccountsDraftRows = Array.Empty<CategoryItemAccountsPanel.AccountRow>();
            SecurityQuestionsDraftRows = Array.Empty<CategoryItemSecurityQuestionsPanel.SecurityQuestionRow>();
            _bankCardsLoaded = false;
            _bankCardsLoadedItemId = 0;
            _accountsLoaded = false;
            _accountsLoadedItemId = 0;
            _securityQuestionsLoaded = false;
            _securityQuestionsLoadedItemId = 0;

            // IMPORTANT: leaving editor means Basic is not open
            ClearIsBasicOpen_BestEffort();
        }

        private void TryPreparePanelsForHostClose()
        {
            try
            {
                BasicPanel?.WipeAllForHostClose();
            }
            catch (Exception ex)
            {
            }

            try { BankCardsPanel?.WipeAllForHostClose(); }
            catch (Exception ex)
            {
            }

            try { AccountsPanel?.WipeAllForHostClose(); }
            catch (Exception ex)
            {
            }

            try { SecurityQuestionsPanel?.WipeAllForHostClose(); }
            catch (Exception ex)
            {
            }
        }

        /// <summary>
        /// Host-callable "press Cancel" that reuses the existing cancel path.
        /// This delegates to BasicPanel.ForceCancelFromHost(), which raises CancelRequested
        /// (same as clicking Cancel).
        /// </summary>
        public void ForceCancelFromHost()
        {
            try
            {
                BasicPanel?.ForceCancelFromHost();
            }
            catch
            {
                // swallow (host-controlled flow)
            }
        }

        /// <summary>
        /// Host-close preflight decision for the active editor session.
        /// Returns true when close may continue; false when close should be canceled.
        /// </summary>
        public bool TryHostClosePreflight()
        {
            if (_isClosing)
            {
                return true;
            }

            if (BasicPanel == null)
            {
                return true;
            }

            bool bankCardsHasHostCloseSessionWork = false;
            try { bankCardsHasHostCloseSessionWork = BankCardsPanel?.HasHostCloseSessionWork() ?? false; } catch { }

            bool securityQuestionsHasHostCloseSessionWork = false;
            try { securityQuestionsHasHostCloseSessionWork = SecurityQuestionsPanel?.HasHostCloseSessionWork() ?? false; } catch { }


            if (bankCardsHasHostCloseSessionWork)
            {
                bool bankCardsAllowed = TryResolveBankCardsHostCloseDecision();
                if (!bankCardsAllowed)
                {
                    _lastTabIndex = ItemTabs?.SelectedIndex ?? _lastTabIndex;
                    UpdateIsBasicOpenFromUi_BestEffort();

                    return false;
                }
            }

            if (securityQuestionsHasHostCloseSessionWork)
            {
                bool securityQuestionsAllowed = TryResolveSecurityQuestionsHostCloseDecision();
                if (!securityQuestionsAllowed)
                {
                    _lastTabIndex = ItemTabs?.SelectedIndex ?? _lastTabIndex;
                    UpdateIsBasicOpenFromUi_BestEffort();

                    return false;
                }
            }

            // Existing item in pure view mode can close without prompting/commit.
            if (IsExistingItemAndBasicPanelIsViewMode())
            {
                return true;
            }

            // Keep focus on Basic for commit prompt/validation workflow.
            if (ItemTabs != null && ItemTabs.SelectedIndex != TabIndexBasic)
                ForceSelectTab(TabIndexBasic);

            bool allowed = TryResolveBasicHostCloseDecision();
            if (!allowed)
            {
                if (ItemTabs != null && ItemTabs.SelectedIndex != TabIndexBasic)
                    ForceSelectTab(TabIndexBasic);

                _lastTabIndex = TabIndexBasic;
                UpdateIsBasicOpenFromUi_BestEffort();
            }

            return allowed;
        }

        /* ======================= Persistence ======================= */

        private enum PersistTrigger
        {
            LeaveBasicTab,
            Save
        }

        private enum HostCloseDecision
        {
            SaveAndExit,
            ExitWithoutSave,
            CancelExit
        }

        private bool TryPersistBasicIfNeeded(PersistTrigger trigger, bool isBookmarkOnly)
        {
            if (_isClosing)
                return true;

            if (BasicPanel == null)
                return true;

            var activeId = TryGetActiveCategoryItemId();
            bool isExisting = _hasPersistedId && activeId.HasValue && activeId.Value > 0;


            // =========================
            // EXISTING ITEM
            // =========================
            if (isExisting)
            {
                if (trigger == PersistTrigger.LeaveBasicTab)
                {
                    return true;
                }

                try
                {
                    long itemId = activeId!.Value;


                    CategoryItemService.CategoryItemBasicRow? beforeRow = null;
                    try { beforeRow = CategoryItemService.LoadCategoryItemBasicById(itemId); }
                    catch { beforeRow = null; }

                    if (beforeRow == null)
                    {
                    }

                    string name = BasicPanel.GetItemNameTrim();
                    string? desc = BasicPanel.GetDescriptionTrimOrNull();
                    string? username = BasicPanel.GetUsernameTrimOrNull();
                    string? url = BasicPanel.GetUrlTrimOrNull();

                    string? emailPlain = BasicPanel.GetEmailTrimOrNull();
                    string? phonePlain = BasicPanel.GetPhoneTrimOrNull();
                    string? pinPlain = BasicPanel.GetPinTrimOrNull();

                    int? isActive = BasicPanel.GetIsActiveInt();
                    int? bookMarkOnly = isBookmarkOnly ? 1 : 0;
                    int categoryKey = BasicPanel.GetSelectedCategoryKeyForExistingEdit();

                    int rows = CategoryItemService.UpdateCategoryItemBasic(
                        itemId: itemId,
                        categoryKey: categoryKey,
                        name: name,
                        description: desc,
                        username: username,
                        signInUrl: url,
                        bookMarkOnly: bookMarkOnly,
                        accountEmailPlain: emailPlain,
                        accountPhonePlain: phonePlain,
                        pinPlain: pinPlain,
                        isActive: isActive);

                    if (rows < 0)
                    {
                        SetStatus("Update failed.");
                        return false;
                    }

                    if (rows == 0)
                    {
                    }
                    else
                    {
                    }

                    SetSedsContextForCategoryItem(activeId.Value);
                    _hasPersistedId = true;

                    if (!TryCaptureAfterSignaturesFromUi(emailPlain, phonePlain, pinPlain))
                    {
                        SetStatus("Saved, but after-signature capture failed (see debug output).");
                        return false;
                    }


                    bool passwordChangedByFingerprint = false;

                    if (isBookmarkOnly)
                    {
                    }
                    else
                    {
                        string? pw = BasicPanel.GetPasswordPlainOrNull();


                        bool shouldInsert = ShouldInsertPasswordHistoryForExistingItem(isBookmarkOnly: false, passwordPlain: pw);
                        passwordChangedByFingerprint = shouldInsert;

                        if (!shouldInsert)
                        {
                        }
                        else
                        {
                            long pwHistId;

                            try
                            {
                                pwHistId = CategoryItemService.InsertPasswordHistoryForExistingItem(
                                    itemId: itemId,
                                    passwordPlain: pw!,
                                    isBookmarkOnly: false,
                                    allowDuplicate: false);
                            }
                            catch (CategoryItemService.DuplicatePasswordWarningException)
                            {

                                if (!PromptDuplicatePasswordAccept())
                                {
                                    SetStatus("Save canceled (duplicate password warning).");
                                    return false;
                                }


                                pwHistId = CategoryItemService.InsertPasswordHistoryForExistingItem(
                                    itemId: itemId,
                                    passwordPlain: pw!,
                                    isBookmarkOnly: false,
                                    allowDuplicate: true);
                            }

                            if (pwHistId <= 0)
                            {
                                SetStatus("Saved Basic fields, but password history insert failed.");
                                return false;
                            }

                        }
                    }

                    if (beforeRow != null)
                    {
                        var changes = ComputeBasicTabChanges_ExistingItem(
                            beforeRow: beforeRow,
                            isBookmarkOnly: isBookmarkOnly,
                            afterCategoryItemName: name,
                            afterNotes: desc,
                            afterUsername: username,
                            afterUrl: url,
                            afterPhone: phonePlain,
                            afterEmail: emailPlain,
                            afterPin: pinPlain,
                            afterIsActive: isActive,
                            afterCategoryKey: categoryKey,
                            passwordChangedByFingerprint: passwordChangedByFingerprint);

                        if (changes.Any)
                        {
                            TryWriteExistingItemChangedLog_BestEffort(
                                itemId: itemId,
                                categoryName: _categoryName,
                                itemName: name,
                                changes: changes);
                        }
                    }

                    var savedCategory = CategoryService.LoadCategoryByKey(categoryKey);
                    if (savedCategory != null)
                    {
                        _categoryKey = savedCategory.CategoryKey;
                        _categoryName = savedCategory.Name;
                    }

                SetStatus("");
                return true;
            }
            catch (CategoryItemService.CategoryItemReactivationBlockedException ex)
            {
                SetStatus(ex.Message);
                ShowInformationalPopup(
                    title: "Cannot Reactivate Item",
                    body: ex.Message,
                    debugContext: "ITEM-REACTIVATE-BLOCKED");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                SetStatus(ex.Message);
                return false;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                SetStatus(ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                    SetStatus("Save failed. See debug output.");
                    return false;
                }
            }

            // =========================
            // NEW ITEM
            // =========================
            if (_hasPersistedId && !activeId.HasValue)
            {
                _hasPersistedId = false;
            }

            try
            {

                string name = BasicPanel.GetItemNameTrim();
                string? desc = BasicPanel.GetDescriptionTrimOrNull();
                string? username = BasicPanel.GetUsernameTrimOrNull();
                string? url = BasicPanel.GetUrlTrimOrNull();

                string? emailPlain = BasicPanel.GetEmailTrimOrNull();
                string? phonePlain = BasicPanel.GetPhoneTrimOrNull();
                string? pinPlain = BasicPanel.GetPinTrimOrNull();

                int? isActive = BasicPanel.GetIsActiveInt();

                long newId;

                if (isBookmarkOnly)
                {
                    newId = CategoryItemService.InsertCategoryItemOnly(
                        categoryKey: _categoryKey,
                        name: name,
                        description: desc,
                        username: username,
                        signInUrl: url,
                        accountEmailPlain: emailPlain,
                        accountPhonePlain: phonePlain,
                        pinPlain: pinPlain,
                        isActive: isActive
                    );
                }
                else
                {
                    string? passwordPlain = BasicPanel.GetPasswordPlainOrNull();


                    try
                    {
                        newId = CategoryItemService.InsertCategoryItemWithPasswordHistory(
                            categoryKey: _categoryKey,
                            name: name,
                            description: desc,
                            username: username,
                            signInUrl: url,
                            accountEmailPlain: emailPlain,
                            accountPhonePlain: phonePlain,
                            pinPlain: pinPlain,
                            isActive: isActive,
                            passwordPlain: passwordPlain,
                            isBookmarkOnly: false,
                            allowDuplicate: false);
                    }
                    catch (CategoryItemService.DuplicatePasswordWarningException)
                    {

                        if (!PromptDuplicatePasswordAccept())
                        {
                            SetStatus("Save canceled (duplicate password warning).");
                            return false;
                        }


                        newId = CategoryItemService.InsertCategoryItemWithPasswordHistory(
                            categoryKey: _categoryKey,
                            name: name,
                            description: desc,
                            username: username,
                            signInUrl: url,
                            accountEmailPlain: emailPlain,
                            accountPhonePlain: phonePlain,
                            pinPlain: pinPlain,
                            isActive: isActive,
                            passwordPlain: passwordPlain,
                            isBookmarkOnly: false,
                            allowDuplicate: true);
                    }
                }

                if (newId <= 0)
                {
                    SetStatus("Insert failed (no ItemId returned).");
                    return false;
                }

                if (newId > int.MaxValue)
                {
                    SetStatus("Insert failed (ItemId overflow).");
                    return false;
                }

                SetSedsContextForCategoryItem((int)newId);
                _hasPersistedId = true;


                TryWriteNewItemCreatedLog_BestEffort(
                    itemId: newId,
                    categoryName: _categoryName,
                    itemName: name);

                // Save (even if staying open) must capture after-signatures for consistency.
                if (!TryCaptureAfterSignaturesFromUi(emailPlain, phonePlain, pinPlain))
                {
                    SetStatus("Inserted, but after-signature capture failed (see debug output).");
                    return false;
                }

                if (trigger == PersistTrigger.LeaveBasicTab)
                    SetStatus("");

                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Insert failed. See debug output.");
                return false;
            }
        }

        /* ======================= Duplicate Password Popup ======================= */

        private bool PromptDuplicatePasswordAccept()
        {
            const string title = "Duplicate Password Warning";
            const string body =
                "This password is currently used or has been previously used.\n\n" +
                "Accept to continue.";

            var result = ShowCustomPopupDecision(
                title: title,
                body: body,
                primaryText: "Accept",
                secondaryText: "Cancel",
                debugContext: "DUPLICATE-PASSWORD",
                safeFallbackResult: PopupDialog.PopupResult.Cancel);


            return result == PopupDialog.PopupResult.Accept;
        }

        private MWPV.View.UserControls.Panel? FindPanelHost()
        {
            try
            {
                DependencyObject? cur = this;

                while (cur != null)
                {
                    if (cur is MWPV.View.UserControls.Panel p)
                        return p;

                    cur = VisualTreeHelper.GetParent(cur);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /* ======================= Panel hooks ======================= */

        private void HookPanelsOnce()
        {
            if (_panelsHooked)
                return;

            if (BasicPanel != null)
            {
                BasicPanel.SaveRequested -= BasicPanel_SaveRequested;
                BasicPanel.CancelRequested -= BasicPanel_CancelRequested;
                BasicPanel.PasswordValidationFailed -= BasicPanel_PasswordValidationFailed;

                BasicPanel.SaveRequested += BasicPanel_SaveRequested;
                BasicPanel.CancelRequested += BasicPanel_CancelRequested;
                BasicPanel.PasswordValidationFailed += BasicPanel_PasswordValidationFailed;
            }

            if (BankCardsPanel != null)
            {
                BankCardsPanel.SaveAndExitRequested -= BankCardsPanel_SaveAndExitRequested;
                BankCardsPanel.CancelAndExitRequested -= BankCardsPanel_CancelAndExitRequested;

                BankCardsPanel.SaveAndExitRequested += BankCardsPanel_SaveAndExitRequested;
                BankCardsPanel.CancelAndExitRequested += BankCardsPanel_CancelAndExitRequested;
            }

            if (AccountsPanel != null)
            {
                AccountsPanel.SaveAndExitRequested -= AccountsPanel_SaveAndExitRequested;
                AccountsPanel.CancelAndExitRequested -= AccountsPanel_CancelAndExitRequested;

                AccountsPanel.SaveAndExitRequested += AccountsPanel_SaveAndExitRequested;
                AccountsPanel.CancelAndExitRequested += AccountsPanel_CancelAndExitRequested;
            }

            if (SecurityQuestionsPanel != null)
            {
                SecurityQuestionsPanel.SaveAndExitRequested -= SecurityQuestionsPanel_SaveAndExitRequested;
                SecurityQuestionsPanel.CancelAndExitRequested -= SecurityQuestionsPanel_CancelAndExitRequested;

                SecurityQuestionsPanel.SaveAndExitRequested += SecurityQuestionsPanel_SaveAndExitRequested;
                SecurityQuestionsPanel.CancelAndExitRequested += SecurityQuestionsPanel_CancelAndExitRequested;
            }

            _panelsHooked = true;
        }

        private void UnhookPanels()
        {
            if (!_panelsHooked)
                return;

            if (BasicPanel != null)
            {
                BasicPanel.SaveRequested -= BasicPanel_SaveRequested;
                BasicPanel.CancelRequested -= BasicPanel_CancelRequested;
                BasicPanel.PasswordValidationFailed -= BasicPanel_PasswordValidationFailed;
            }

            if (BankCardsPanel != null)
            {
                BankCardsPanel.SaveAndExitRequested -= BankCardsPanel_SaveAndExitRequested;
                BankCardsPanel.CancelAndExitRequested -= BankCardsPanel_CancelAndExitRequested;
            }

            if (AccountsPanel != null)
            {
                AccountsPanel.SaveAndExitRequested -= AccountsPanel_SaveAndExitRequested;
                AccountsPanel.CancelAndExitRequested -= AccountsPanel_CancelAndExitRequested;
            }

            if (SecurityQuestionsPanel != null)
            {
                SecurityQuestionsPanel.SaveAndExitRequested -= SecurityQuestionsPanel_SaveAndExitRequested;
                SecurityQuestionsPanel.CancelAndExitRequested -= SecurityQuestionsPanel_CancelAndExitRequested;
            }

            _panelsHooked = false;
        }

        private void BasicPanel_PasswordValidationFailed(object? sender, string message)
        {
            PasswordValidationFailed?.Invoke(this, message);
        }

        /* ======================= Selection helper ======================= */

        private void ForceSelectTab(int index)
        {
            if (ItemTabs == null) return;

            try
            {
                _handlingTabSelection = true;
                ItemTabs.SelectedIndex = index;
            }
            finally
            {
                _handlingTabSelection = false;

                // IMPORTANT: keep AppStatus in sync even for forced selections
                UpdateIsBasicOpenFromUi_BestEffort();
            }
        }

        private bool TryCommitBasicFromBasicFlow(string validationFailureStatus)
        {
            SetStatus("");

            if (BasicPanel == null)
                return true;

            // Basic is effectively "open" while we're in this editor, but keep it driven by tab index.
            SetIsBasicOpen_BestEffort(true);

            BasicPanel.NormalizeUsernameFromEmailIfEmpty();

            if (!BasicPanel.TryValidateAllForSubmit(out bool isBookmarkOnly, out bool okName, out bool okPassword, out bool okPin, out bool okEmail, out bool okPhone))
            {
                if (ItemTabs != null && ItemTabs.SelectedIndex != TabIndexBasic)
                    ForceSelectTab(TabIndexBasic);

                BasicPanel.FocusFirstError(okName, okPassword, okPin, okEmail, okPhone, isBookmarkOnly);
                SetStatus(validationFailureStatus);
                return false;
            }

            // Basic commits must persist through the explicit Save trigger path.
            if (!TryPersistBasicIfNeeded(PersistTrigger.Save, isBookmarkOnly))
            {
                if (ItemTabs != null && ItemTabs.SelectedIndex != TabIndexBasic)
                    ForceSelectTab(TabIndexBasic);

                return false;
            }

            return true;
        }

        /* ======================= BasicPanel events ======================= */

        private void BasicPanel_SaveRequested(object? sender, EventArgs e)
        {
            SetStatus("");

            if (BasicPanel == null)
                return;

            if (!TryCommitBasicFromBasicFlow("Fix the highlighted errors before saving."))
                return;

            // Refresh grid after successful save
            NotifyPanel_RefreshCategoryItemGrid_BestEffort();
            NotifyPanel_RefreshCategoryGrid_BestEffort();

            // Stay on Basic tab
            ForceSelectTab(TabIndexBasic);
            _lastTabIndex = TabIndexBasic;

            // After saving: reload Basic to force VIEW mode (resets edit-unlock)
            // Yes, this costs extra I/O. That's intentional for correctness.
            BasicPanel.PopulateFromDbForCurrentEntity();

            SetStatus("");
        }

        private void BasicPanel_CancelRequested(object? sender, EventArgs e)
        {
            HandleCancelAndExitRequest();
        }

        /* ======================= Bank Cards integration ======================= */

        private void BankCardsPanel_SaveAndExitRequested(object? sender, CategoryItemBankCardsPanel.BankCardsCommitEventArgs e)
        {
            TryPersistBankCardsRows(
                rows: e.Rows ?? Array.Empty<CategoryItemBankCardsPanel.BankCardRow>(),
                selectBankCardsOnSuccess: true);
        }
        private void BankCardsPanel_CancelAndExitRequested(object? sender, EventArgs e)
        {
            HandleCancelAndExitRequest();
        }

        /* ======================= Accounts integration ======================= */

        private void AccountsPanel_SaveAndExitRequested(object? sender, CategoryItemAccountsPanel.AccountsCommitEventArgs e)
        {
            TryPersistNewAccountsRowsAndReload(
                e.Rows ?? Array.Empty<CategoryItemAccountsPanel.AccountRow>());
        }

        private void AccountsPanel_CancelAndExitRequested(object? sender, EventArgs e)
        {
            HandleCancelAndExitRequest();
        }

        /* ======================= Security Questions integration ======================= */

        private void SecurityQuestionsPanel_SaveAndExitRequested(object? sender, CategoryItemSecurityQuestionsPanel.SecurityQuestionsCommitEventArgs e)
        {
            TryPersistSecurityQuestionsRows(
                rows: e.Rows ?? Array.Empty<CategoryItemSecurityQuestionsPanel.SecurityQuestionRow>(),
                selectSecurityQuestionsOnSuccess: true);
        }

        private void SecurityQuestionsPanel_CancelAndExitRequested(object? sender, EventArgs e)
        {
            HandleCancelAndExitRequest();
        }

        private bool TryPersistSecurityQuestionsRows(
            IReadOnlyList<CategoryItemSecurityQuestionsPanel.SecurityQuestionRow> rows,
            bool selectSecurityQuestionsOnSuccess)
        {
            SecurityQuestionsDraftRows = (rows ?? Array.Empty<CategoryItemSecurityQuestionsPanel.SecurityQuestionRow>()).ToList();
            SetStatus("");

            if (BasicPanel == null)
                return true;

            bool bypassBasicPersist = IsExistingItemAndBasicPanelIsViewMode();
            bool isBookmarkOnly = false;

            if (!bypassBasicPersist)
            {
                BasicPanel.NormalizeUsernameFromEmailIfEmpty();
                if (!BasicPanel.TryValidateAllForSubmit(out isBookmarkOnly, out bool okName, out bool okPassword, out bool okPin, out bool okEmail, out bool okPhone))
                {
                    if (ItemTabs != null)
                        ForceSelectTab(TabIndexBasic);

                    BasicPanel.FocusFirstError(okName, okPassword, okPin, okEmail, okPhone, isBookmarkOnly);
                    SetStatus("Security Questions are staged, fix Basic tab errors before saving.");

                    return false;
                }

                if (!TryPersistBasicIfNeeded(PersistTrigger.Save, isBookmarkOnly))
                {
                    if (ItemTabs != null)
                        ForceSelectTab(TabIndexBasic);

                    return false;
                }
            }


            var activeId = TryGetActiveCategoryItemId();
            if (!activeId.HasValue || activeId.Value <= 0)
            {
                SetStatus("Security Questions save failed: missing ItemId context.");
                return false;
            }

            int itemId = activeId.Value;
            string itemName = BasicPanel?.GetItemNameTrim() ?? string.Empty;

            try
            {
                int writes = 0;

                foreach (var row in SecurityQuestionsDraftRows.Where(r => r != null))
                {
                    if (row.Id <= 0)
                    {
                        int nextSeq = CategoryItemSecurityQuestionsService.GetNextSeqForItem(itemId);

                        long insertedId = CategoryItemSecurityQuestionsService.InsertCategoryItemSecurityQuestionFromUi(
                            itemId: itemId,
                            seq: nextSeq,
                            questionPlain: row.QuestionPlain ?? string.Empty,
                            answerPlain: row.AnswerRaw ?? string.Empty,
                            isActive: row.IsActive);

                        if (insertedId <= 0)
                            throw new InvalidOperationException("CategoryItemSecurityQuestion insert failed.");

                        writes++;
                        TryWriteSecurityQuestionLog_BestEffort(
                            itemId: itemId,
                            itemName: itemName,
                            eventCode: LogEvent_SecurityQuestionCreated,
                            templateSeq: 1);
                    }
                    else if (!row.IsActive && string.IsNullOrWhiteSpace(row.AnswerRaw))
                    {
                        int affected = CategoryItemSecurityQuestionsService.DeactivateCategoryItemSecurityQuestion(
                            id: row.Id,
                            itemId: itemId);

                        if (affected <= 0)
                            throw new InvalidOperationException("CategoryItemSecurityQuestion deactivate failed.");

                        writes += affected;
                        TryWriteSecurityQuestionLog_BestEffort(
                            itemId: itemId,
                            itemName: itemName,
                            eventCode: LogEvent_SecurityQuestionDeactivated,
                            templateSeq: 3);
                    }
                    else if (!string.IsNullOrWhiteSpace(row.AnswerRaw))
                    {
                        int affected = CategoryItemSecurityQuestionsService.UpdateCategoryItemSecurityQuestionFromUi(
                            id: row.Id,
                            itemId: itemId,
                            seq: row.Seq,
                            questionPlain: row.QuestionPlain ?? string.Empty,
                            answerPlain: row.AnswerRaw ?? string.Empty,
                            isActive: row.IsActive);

                        if (affected <= 0)
                            throw new InvalidOperationException("CategoryItemSecurityQuestion update failed.");

                        writes += affected;
                        TryWriteSecurityQuestionLog_BestEffort(
                            itemId: itemId,
                            itemName: itemName,
                            eventCode: row.IsActive ? LogEvent_SecurityQuestionChanged : LogEvent_SecurityQuestionDeactivated,
                            templateSeq: row.IsActive ? 2 : 3);
                    }
                }


                EnsureSecurityQuestionsLoadedForActiveItem(forceReload: true);
                NotifyPanel_RefreshCategoryItemGrid_BestEffort();

                if (selectSecurityQuestionsOnSuccess)
                {
                    ForceSelectTab(TabIndexSecurityQuestions);
                    _lastTabIndex = TabIndexSecurityQuestions;
                }

                SetStatus("");
                return true;
            }
            catch (Exception ex)
            {
                SecurityQuestionsPanel?.ShowPersistenceError("Security Questions save failed. See debug output.");
                SetStatus("Security Questions save failed. See debug output.");
                return false;
            }
        }

        private bool TryPersistNewAccountsRowsAndReload(
            IReadOnlyList<CategoryItemAccountsPanel.AccountRow> rows)
        {
            AccountsDraftRows = (rows ?? Array.Empty<CategoryItemAccountsPanel.AccountRow>()).ToList();
            SetStatus("");

            var activeId = TryGetActiveCategoryItemId();
            if (!activeId.HasValue || activeId.Value <= 0)
            {
                SetStatus("Accounts save failed: missing ItemId context.");
                return false;
            }

            int itemId = activeId.Value;
            string itemName = BasicPanel?.GetItemNameTrim() ?? string.Empty;

            try
            {
                foreach (var row in AccountsDraftRows.Where(r => r != null))
                {
                    if (row.Id <= 0)
                    {
                        long insertedAccountId = tmp_CategoryItemAccountsService.InsertCategoryItemAccountFromUi(
                            itemId: itemId,
                            accountTypeId: row.AccountTypeId,
                            accountTypeFreeform: string.IsNullOrWhiteSpace(row.AccountTypeFreeform) ? null : row.AccountTypeFreeform,
                            accountNumberRaw: row.AccountNumberRaw ?? string.Empty,
                            isActive: row.IsActive);

                        if (insertedAccountId > 0)
                        {
                            TryWriteAccountCreatedLog_BestEffort(
                                itemId: itemId,
                                itemName: itemName,
                                accountTypeDisplay: row.AccountTypeDisplay ?? string.Empty,
                                accountNumberMasked: row.AccountNumberMasked ?? string.Empty);
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(row.AccountNumberRaw))
                    {
                        int affected = tmp_CategoryItemAccountsService.UpdateCategoryItemAccountFromUi(
                            id: row.Id,
                            itemId: itemId,
                            accountTypeId: row.AccountTypeId,
                            accountTypeFreeform: string.IsNullOrWhiteSpace(row.AccountTypeFreeform) ? null : row.AccountTypeFreeform,
                            accountNumberRaw: row.AccountNumberRaw ?? string.Empty,
                            isActive: row.IsActive);

                        if (affected <= 0)
                            throw new InvalidOperationException("CategoryItemAccount update failed.");

                        if (!row.IsActive)
                        {
                            TryWriteAccountDeactivatedLog_BestEffort(
                                itemId: itemId,
                                itemName: itemName,
                                accountTypeDisplay: row.AccountTypeDisplay ?? string.Empty,
                                accountNumberMasked: row.AccountNumberMasked ?? string.Empty);
                        }
                    }
                }

                if (AccountsPanel != null)
                {
                    var reloadedRows = tmp_CategoryItemAccountsService.LoadAccountListRowsByItemId(itemId);
                    AccountsPanel.LoadFromHostRows(reloadedRows);
                }

                NotifyPanel_RefreshCategoryItemGrid_BestEffort();
                SetStatus("");
                return true;
            }
            catch (Exception ex)
            {
                bool isDuplicateAccountNumber =
                    ex is InvalidOperationException &&
                    string.Equals(ex.Message, DuplicateAccountNumberMessage, StringComparison.Ordinal);

                try
                {
                    var reloadedRows = tmp_CategoryItemAccountsService.LoadAccountListRowsByItemId(itemId);
                    if (AccountsPanel != null)
                        AccountsPanel.LoadFromHostRows(reloadedRows);

                    AccountsDraftRows = Array.Empty<CategoryItemAccountsPanel.AccountRow>();
                }
                catch (Exception reloadEx)
                {
                }

                if (isDuplicateAccountNumber)
                {
                    AccountsPanel?.ShowPersistenceError(DuplicateAccountNumberMessage);
                    SetStatus("");
                }
                else
                {
                    SetStatus("Accounts save failed. See debug output.");
                }

                return false;
            }
        }

        private void HandleCancelAndExitRequest()
        {
            SetStatus("");

            // Refresh no matter what (cancel path)
            NotifyPanel_RefreshCategoryItemGrid_BestEffort();

            WipeAllForHostClose();
            Canceled?.Invoke(this, EventArgs.Empty);
        }

        /* ======================= Tab switching behavior ======================= */

        private void ItemTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_handlingTabSelection)
                return;

            if (ItemTabs == null)
                return;

            int newIndex = ItemTabs.SelectedIndex;
            int oldIndex = _lastTabIndex;

            // IMPORTANT: keep AppStatus in sync for inactivity timer
            UpdateIsBasicOpenFromUi_BestEffort();

            if (!_isClosing && oldIndex == TabIndexBasic && newIndex != TabIndexBasic)
            {
                // EXISTING+VIEW bypass: no validate/write, but still refresh grid (best-effort)
                if (IsExistingItemAndBasicPanelIsViewMode())
                {
                    NotifyPanel_RefreshCategoryItemGrid_BestEffort();
                    if (newIndex == TabIndexBankCards)
                    {
                        EnsureBankCardsLoadedForActiveItem(forceReload: false);
                    }
                    else if (newIndex == TabIndexAccounts)
                    {
                        EnsureAccountsLoadedForActiveItem(forceReload: false);
                    }
                    else if (newIndex == TabIndexSecurityQuestions)
                    {
                        EnsureSecurityQuestionsLoadedForActiveItem(forceReload: false);
                    }
                    _lastTabIndex = newIndex;
                    return;
                }

                if (_suppressLeaveBasicOnce)
                {
                    _suppressLeaveBasicOnce = false;
                }
                else
                {
                    int requestedIndex = newIndex;

                    ForceSelectTab(TabIndexBasic);

                    bool allowLeaveBasic = TryValidateAndPersistOnLeaveBasic();
                    if (!allowLeaveBasic)
                    {
                        _lastTabIndex = TabIndexBasic;

                        // Ensure status reflects forced Basic
                        UpdateIsBasicOpenFromUi_BestEffort();
                        return;
                    }

                    // Refresh after successful leave-basic
                    NotifyPanel_RefreshCategoryItemGrid_BestEffort();

                    _lastTabIndex = TabIndexBasic;
                    _suppressLeaveBasicOnce = true;

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_isClosing || ItemTabs == null)
                            return;

                        try
                        {
                            ItemTabs.SelectedIndex = requestedIndex;
                        }
                        catch
                        {
                        }
                        finally
                        {
                            // IMPORTANT: keep AppStatus in sync for programmatic jump
                            UpdateIsBasicOpenFromUi_BestEffort();
                        }
                    }), DispatcherPriority.Background);

                    return;
                }
            }

            if (oldIndex == TabIndexBankCards && newIndex != TabIndexBankCards)
            {
                if (_suppressLeaveBankCardsOnce)
                {
                    _suppressLeaveBankCardsOnce = false;
                }
                else
                {
                    int requestedIndex = newIndex;

                    ForceSelectTab(TabIndexBankCards);

                    bool allowLeaveBankCards = PromptLeaveBankCardsBeforeTabSwitch();
                    if (!allowLeaveBankCards)
                    {
                        _lastTabIndex = TabIndexBankCards;

                        UpdateIsBasicOpenFromUi_BestEffort();
                        return;
                    }

                    bool allowLeave = true;

                    try
                    {
                        _handlingTabSelection = true;
                        if (BankCardsPanel != null)
                            allowLeave = BankCardsPanel.TryAutoCommitAndWipe();
                    }
                    finally
                    {
                        _handlingTabSelection = false;

                        // IMPORTANT: status sync after any forced selection inside bankcards
                        UpdateIsBasicOpenFromUi_BestEffort();
                    }

                    if (!allowLeave)
                    {
                        _lastTabIndex = TabIndexBankCards;
                        return;
                    }

                    _lastTabIndex = TabIndexBankCards;
                    _suppressLeaveBankCardsOnce = true;

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_isClosing || ItemTabs == null)
                            return;

                        try
                        {
                            ItemTabs.SelectedIndex = requestedIndex;
                        }
                        catch
                        {
                        }
                        finally
                        {
                            // IMPORTANT: keep AppStatus in sync for programmatic jump
                            UpdateIsBasicOpenFromUi_BestEffort();
                        }
                    }), DispatcherPriority.Background);

                    return;
                }
            }

            if (oldIndex == TabIndexSecurityQuestions && newIndex != TabIndexSecurityQuestions)
            {
                if (_suppressLeaveSecurityQuestionsOnce)
                {
                    _suppressLeaveSecurityQuestionsOnce = false;
                }
                else
                {
                    int requestedIndex = newIndex;

                    ForceSelectTab(TabIndexSecurityQuestions);

                    bool allowLeaveSecurityQuestions = PromptLeaveSecurityQuestionsBeforeTabSwitch();
                    if (!allowLeaveSecurityQuestions)
                    {
                        _lastTabIndex = TabIndexSecurityQuestions;

                        UpdateIsBasicOpenFromUi_BestEffort();
                        return;
                    }

                    bool allowLeave = true;

                    try
                    {
                        _handlingTabSelection = true;
                        if (SecurityQuestionsPanel != null)
                            allowLeave = SecurityQuestionsPanel.TryAutoCommitAndWipe();
                    }
                    finally
                    {
                        _handlingTabSelection = false;
                        UpdateIsBasicOpenFromUi_BestEffort();
                    }

                    if (!allowLeave)
                    {
                        _lastTabIndex = TabIndexSecurityQuestions;
                        return;
                    }

                    _lastTabIndex = TabIndexSecurityQuestions;
                    _suppressLeaveSecurityQuestionsOnce = true;

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_isClosing || ItemTabs == null)
                            return;

                        try
                        {
                            ItemTabs.SelectedIndex = requestedIndex;
                        }
                        catch
                        {
                        }
                        finally
                        {
                            UpdateIsBasicOpenFromUi_BestEffort();
                        }
                    }), DispatcherPriority.Background);

                    return;
                }
            }

            if (oldIndex != TabIndexBankCards && newIndex == TabIndexBankCards)
            {
                EnsureBankCardsLoadedForActiveItem(forceReload: false);
            }
            else if (oldIndex != TabIndexAccounts && newIndex == TabIndexAccounts)
            {
                EnsureAccountsLoadedForActiveItem(forceReload: false);
            }
            else if (oldIndex != TabIndexSecurityQuestions && newIndex == TabIndexSecurityQuestions)
            {
                EnsureSecurityQuestionsLoadedForActiveItem(forceReload: false);
            }

            _lastTabIndex = newIndex;
        }

        private bool TryValidateAndPersistOnLeaveBasic()
        {
            SetStatus("");

            if (BasicPanel == null)
                return true;

            bool saveAndContinue = PromptSaveBasicBeforeTabSwitch();
            if (!saveAndContinue)
                return false;

            if (!TryCommitBasicFromBasicFlow("Cannot leave Basic tab, fix highlighted errors before saving."))
                return false;

            // Keep Basic mode consistent after commit before switching away.
            BasicPanel.PopulateFromDbForCurrentEntity();

            return true;
        }

        private bool TryResolveBasicHostCloseDecision()
        {
            return TryResolveHostCloseDecision(
                hasPanel: BasicPanel != null,
                panelMissingDebugMessage: "[ITEM-TABS][HOST-CLOSE][BASIC] Decision path: BasicPanel missing -> allowClose=true",
                debugPrefix: "[ITEM-TABS][HOST-CLOSE][BASIC]",
                getDecision: PromptBasicHostCloseDecision,
                saveAndExit: () =>
                {
                    if (!TryCommitBasicFromBasicFlow("Cannot close window, fix highlighted errors before saving."))
                        return false;

                    // Keep Basic mode consistent after commit before host-close cleanup runs.
                    BasicPanel.PopulateFromDbForCurrentEntity();
                    return true;
                },
                exitWithoutSave: () => true);
        }

        private bool TryResolveBankCardsHostCloseDecision()
        {
            return TryResolveHostCloseDecision(
                hasPanel: BankCardsPanel != null,
                panelMissingDebugMessage: "[ITEM-TABS][HOST-CLOSE][BANKCARDS] Panel missing -> allowClose=true",
                debugPrefix: "[ITEM-TABS][HOST-CLOSE][BANKCARDS]",
                getDecision: PromptBankCardsHostCloseDecision,
                saveAndExit: TryCommitBankCardsFromHostClose,
                exitWithoutSave: () => BankCardsPanel?.TryPrepareHostCloseDiscard() ?? true,
                onCancelExit: () =>
                {
                    if (ItemTabs != null && ItemTabs.SelectedIndex != TabIndexBankCards)
                        ForceSelectTab(TabIndexBankCards);
                });
        }

        private bool TryResolveSecurityQuestionsHostCloseDecision()
        {
            return TryResolveHostCloseDecision(
                hasPanel: SecurityQuestionsPanel != null,
                panelMissingDebugMessage: "[ITEM-TABS][HOST-CLOSE][SECURITYQUESTIONS] Panel missing -> allowClose=true",
                debugPrefix: "[ITEM-TABS][HOST-CLOSE][SECURITYQUESTIONS]",
                getDecision: PromptSecurityQuestionsHostCloseDecision,
                saveAndExit: TryCommitSecurityQuestionsFromHostClose,
                exitWithoutSave: () => SecurityQuestionsPanel?.TryPrepareHostCloseDiscard() ?? true,
                onCancelExit: () =>
                {
                    if (ItemTabs != null && ItemTabs.SelectedIndex != TabIndexSecurityQuestions)
                        ForceSelectTab(TabIndexSecurityQuestions);
                });
        }

        private bool TryResolveHostCloseDecision(
            bool hasPanel,
            string panelMissingDebugMessage,
            string debugPrefix,
            Func<HostCloseDecision> getDecision,
            Func<bool> saveAndExit,
            Func<bool> exitWithoutSave,
            Action? onCancelExit = null)
        {
            SetStatus("");

            if (!hasPanel)
            {
                return true;
            }

            return RunHostCloseDecision(
                debugPrefix: debugPrefix,
                getDecision: getDecision,
                saveAndExit: saveAndExit,
                exitWithoutSave: exitWithoutSave,
                onCancelExit: onCancelExit);
        }

        private bool RunHostCloseDecision(
            string debugPrefix,
            Func<HostCloseDecision> getDecision,
            Func<bool> saveAndExit,
            Func<bool> exitWithoutSave,
            Action? onCancelExit = null)
        {
            var decision = getDecision();


            switch (decision)
            {
                case HostCloseDecision.SaveAndExit:
                    if (!saveAndExit())
                    {
                        return false;
                    }

                    return true;

                case HostCloseDecision.ExitWithoutSave:
                    if (!exitWithoutSave())
                    {
                        return false;
                    }

                    return true;

                default:
                    onCancelExit?.Invoke();

                    return false;
            }
        }

        private bool TryCommitBankCardsFromHostClose()
        {
            if (ItemTabs != null && ItemTabs.SelectedIndex != TabIndexBankCards)
                ForceSelectTab(TabIndexBankCards);

            if (BankCardsPanel == null)
                return true;

            if (!BankCardsPanel.TryBuildHostCloseSavePayload(out var rows))
            {
                return false;
            }

            return TryPersistBankCardsRows(rows, selectBankCardsOnSuccess: false);
        }

        private bool TryCommitSecurityQuestionsFromHostClose()
        {
            if (ItemTabs != null && ItemTabs.SelectedIndex != TabIndexSecurityQuestions)
                ForceSelectTab(TabIndexSecurityQuestions);

            if (SecurityQuestionsPanel == null)
                return true;

            if (!SecurityQuestionsPanel.TryBuildHostCloseSavePayload(out var rows))
            {
                return false;
            }

            return TryPersistSecurityQuestionsRows(rows, selectSecurityQuestionsOnSuccess: false);
        }

        private bool TryPersistBankCardsRows(
            IReadOnlyList<CategoryItemBankCardsPanel.BankCardRow> rows,
            bool selectBankCardsOnSuccess)
        {
            BankCardsDraftRows = (rows ?? Array.Empty<CategoryItemBankCardsPanel.BankCardRow>()).ToList();
            SetStatus("");

            if (BasicPanel == null)
                return true;

            bool bypassBasicPersist = IsExistingItemAndBasicPanelIsViewMode();
            bool isBookmarkOnly = false;

            if (!bypassBasicPersist)
            {
                BasicPanel.NormalizeUsernameFromEmailIfEmpty();
                if (!BasicPanel.TryValidateAllForSubmit(out isBookmarkOnly, out bool okName, out bool okPassword, out bool okPin, out bool okEmail, out bool okPhone))
                {
                    if (ItemTabs != null)
                        ForceSelectTab(TabIndexBasic);

                    BasicPanel.FocusFirstError(okName, okPassword, okPin, okEmail, okPhone, isBookmarkOnly);
                    SetStatus("Bank Cards are staged, fix Basic tab errors before saving.");

                    return false;
                }

                if (!TryPersistBasicIfNeeded(PersistTrigger.Save, isBookmarkOnly))
                {
                    if (ItemTabs != null)
                        ForceSelectTab(TabIndexBasic);

                    return false;
                }
            }

            var activeId = TryGetActiveCategoryItemId();
            if (!activeId.HasValue || activeId.Value <= 0)
            {
                SetStatus("Save failed: missing ItemId context.");
                return false;
            }

            int itemId = activeId.Value;
            string itemName = BasicPanel?.GetItemNameTrim() ?? string.Empty;

            try
            {
                var serviceRows = BankCardsDraftRows
                    .Select(r => new CategoryItemService.BankCardRow
                    {
                        Id = r.Id,
                        ItemId = itemId,
                        CardTypeId = r.CardTypeId,
                        CardTypeDisplay = r.CardTypeDisplay ?? string.Empty,
                        Cardholder = r.Cardholder,
                        CardNumberRaw = r.CardNumberRaw ?? string.Empty,
                        ExpirationDisplay = r.Expiration ?? string.Empty,
                        ExpMonth = 0,
                        ExpYear = 0,
                        CvvRaw = r.CvvRaw ?? string.Empty,
                        PinRaw = r.PinRaw ?? string.Empty,
                        BillingZipRaw = null,
                        CardNumberMasked = r.CardNumberMasked ?? string.Empty,
                        CvvMasked = r.CvvMasked ?? string.Empty,
                        PinMasked = r.PinMasked ?? string.Empty,
                        IsPrimary = false,
                        IsActive = r.IsActive
                    })
                    .ToList();

                var saveResult = CategoryItemService.SaveBankCardsByItemIdWithLogResult(itemId, serviceRows);
                int writes = saveResult.Writes;
                if (writes < 0)
                {
                    SetStatus("Bank Cards save failed. See debug output.");
                    return false;
                }

                foreach (var entry in saveResult.LogEntries)
                {
                    TryWriteBankCardLog_BestEffort(
                        itemId: itemId,
                        itemName: itemName,
                        entry: entry);
                }

                EnsureBankCardsLoadedForActiveItem(forceReload: true);
                NotifyPanel_RefreshCategoryItemGrid_BestEffort();

                if (selectBankCardsOnSuccess)
                {
                    ForceSelectTab(TabIndexBankCards);
                    _lastTabIndex = TabIndexBankCards;
                }

                SetStatus("");
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Bank Cards save failed. See debug output.");
                return false;
            }
        }

        private enum TwoStepHostClosePopupDecision
        {
            SaveAndExit,
            ExitWithoutSave,
            CancelExit
        }

        private TwoStepHostClosePopupDecision PromptTwoStepHostClosePopupDecision(
            string step1Title,
            string step1Body,
            string step2Title,
            string step2Body,
            string resultDebugPrefix,
            string step1DebugContext = "POPUP",
            string step2DebugContext = "POPUP")
        {
            var first = ShowCustomPopupDecision(
                title: step1Title,
                body: step1Body,
                primaryText: "Save & Exit",
                secondaryText: "More Options",
                debugContext: step1DebugContext);


            if (first == PopupDialog.PopupResult.Accept)
                return TwoStepHostClosePopupDecision.SaveAndExit;

            var second = ShowCustomPopupDecision(
                title: step2Title,
                body: step2Body,
                primaryText: "Exit Without Saving",
                secondaryText: "Cancel",
                debugContext: step2DebugContext);


            if (second == PopupDialog.PopupResult.Accept)
                return TwoStepHostClosePopupDecision.ExitWithoutSave;

            return TwoStepHostClosePopupDecision.CancelExit;
        }

        private HostCloseDecision PromptBankCardsHostCloseDecision()
        {
            return PromptHostCloseDecision(
                step1Title: "Save Bank Cards Before Exiting?",
                step1Body:
                    "You have Bank Cards work in this session.\n\n" +
                    "Choose Save & Exit to save your Bank Cards changes and close the window.\n" +
                    "Choose More Options to continue without saving or cancel exit.",
                step2Title: "Exit Bank Cards Without Saving?",
                step2Body:
                    "Your Bank Cards session work will be discarded.\n\n" +
                    "Choose Exit Without Saving to close the window now.\n" +
                    "Choose Cancel to remain in the editor.",
                resultDebugPrefix: "[ITEM-TABS][HOST-CLOSE][BANKCARDS]",
                step1DebugContext: "HOST-CLOSE-BANKCARDS-STEP1",
                step2DebugContext: "HOST-CLOSE-BANKCARDS-STEP2");
        }

        private HostCloseDecision PromptSecurityQuestionsHostCloseDecision()
        {
            return PromptHostCloseDecision(
                step1Title: "Save Security Questions Before Exiting?",
                step1Body:
                    "You have Security Questions work in this session.\n\n" +
                    "Choose Save & Exit to save your Security Questions changes and close the window.\n" +
                    "Choose More Options to continue without saving or cancel exit.",
                step2Title: "Exit Security Questions Without Saving?",
                step2Body:
                    "Your Security Questions session work will be discarded.\n\n" +
                    "Choose Exit Without Saving to close the window now.\n" +
                    "Choose Cancel to remain in the editor.",
                resultDebugPrefix: "[ITEM-TABS][HOST-CLOSE][SECURITYQUESTIONS]",
                step1DebugContext: "HOST-CLOSE-SECURITYQUESTIONS-STEP1",
                step2DebugContext: "HOST-CLOSE-SECURITYQUESTIONS-STEP2");
        }

        private HostCloseDecision PromptBasicHostCloseDecision()
        {
            return PromptHostCloseDecision(
                step1Title: "Save Basic Before Exiting?",
                step1Body:
                    "You have uncommitted Basic changes.\n\n" +
                    "Choose Save & Exit to save your Basic changes and close the window.\n" +
                    "Choose More Options to continue without saving or cancel exit.",
                step2Title: "Exit Without Saving?",
                step2Body:
                    "Your Basic changes will be discarded.\n\n" +
                    "Choose Exit Without Saving to close the window now.\n" +
                    "Choose Cancel to remain on Basic.",
                resultDebugPrefix: "[ITEM-TABS][HOST-CLOSE][BASIC]");
        }

        private HostCloseDecision PromptHostCloseDecision(
            string step1Title,
            string step1Body,
            string step2Title,
            string step2Body,
            string resultDebugPrefix,
            string step1DebugContext = "POPUP",
            string step2DebugContext = "POPUP")
        {
            return PromptTwoStepHostClosePopupDecision(
                step1Title: step1Title,
                step1Body: step1Body,
                step2Title: step2Title,
                step2Body: step2Body,
                resultDebugPrefix: resultDebugPrefix,
                step1DebugContext: step1DebugContext,
                step2DebugContext: step2DebugContext)
                switch
            {
                TwoStepHostClosePopupDecision.SaveAndExit => HostCloseDecision.SaveAndExit,
                TwoStepHostClosePopupDecision.ExitWithoutSave => HostCloseDecision.ExitWithoutSave,
                _ => HostCloseDecision.CancelExit
            };
        }

        private PopupDialog.PopupResult ShowCustomPopupDecision(
            string title,
            string body,
            string primaryText,
            string secondaryText,
            string debugContext = "POPUP",
            PopupDialog.PopupResult safeFallbackResult = PopupDialog.PopupResult.Cancel,
            bool showCancel = true)
        {
            try
            {
                var hostPanel = FindPanelHost();
                if (hostPanel == null)
                {
                    return safeFallbackResult;
                }

                var overlayHost = hostPanel.FindName("PopupOverlayHost") as Border;
                var overlayContent = hostPanel.FindName("PopupOverlayContent") as ContentControl;

                if (overlayHost == null || overlayContent == null)
                {
                    return safeFallbackResult;
                }

                var result = safeFallbackResult;

                var popup = new PopupDialog();
                popup.Configure(
                    severity: 0,
                    title: title,
                    message: body,
                    showCancel: showCancel,
                    primaryText: primaryText,
                    secondaryText: secondaryText);

                var frame = new DispatcherFrame();

                popup.Completed += popupResult =>
                {
                    result = popupResult;

                    try { overlayContent.Content = null; } catch { }
                    try { overlayHost.Visibility = Visibility.Collapsed; } catch { }

                    frame.Continue = false;
                };

                overlayContent.Content = popup;
                overlayHost.Visibility = Visibility.Visible;

                popup.Focus();
                Keyboard.Focus(popup);

                Dispatcher.PushFrame(frame);

                return result;
            }
            catch (Exception ex)
            {
                return safeFallbackResult;
            }
        }

        private void ShowInformationalPopup(string title, string body, string debugContext)
        {
            _ = ShowCustomPopupDecision(
                title: title,
                body: body,
                primaryText: "OK",
                secondaryText: "Cancel",
                debugContext: debugContext,
                safeFallbackResult: PopupDialog.PopupResult.Accept,
                showCancel: false);
        }

        private bool PromptSaveBasicBeforeTabSwitch()
        {
            const string title = "Save Basic Before Leaving?";
            const string body =
                "You have uncommitted Basic changes.\n\n" +
                "Choose Yes to save from Basic now, or No to stay on Basic.";

            var result = ShowCustomPopupDecision(
                title: title,
                body: body,
                primaryText: "Yes",
                secondaryText: "No",
                debugContext: "LEAVE-BASIC",
                safeFallbackResult: PopupDialog.PopupResult.Cancel);


            return result == PopupDialog.PopupResult.Accept;
        }
        private bool PromptLeaveBankCardsBeforeTabSwitch()
        {
            const string title = "Leave Bank Cards Tab?";
            const string body =
                "You have Bank Cards work in this editor session.\n\n" +
                "Choose Yes to leave Bank Cards now, or No to stay on this tab.";

            var result = ShowCustomPopupDecision(
                title: title,
                body: body,
                primaryText: "Yes",
                secondaryText: "No",
                debugContext: "LEAVE-BANKCARDS",
                safeFallbackResult: PopupDialog.PopupResult.Cancel);


            return result == PopupDialog.PopupResult.Accept;
        }

        private bool PromptLeaveSecurityQuestionsBeforeTabSwitch()
        {
            const string title = "Leave Security Questions Tab?";
            const string body =
                "You have Security Questions work in this editor session.\n\n" +
                "Choose Yes to leave Security Questions now, or No to stay on this tab.";

            var result = ShowCustomPopupDecision(
                title: title,
                body: body,
                primaryText: "Yes",
                secondaryText: "No",
                debugContext: "LEAVE-SECURITYQUESTIONS",
                safeFallbackResult: PopupDialog.PopupResult.Cancel);


            return result == PopupDialog.PopupResult.Accept;
        }
        /* ======================= Status line ======================= */

        private void SetStatus(string text)
        {
            try
            {
                if (txtStatusLine == null)
                    return;

                txtStatusLine.Text = text ?? string.Empty;
            }
            catch
            {
            }
        }
    }
}

