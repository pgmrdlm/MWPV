// File: View/UserControls/CategoryItemEditorTabs.xaml.cs
//
// FULL REWRITE (Guardrails-first; INSERT for new items, UPDATE for existing items)
// -----------------------------------------------------------------------------
// Ground rules (kept + extended):
// 1) SEDS tells us ONLY whether the item exists (PK present).
//    - If PK present => existing item => open in VIEW by default (panel decides).
// 2) This file stays out of AES crypto. Services do AES.
// 3) Inserts happen only once: new item (no PK) => insert => set SEDS PK.
// 4) Updates happen only on explicit Save actions (NOT on tab-leave).
// 5) Never clear form on Loaded if PK exists.
// 6) Tab switching:
//    - Leaving Basic requires validation.
//    - If NEW item, we insert-on-leave (so downstream tabs can rely on PK).
//    - If EXISTING item, we do NOT write on leave (save is explicit).
// 7) Password history updates for EXISTING items:
//    - Only occur on explicit Save.
//    - Uses CategoryItemService.InsertPasswordHistoryForExistingItem(...)
// 8) Duplicate password warning (GLOBAL):
//    - Service throws DuplicatePasswordWarningException
//    - UI catches, shows OUR PopupDialog (themed) using Panel.xaml overlay host
//    - Accept => retry with allowDuplicate:true
//    - Cancel => stop, no save
//
// PASSWORD-ONLY FIX (THIS SESSION TASK):
// - Existing item SaveAndExit:
//   - Compare BEFORE password fingerprint (from DB, loaded by BasicPanel)
//     vs AFTER password fingerprint (computed from current UI plaintext)
//   - If SAME => SKIP duplicate check + SKIP password history insert (bug fix)
//   - If DIFFERENT => run existing duplicate-password flow as-is (popup logic unchanged)
//
// NON-SENSITIVE COMPARES (THIS SESSION TASK):
// - On EXISTING item SaveAndExit we compute a centralized BasicTab change-set:
//   - Bookmark flag toggled
//   - PIN updated
//   - User name updated
//   - URL/Location updated
//   - Phone number updated
//   - Email updated
//   - Notes updated (Basic description field)
//   - Password updated (derived from fingerprint compare, already centralized)
// - Comparisons use BEFORE baselines pulled from DB row (single call, save-time only).
//   (We keep it centralized in this file; BasicPanel already holds its own baseline too.)
//
// NEW ITEM LOGGING (DONE):
// - Immediately after NEW item insert succeeds, write a single log row:
//   EventCode: CATEGORYITEM_CREATED
//   Source:   CategoryItem
//   SubjectText: item name
//   MessageText: template-expanded message
// - Log write MUST NOT block the insert if it fails (best-effort).
//
// EXISTING ITEM CHANGE LOGGING (NEW TASK):
// - After EXISTING item update succeeds (SaveAndExit only),
//   if BasicTabChanges.Any == true => write a single log row:
//   EventCode: CATEGORYITEM_CHANGED
//   Source:   CategoryItem
//   SubjectText: item name
//   MessageText:
//     Seq 2: header line (template)
//     Seq 3-10: bullet lines for each changed flag (template)
// - Uses LogMessageTemplate rows (UpdateForm='BasicTab').
// - Log write MUST NOT block the save if it fails (best-effort).
// -----------------------------------------------------------------------------


using MWPV.Services;
using MWPV.View.UserControls.CategoryItems;
using MWPV.View.UserControls.Popup;
using Security.Utility.Storage;            // SecureEncryptedDataStore (SEDS)
using Security.Utility.Crypto.Signatures;  // SensitiveValueSignature (HMAC signature)
using Security.Utility.Crypto.Fields;      // FieldAesCrypto (SEDS key constant)
using Security.Utility.Wiping;             // SensitiveDataCleaner (central wiping)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Security.Cryptography;        // CryptographicOperations.FixedTimeEquals
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

        public IReadOnlyList<CategoryItemBankCardsPanel.BankCardRow> BankCardsDraftRows { get; private set; }
            = Array.Empty<CategoryItemBankCardsPanel.BankCardRow>();

        private const int TabIndexBasic = 0;
        private const int TabIndexBankCards = 1;

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

        private const string Template_NewItemCreated =
            "Category Item #CategoryItemName# has been created for Category #CategoryName#";

        private const string TemplateForm_BasicTab = "BasicTab";

        public CategoryItemEditorTabs()
        {
            InitializeComponent();

            Loaded += CategoryItemEditorTabs_Loaded;
            Unloaded += CategoryItemEditorTabs_Unloaded;
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

        /* ======================= AFTER SIGNATURE HELPERS ======================= */

        private static void ClearAfterSignatureSedsKeys_BestEffort()
        {
            try { SecureEncryptedDataStore.Clear(SedsKey_AfterSig_Email); } catch { }
            try { SecureEncryptedDataStore.Clear(SedsKey_AfterSig_Phone); } catch { }
            try { SecureEncryptedDataStore.Clear(SedsKey_AfterSig_Pin); } catch { }
        }

        private static void CaptureAfterSignatureToSeds(string sedsKey, string purpose, string? plain)
        {
            // Normalize to stable empty string
            string value = plain ?? string.Empty;

            // Pull secret key bytes from SEDS (must be COPY)
            byte[] keyBytes = SecureEncryptedDataStore.GetBytes(FieldAesCrypto.SedsKey_UserSecretsKey);

            try
            {
                byte[] sig = SensitiveValueSignature.Compute(value, keyBytes, purpose: purpose);

                try
                {
                    string b64 = Convert.ToBase64String(sig);
                    SecureEncryptedDataStore.SetString(sedsKey, b64);

#if DEBUG
                    bool present = !string.IsNullOrWhiteSpace(value);
                    Debug.WriteLine($"[CI][AFTER-SIG] CAPTURED sedsKey={sedsKey} purpose={purpose} sigLen={sig.Length} present={present}");
#endif
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

#if DEBUG
                Debug.WriteLine("[CI][AFTER-SIG] COMPLETE (Email/Phone/PIN) captured into SEDS.");
#endif
                return true;
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[CI][AFTER-SIG] FAILED: {ex}");
#endif
                // Fail-safe: clear keys to avoid stale comparisons
                ClearAfterSignatureSedsKeys_BestEffort();
                return false;
            }
        }

        /* ======================= PASSWORD COMPARE HELPERS (THIS SESSION) ======================= */

        private static byte[] ComputePasswordFingerprint(string passwordPlain)
        {
            byte[] keyBytes = SecureEncryptedDataStore.GetBytes(FieldAesCrypto.SedsKey_UserSecretsKey);
            try
            {
                // MUST match DB stored sig algorithm (HMAC + purpose)
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

            // Fixed-time compare
            return CryptographicOperations.FixedTimeEquals(a, b);
        }

        /// <summary>
        /// EXISTING ITEMS ONLY:
        /// - If password fingerprint has NOT changed => do NOT run duplicate check and do NOT insert PW history.
        /// - If changed => proceed (duplicate check runs inside service insert).
        /// </summary>
        private bool ShouldInsertPasswordHistoryForExistingItem(bool isBookmarkOnly, string? passwordPlain)
        {
            if (isBookmarkOnly)
                return false;

            if (BasicPanel == null)
                return false;

            if (string.IsNullOrWhiteSpace(passwordPlain))
                return false;

            // BEFORE comes from DB stored sig loaded by BasicPanel on open.
            byte[]? before = null;
            try { before = BasicPanel.GetOriginalPasswordFingerprintCopy(); }
            catch { before = null; }

            // If we have no BEFORE baseline, treat as "changed" so the normal path runs.
            if (before == null || before.Length == 0)
            {
#if DEBUG
                Debug.WriteLine("[ITEM-TABS][PW-SIG] BEFORE missing => treat as CHANGED (will run dup-check + insert).");
#endif
                return true;
            }

            byte[] after = ComputePasswordFingerprint(passwordPlain!);

            try
            {
                bool same = SigEquals(before, after);

#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][PW-SIG] COMPARE beforeLen={before.Length} afterLen={after.Length} SAME={(same ? "YES" : "NO")}");
#endif
                // If SAME => no change => bug fix: skip insert + skip duplicate search
                return !same;
            }
            finally
            {
                SensitiveDataCleaner.Zero(before);
                SensitiveDataCleaner.Zero(after);
            }
        }

        /* ======================= NON-SENSITIVE COMPARES (CENTRALIZED) ======================= */

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
                NotesUpdated;

            public bool PasswordUpdated { get; init; }
            public bool BookmarkToggled { get; init; }
            public bool PinUpdated { get; init; }
            public bool UsernameUpdated { get; init; }
            public bool UrlUpdated { get; init; }
            public bool PhoneUpdated { get; init; }
            public bool EmailUpdated { get; init; }
            public bool NotesUpdated { get; init; }
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
            string? afterNotes,
            string? afterUsername,
            string? afterUrl,
            string? afterPhone,
            string? afterEmail,
            string? afterPin,
            bool passwordChangedByFingerprint)
        {
            // BEFORE values come from the DB row we load save-time only.
            // AFTER values are pulled from the UI and already normalized upstream.
            bool bookmarkSame = BoolEqBookmark(beforeRow.BookMarkOnly, isBookmarkOnly);

            bool pinSame = StrEq(beforeRow.PinPlain, afterPin);
            bool userSame = StrEq(beforeRow.Username, afterUsername);
            bool urlSame = StrEq(beforeRow.SignInUrl, afterUrl);
            bool phoneSame = StrEq(beforeRow.AccountPhonePlain, afterPhone);
            bool emailSame = StrEq(beforeRow.AccountEmailPlain, afterEmail);

            // Notes (Basic tab description)
            bool notesSame = StrEq(beforeRow.Description, afterNotes);

            var changes = new BasicTabChanges
            {
                PasswordUpdated = passwordChangedByFingerprint,
                BookmarkToggled = !bookmarkSame,
                PinUpdated = !pinSame,
                UsernameUpdated = !userSame,
                UrlUpdated = !urlSame,
                PhoneUpdated = !phoneSame,
                EmailUpdated = !emailSame,
                NotesUpdated = !notesSame
            };

#if DEBUG
            Debug.WriteLine(
                "[ITEM-TABS][BASIC-CHANGES] " +
                $"Pw={changes.PasswordUpdated} Bm={changes.BookmarkToggled} Pin={changes.PinUpdated} User={changes.UsernameUpdated} " +
                $"Url={changes.UrlUpdated} Phone={changes.PhoneUpdated} Email={changes.EmailUpdated} Notes={changes.NotesUpdated}");
#endif

            return changes;
        }

        /* ======================= LOGGING HELPERS (TemplateLogWriter) ======================= */

        private static IReadOnlyDictionary<string, string?> BuildCommonTokens(string categoryName, string categoryItemName)
        {
            // Token keys are WITHOUT surrounding '#'
            return new Dictionary<string, string?>
            {
                ["CategoryName"] = categoryName,
                ["CategoryItemName"] = categoryItemName
            };
        }

        private static string BuildCategoryItemCreatedMessage(string categoryName, string categoryItemName)
        {
            // Exact template seed:
            // "Category Item #CategoryItemName# has been created for Category #CategoryName#"
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

#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][LOG][NEW-ITEM] Inserted CATEGORYITEM_CREATED logId={logId} itemId={itemId} subject='{itemName}'");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][LOG][NEW-ITEM] FAILED (best-effort ignored): {ex}");
#endif
                // DO NOT BLOCK new item insert because of logging.
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
                // Templates (BasicTab):
                // 2  The following updates have been saved for #CategoryItemName#
                // 3  - Password updated
                // 4  - Bookmark flag toggled
                // 5  - PIN updated
                // 6  - User name updated
                // 7  - URL/Location updated
                // 8  - Phone number updated
                // 9  - Email updated
                // 10 - Notes updated
                var seqs = new List<int>(capacity: 9) { 2 };

                if (changes.PasswordUpdated) seqs.Add(3);
                if (changes.BookmarkToggled) seqs.Add(4);
                if (changes.PinUpdated) seqs.Add(5);
                if (changes.UsernameUpdated) seqs.Add(6);
                if (changes.UrlUpdated) seqs.Add(7);
                if (changes.PhoneUpdated) seqs.Add(8);
                if (changes.EmailUpdated) seqs.Add(9);
                if (changes.NotesUpdated) seqs.Add(10);

                // Safety: if no bullet seqs, do nothing
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

                var tokens = BuildCommonTokens(categoryName, itemName);

                var logId = TemplateLogWriter.InsertFromTemplates_BestEffort(
                    updateForm: TemplateForm_BasicTab,
                    seqsInOrder: seqs,
                    tokens: tokens,
                    write: write);

#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][LOG][CHANGED] Inserted CATEGORYITEM_CHANGED logId={logId} itemId={itemId} subject='{itemName}' seqCount={seqs.Count}");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][LOG][CHANGED] FAILED (best-effort ignored): {ex}");
#endif
                // DO NOT BLOCK save because of logging.
            }
        }

        /* ======================= Public Open API ======================= */

        public void ConfigureForOpen(int categoryKey, string categoryName)
        {
            _categoryKey = categoryKey;
            _categoryName = categoryName;

            _hasPersistedId = HasPersistedIdFromSeds();

#if DEBUG
            var id = TryGetActiveCategoryItemId();
            Debug.WriteLine($"[ITEM-TABS] ConfigureForOpen: catKey={categoryKey}, name='{categoryName}', exists={_hasPersistedId}, activeId={(id.HasValue ? id.Value : 0)}");
#endif

            InitializeUiForOpen();
        }

        public void ConfigureForAdd(int categoryKey, string categoryName)
        {
            _categoryKey = categoryKey;
            _categoryName = categoryName;

            ClearSedsContext();
            _hasPersistedId = false;

#if DEBUG
            Debug.WriteLine($"[ITEM-TABS] ConfigureForAdd: catKey={categoryKey}, name='{categoryName}', exists=false (SEDS cleared)");
#endif

            InitializeUiForOpen();
        }

        public void ConfigureForEdit(int categoryKey, string categoryName, object existingItem)
        {
            _categoryKey = categoryKey;
            _categoryName = categoryName;

            _hasPersistedId = HasPersistedIdFromSeds();

#if DEBUG
            var id = TryGetActiveCategoryItemId();
            Debug.WriteLine($"[ITEM-TABS] ConfigureForEdit: catKey={categoryKey}, name='{categoryName}', exists={_hasPersistedId}, activeId={(id.HasValue ? id.Value : 0)} (SEDS-derived existence)");
#endif

            InitializeUiForOpen();
        }

        private void InitializeUiForOpen()
        {
            HookPanelsOnce();

            if (ItemTabs != null)
            {
                ItemTabs.SelectionChanged -= ItemTabs_SelectionChanged;
                ItemTabs.SelectionChanged += ItemTabs_SelectionChanged;

                if (ItemTabs.SelectedIndex < 0)
                    ItemTabs.SelectedIndex = TabIndexBasic;

                _lastTabIndex = ItemTabs.SelectedIndex;
            }

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
#if DEBUG
            Debug.WriteLine("[ITEM-TABS] Loaded");
#endif
            HookPanelsOnce();

            if (ItemTabs != null)
            {
                ItemTabs.SelectionChanged -= ItemTabs_SelectionChanged;
                ItemTabs.SelectionChanged += ItemTabs_SelectionChanged;
                _lastTabIndex = ItemTabs.SelectedIndex;
            }

            _hasPersistedId = HasPersistedIdFromSeds();
            InitializeUiForOpen();
            SetStatus("");
        }

        private void CategoryItemEditorTabs_Unloaded(object? sender, RoutedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-TABS] Unloaded");
#endif
            UnhookPanels();

            if (ItemTabs != null)
                ItemTabs.SelectionChanged -= ItemTabs_SelectionChanged;

            try
            {
                BasicPanel?.WipeAllForHostClose();
                BankCardsPanel?.WipeAllForHostClose();
            }
            catch { }

            try { ClearSedsContext(); } catch { }

            SetStatus("");
        }

        /* ======================= Host API ======================= */

        public void WipeAllForHostClose()
        {
            if (_isClosing)
                return;

            _isClosing = true;

#if DEBUG
            Debug.WriteLine("[ITEM-TABS] WipeAllForHostClose ENTER");
#endif
            try
            {
                TryPreparePanelsForHostClose();
                SetStatus("");
            }
            finally
            {
                try { ClearSedsContext(); } catch { }
#if DEBUG
                Debug.WriteLine("[ITEM-TABS] WipeAllForHostClose EXIT");
#endif
            }
        }

        private void TryPreparePanelsForHostClose()
        {
            try { BasicPanel?.WipeAllForHostClose(); }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS] BasicPanel.WipeAllForHostClose failed: {ex}");
#endif
            }

            try { BankCardsPanel?.WipeAllForHostClose(); }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS] BankCardsPanel.WipeAllForHostClose failed: {ex}");
#endif
            }
        }

        /* ======================= Persistence ======================= */

        private enum PersistTrigger
        {
            LeaveBasicTab,
            SaveAndExit
        }

        private bool TryPersistBasicIfNeeded(PersistTrigger trigger, bool isBookmarkOnly)
        {
            if (_isClosing)
                return true;

            if (BasicPanel == null)
                return true;

            var activeId = TryGetActiveCategoryItemId();
            bool isExisting = _hasPersistedId && activeId.HasValue && activeId.Value > 0;

#if DEBUG
            Debug.WriteLine($"[ITEM-TABS][PERSIST] ENTER trigger={trigger} bookmarkOnly={isBookmarkOnly} hasPersisted={_hasPersistedId} activeId={(activeId.HasValue ? activeId.Value : 0)} isExisting={isExisting}");
#endif

            // =========================
            // EXISTING ITEM
            // =========================
            if (isExisting)
            {
                // Guardrail: NO update on tab leave.
                if (trigger == PersistTrigger.LeaveBasicTab)
                {
#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][PERSIST] Existing item (id={activeId!.Value}) leave-tab: no write. bookmarkOnly={isBookmarkOnly}");
#endif
                    return true;
                }

                // SaveAndExit: UPDATE basic fields, then (optionally) insert password history, then change-log.
                try
                {
                    long itemId = activeId!.Value;

#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][UPDATE] Existing SaveAndExit BEGIN itemId={itemId} bookmarkOnly={isBookmarkOnly}");
#endif

                    // Pull BEFORE baselines (single call, centralized, save-time only).
                    CategoryItemService.CategoryItemBasicRow? beforeRow = null;
                    try { beforeRow = CategoryItemService.LoadCategoryItemBasicById(itemId); }
                    catch { beforeRow = null; }

                    if (beforeRow == null)
                    {
#if DEBUG
                        Debug.WriteLine($"[ITEM-TABS][UPDATE] BEFORE row load failed itemId={itemId} (non-sensitive compares + logging will be skipped)");
#endif
                    }

                    string name = BasicPanel.GetItemNameTrim();
                    string? desc = BasicPanel.GetDescriptionTrimOrNull();
                    string? username = BasicPanel.GetUsernameTrimOrNull();
                    string? url = BasicPanel.GetUrlTrimOrNull();

                    string? emailPlain = BasicPanel.GetEmailTrimOrNull();
                    string? phonePlain = BasicPanel.GetPhoneTrimOrNull();
                    string? pinPlain = BasicPanel.GetPinTrimOrNull();

                    int? isActive = 1;
                    int? bookMarkOnly = isBookmarkOnly ? 1 : 0;

                    int rows = CategoryItemService.UpdateCategoryItemBasic(
                        itemId: itemId,
                        name: name,
                        description: desc,
                        username: username,
                        signInUrl: url,
                        bookMarkOnly: bookMarkOnly,
                        accountEmailPlain: emailPlain,
                        accountPhonePlain: phonePlain,
                        pinPlain: pinPlain,
                        isActive: isActive);

                    if (rows <= 0)
                    {
                        SetStatus("Update failed (0 rows affected).");
#if DEBUG
                        Debug.WriteLine($"[ITEM-TABS][UPDATE] FAILED itemId={itemId} rowsAffected=0 bookmarkOnly={isBookmarkOnly}");
#endif
                        return false;
                    }

#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][UPDATE] OK itemId={itemId} rowsAffected={rows} bookmarkOnly={isBookmarkOnly}");
#endif

                    // Ensure SEDS stays correct.
                    SetSedsContextForCategoryItem(activeId.Value);
                    _hasPersistedId = true;

                    // AFTER signatures: SAVE-TIME ONLY (existing item)
                    if (!TryCaptureAfterSignaturesFromUi(emailPlain, phonePlain, pinPlain))
                    {
                        SetStatus("Saved, but after-signature capture failed (see debug output).");
#if DEBUG
                        Debug.WriteLine($"[ITEM-TABS][AFTER-SIG] FAILED itemId={itemId} (existing) - aborting save exit.");
#endif
                        return false;
                    }

#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][AFTER-SIG] OK itemId={itemId} (existing) after-signatures captured.");
#endif

                    // ==========================================================
                    // Password History Insert (EXISTING ITEM) - ONLY ON SAVE
                    // BUG FIX: only if password fingerprint CHANGED
                    // ==========================================================
                    bool passwordChangedByFingerprint = false;

                    if (isBookmarkOnly)
                    {
#if DEBUG
                        Debug.WriteLine($"[ITEM-TABS][PW-HIST] SKIP (bookmark-only) itemId={itemId}");
#endif
                    }
                    else
                    {
                        string? pw = BasicPanel.GetPasswordPlainOrNull();

#if DEBUG
                        Debug.WriteLine($"[ITEM-TABS][PW-HIST] Existing SaveAndExit passwordCheck itemId={itemId} pwIsNull={(pw == null)} pwIsWhite={(string.IsNullOrWhiteSpace(pw))}");
#endif

                        // THE FIX:
                        // - If password fingerprint is unchanged => do NOT run duplicate check, do NOT insert history.
                        bool shouldInsert = ShouldInsertPasswordHistoryForExistingItem(isBookmarkOnly: false, passwordPlain: pw);
                        passwordChangedByFingerprint = shouldInsert;

                        if (!shouldInsert)
                        {
#if DEBUG
                            Debug.WriteLine($"[ITEM-TABS][PW-HIST] SKIP (unchanged fingerprint) itemId={itemId}");
#endif
                        }
                        else
                        {
                            long pwHistId;

                            try
                            {
                                // Duplicate check happens inside service insert.
                                pwHistId = CategoryItemService.InsertPasswordHistoryForExistingItem(
                                    itemId: itemId,
                                    passwordPlain: pw!,
                                    isBookmarkOnly: false,
                                    allowDuplicate: false);
                            }
                            catch (CategoryItemService.DuplicatePasswordWarningException)
                            {
#if DEBUG
                                Debug.WriteLine($"[ITEM-TABS][DUP-PW] Duplicate warning (existing itemId={itemId}). Prompting user.");
#endif

                                if (!PromptDuplicatePasswordAccept())
                                {
#if DEBUG
                                    Debug.WriteLine("[ITEM-TABS][DUP-PW] User canceled duplicate warning (existing). Aborting save.");
#endif
                                    SetStatus("Save canceled (duplicate password warning).");
                                    return false;
                                }

#if DEBUG
                                Debug.WriteLine("[ITEM-TABS][DUP-PW] User accepted duplicate warning (existing). Retrying allowDuplicate=true.");
#endif

                                pwHistId = CategoryItemService.InsertPasswordHistoryForExistingItem(
                                    itemId: itemId,
                                    passwordPlain: pw!,
                                    isBookmarkOnly: false,
                                    allowDuplicate: true);
                            }

                            if (pwHistId <= 0)
                            {
                                SetStatus("Saved Basic fields, but password history insert failed.");
#if DEBUG
                                Debug.WriteLine($"[ITEM-TABS][PW-HIST][INSERT] FAILED itemId={itemId} pwHistId={pwHistId}");
#endif
                                return false;
                            }

#if DEBUG
                            Debug.WriteLine($"[ITEM-TABS][PW-HIST][INSERT] OK itemId={itemId} pwHistId={pwHistId}");
#endif
                        }
                    }

                    // ==========================================================
                    // NON-SENSITIVE COMPARES (save-time, centralized)
                    // + EXISTING ITEM CHANGE LOG (NEW TASK)
                    // ==========================================================
                    if (beforeRow != null)
                    {
                        var changes = ComputeBasicTabChanges_ExistingItem(
                            beforeRow: beforeRow,
                            isBookmarkOnly: isBookmarkOnly,
                            afterNotes: desc,
                            afterUsername: username,
                            afterUrl: url,
                            afterPhone: phonePlain,
                            afterEmail: emailPlain,
                            afterPin: pinPlain,
                            passwordChangedByFingerprint: passwordChangedByFingerprint);

#if DEBUG
                        if (!changes.Any)
                        {
                            Debug.WriteLine("[ITEM-TABS][BASIC-CHANGES] No non-sensitive changes detected (and password unchanged).");
                        }
#endif

                        if (changes.Any)
                        {
                            TryWriteExistingItemChangedLog_BestEffort(
                                itemId: itemId,
                                categoryName: _categoryName,
                                itemName: name,
                                changes: changes);
                        }
                    }
                    else
                    {
#if DEBUG
                        Debug.WriteLine("[ITEM-TABS][BASIC-CHANGES] Skipped (beforeRow missing).");
#endif
                    }

                    SetStatus("");
                    return true;
                }
                catch (Exception ex)
                {
#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][SAVE][EXISTING] FAILED: {ex}");
#endif
                    SetStatus("Save failed. See debug output.");
                    return false;
                }
            }

            // =========================
            // NEW ITEM
            // =========================
            if (_hasPersistedId && !activeId.HasValue)
            {
#if DEBUG
                Debug.WriteLine("[ITEM-TABS][PERSIST] _hasPersistedId=true but SEDS has no valid ItemId. Treating as NEW.");
#endif
                _hasPersistedId = false;
            }

            try
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][INSERT] New item persist BEGIN trigger={trigger} bookmarkOnly={isBookmarkOnly} catKey={_categoryKey} catName='{_categoryName}'");
#endif

                string name = BasicPanel.GetItemNameTrim();
                string? desc = BasicPanel.GetDescriptionTrimOrNull();
                string? username = BasicPanel.GetUsernameTrimOrNull();
                string? url = BasicPanel.GetUrlTrimOrNull();

                string? emailPlain = BasicPanel.GetEmailTrimOrNull();
                string? phonePlain = BasicPanel.GetPhoneTrimOrNull();
                string? pinPlain = BasicPanel.GetPinTrimOrNull();

                int? isActive = 1;

                long newId;

                if (isBookmarkOnly)
                {
#if DEBUG
                    Debug.WriteLine("[ITEM-TABS][INSERT] PATH=BookmarkOnly => InsertCategoryItemOnly()");
#endif
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

#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][INSERT] PATH=WithPassword => InsertCategoryItemWithPasswordHistory() pwIsNull={(passwordPlain == null)} pwIsWhite={(string.IsNullOrWhiteSpace(passwordPlain))}");
#endif

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
#if DEBUG
                        Debug.WriteLine("[ITEM-TABS][DUP-PW] Duplicate warning (new item). Prompting user.");
#endif

                        if (!PromptDuplicatePasswordAccept())
                        {
#if DEBUG
                            Debug.WriteLine("[ITEM-TABS][DUP-PW] User canceled duplicate warning (new). Aborting insert.");
#endif
                            SetStatus("Save canceled (duplicate password warning).");
                            return false;
                        }

#if DEBUG
                        Debug.WriteLine("[ITEM-TABS][DUP-PW] User accepted duplicate warning (new). Retrying allowDuplicate=true.");
#endif

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
#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][INSERT] FAILED newId={newId} bookmarkOnly={isBookmarkOnly}");
#endif
                    return false;
                }

                if (newId > int.MaxValue)
                {
                    SetStatus("Insert failed (ItemId overflow).");
#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][INSERT] FAILED New ItemId={newId} exceeds Int32.MaxValue; cannot store in SEDS Int32.");
#endif
                    return false;
                }

                SetSedsContextForCategoryItem((int)newId);
                _hasPersistedId = true;

#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][INSERT] OK newId={newId} bookmarkOnly={isBookmarkOnly} => SEDS set (kind={EntityKind_CategoryItem})");
#endif

                // ==========================================================
                // NEW ITEM LOG ROW (DONE) - ALWAYS ON INSERT SUCCESS
                // ==========================================================
                TryWriteNewItemCreatedLog_BestEffort(
                    itemId: newId,
                    categoryName: _categoryName,
                    itemName: name);

                // AFTER signatures: SAVE-TIME ONLY (new item insert)
                if (trigger == PersistTrigger.SaveAndExit)
                {
                    if (!TryCaptureAfterSignaturesFromUi(emailPlain, phonePlain, pinPlain))
                    {
                        SetStatus("Inserted, but after-signature capture failed (see debug output).");
#if DEBUG
                        Debug.WriteLine($"[ITEM-TABS][AFTER-SIG] FAILED newId={newId} (new) - aborting save exit.");
#endif
                        return false;
                    }

#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][AFTER-SIG] OK newId={newId} (new) after-signatures captured.");
#endif
                }
                else
                {
#if DEBUG
                    Debug.WriteLine("[ITEM-TABS][AFTER-SIG] SKIP (trigger=LeaveBasicTab) - after signatures are save-time only.");
#endif
                }

                if (trigger == PersistTrigger.LeaveBasicTab)
                    SetStatus("");

                return true;
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][INSERT][NEW] FAILED: {ex}");
#endif
                SetStatus("Insert failed. See debug output.");
                return false;
            }
        }

        /* ======================= Duplicate Password Popup (OUR themed PopupDialog) ======================= */

        private bool PromptDuplicatePasswordAccept()
        {
            const string title = "Duplicate Password Warning";
            const string body =
                "This password is currently used or has been previously used.\n\n" +
                "Accept to continue.";

            try
            {
                var hostPanel = FindPanelHost();
                if (hostPanel == null)
                {
#if DEBUG
                    Debug.WriteLine("[ITEM-TABS][POPUP] Host Panel not found. Falling back to MessageBox.");
#endif
                    var r = MessageBox.Show(body, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                    return r == MessageBoxResult.OK;
                }

                var overlayHost = hostPanel.FindName("PopupOverlayHost") as Border;
                var overlayContent = hostPanel.FindName("PopupOverlayContent") as ContentControl;

                if (overlayHost == null || overlayContent == null)
                {
#if DEBUG
                    Debug.WriteLine("[ITEM-TABS][POPUP] PopupOverlayHost/PopupOverlayContent not found. Falling back to MessageBox.");
#endif
                    var r = MessageBox.Show(body, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                    return r == MessageBoxResult.OK;
                }

                bool accepted = false;

                var popup = new PopupDialog();
                popup.ConfigureWarningAcceptCancel(title, body);

                var frame = new DispatcherFrame();

                popup.Completed += result =>
                {
                    accepted = (result == PopupDialog.PopupResult.Accept);

                    try { overlayContent.Content = null; } catch { }
                    try { overlayHost.Visibility = Visibility.Collapsed; } catch { }

                    frame.Continue = false;
                };

                overlayContent.Content = popup;
                overlayHost.Visibility = Visibility.Visible;

                popup.Focus();
                Keyboard.Focus(popup);

                Dispatcher.PushFrame(frame);

                return accepted;
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][POPUP] Failed to show PopupDialog: {ex}");
#endif
                var r = MessageBox.Show(body, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                return r == MessageBoxResult.OK;
            }
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

            _panelsHooked = false;
        }

        private void BasicPanel_PasswordValidationFailed(object? sender, string message)
        {
            PasswordValidationFailed?.Invoke(this, message);
        }

        private void BasicPanel_SaveRequested(object? sender, EventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-TABS] BasicPanel SaveRequested");
#endif
            SetStatus("");

            if (BasicPanel == null)
                return;

            BasicPanel.NormalizeUsernameFromEmailIfEmpty();

            if (!BasicPanel.TryValidateAllForSubmit(out bool isBookmarkOnly, out bool okName, out bool okPassword, out bool okPin, out bool okEmail, out bool okPhone))
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][SAVE] Validation FAILED bookmarkOnly={isBookmarkOnly} okName={okName} okPw={okPassword} okPin={okPin} okEmail={okEmail} okPhone={okPhone}");
#endif
                if (ItemTabs != null && ItemTabs.SelectedIndex != TabIndexBasic)
                    ItemTabs.SelectedIndex = TabIndexBasic;

                BasicPanel.FocusFirstError(okName, okPassword, okPin, okEmail, okPhone, isBookmarkOnly);
                SetStatus("Fix the highlighted errors before saving.");
                return;
            }

#if DEBUG
            Debug.WriteLine($"[ITEM-TABS][SAVE] Validation OK bookmarkOnly={isBookmarkOnly}");
#endif

            if (!TryPersistBasicIfNeeded(PersistTrigger.SaveAndExit, isBookmarkOnly))
            {
                if (ItemTabs != null && ItemTabs.SelectedIndex != TabIndexBasic)
                    ItemTabs.SelectedIndex = TabIndexBasic;

                return;
            }

            WipeAllForHostClose();
            Submitted?.Invoke(this, EventArgs.Empty);
        }

        private void BasicPanel_CancelRequested(object? sender, EventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-TABS] BasicPanel CancelRequested");
#endif
            SetStatus("");

            WipeAllForHostClose();
            Canceled?.Invoke(this, EventArgs.Empty);
        }

        /* ======================= Bank Cards integration ======================= */

        private void BankCardsPanel_SaveAndExitRequested(object? sender, CategoryItemBankCardsPanel.BankCardsCommitEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-TABS] BankCardsPanel SaveAndExitRequested");
#endif
            BankCardsDraftRows = (e.Rows ?? Array.Empty<CategoryItemBankCardsPanel.BankCardRow>()).ToList();
            SetStatus("");

            if (BasicPanel == null)
                return;

            BasicPanel.NormalizeUsernameFromEmailIfEmpty();

            if (!BasicPanel.TryValidateAllForSubmit(out bool isBookmarkOnly, out bool okName, out bool okPassword, out bool okPin, out bool okEmail, out bool okPhone))
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][BANK-SAVE] Validation FAILED bookmarkOnly={isBookmarkOnly} okName={okName} okPw={okPassword} okPin={okPin} okEmail={okEmail} okPhone={okPhone}");
#endif
                if (ItemTabs != null)
                    ItemTabs.SelectedIndex = TabIndexBasic;

                BasicPanel.FocusFirstError(okName, okPassword, okPin, okEmail, okPhone, isBookmarkOnly);
                SetStatus("Bank Cards are staged, fix Basic tab errors before saving.");
                return;
            }

#if DEBUG
            Debug.WriteLine($"[ITEM-TABS][BANK-SAVE] Validation OK bookmarkOnly={isBookmarkOnly} rowsStaged={BankCardsDraftRows.Count}");
#endif

            if (!TryPersistBasicIfNeeded(PersistTrigger.SaveAndExit, isBookmarkOnly))
            {
                if (ItemTabs != null)
                    ItemTabs.SelectedIndex = TabIndexBasic;

                return;
            }

            WipeAllForHostClose();
            Submitted?.Invoke(this, EventArgs.Empty);
        }

        private void BankCardsPanel_CancelAndExitRequested(object? sender, EventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-TABS] BankCardsPanel CancelAndExitRequested");
#endif
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

            // Leaving Basic -> must validate, and if NEW, persist insert-on-leave
            if (!_isClosing && oldIndex == TabIndexBasic && newIndex != TabIndexBasic)
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][TAB] Leaving BASIC => newIndex={newIndex}");
#endif
                bool allowLeaveBasic = TryValidateAndPersistOnLeaveBasic();
                if (!allowLeaveBasic)
                {
#if DEBUG
                    Debug.WriteLine("[ITEM-TABS][TAB] Leave BASIC BLOCKED -> force back to Basic.");
#endif
                    _handlingTabSelection = true;
                    try { ItemTabs.SelectedIndex = TabIndexBasic; }
                    finally { _handlingTabSelection = false; }

                    _lastTabIndex = TabIndexBasic;
                    return;
                }
            }

            // Leaving BankCards -> let panel auto-commit/wipe draft row state
            if (oldIndex == TabIndexBankCards && newIndex != TabIndexBankCards)
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][TAB] Leaving BANKCARDS => newIndex={newIndex}");
#endif
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
                }

                if (!allowLeave)
                {
#if DEBUG
                    Debug.WriteLine("[ITEM-TABS][TAB] Leave BANKCARDS BLOCKED -> force back to BankCards.");
#endif
                    _handlingTabSelection = true;
                    try { ItemTabs.SelectedIndex = TabIndexBankCards; }
                    finally { _handlingTabSelection = false; }

                    _lastTabIndex = TabIndexBankCards;
                    return;
                }
            }

            _lastTabIndex = newIndex;
        }

        private bool TryValidateAndPersistOnLeaveBasic()
        {
            SetStatus("");

            if (BasicPanel == null)
                return true;

            BasicPanel.NormalizeUsernameFromEmailIfEmpty();

            if (!BasicPanel.TryValidateAllForSubmit(out bool isBookmarkOnly, out bool okName, out bool okPassword, out bool okPin, out bool okEmail, out bool okPhone))
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][LEAVE-BASIC] Validation FAILED bookmarkOnly={isBookmarkOnly} okName={okName} okPw={okPassword} okPin={okPin} okEmail={okEmail} okPhone={okPhone}");
#endif
                BasicPanel.FocusFirstError(okName, okPassword, okPin, okEmail, okPhone, isBookmarkOnly);
                SetStatus("Cannot leave Basic tab, fix highlighted errors first.");
                return false;
            }

#if DEBUG
            Debug.WriteLine($"[ITEM-TABS][LEAVE-BASIC] Validation OK bookmarkOnly={isBookmarkOnly} -> persist if needed");
#endif

            // Leaving Basic should INSERT only for NEW items. Existing items do not write here.
            if (!TryPersistBasicIfNeeded(PersistTrigger.LeaveBasicTab, isBookmarkOnly))
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][LEAVE-BASIC] Persist FAILED bookmarkOnly={isBookmarkOnly}");
#endif
                return false;
            }

#if DEBUG
            Debug.WriteLine($"[ITEM-TABS][LEAVE-BASIC] Persist OK bookmarkOnly={isBookmarkOnly}");
#endif
            return true;
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
                // swallow (status line must never crash the editor)
            }
        }
    }
}
