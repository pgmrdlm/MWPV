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
using System.Diagnostics;
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
        private bool _bankCardsLoaded;
        private int _bankCardsLoadedItemId;

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

        /* ======================= AppStatus bridge (inactivity timer) ======================= */

        private void UpdateIsBasicOpenFromUi_BestEffort()
        {
            try
            {
                int idx = ItemTabs?.SelectedIndex ?? -1;
                bool isBasic = idx == TabIndexBasic;
                AppStatus.IsBasicOpen = isBasic;

#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][APPSTATUS] IsBasicOpen={isBasic} (SelectedIndex={idx})");
#endif
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

#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][APPSTATUS] IsBasicOpen={isBasic} (explicit)");
#endif
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
#if DEBUG
                Debug.WriteLine("[ITEM-TABS][APPSTATUS] IsBasicOpen=false (cleared)");
#endif
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
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][BANKCARDS][LOAD] BEGIN itemId={itemId} forceReload={forceReload}");
#endif
                var rows = CategoryItemService.LoadBankCardsByItemId(itemId);
                BankCardsPanel.LoadFromHostRows(rows);
                _bankCardsLoaded = true;
                _bankCardsLoadedItemId = itemId;
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][BANKCARDS][LOAD] OK itemId={itemId} rows={rows.Count}");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][BANKCARDS][LOAD] FAILED itemId={itemId}: {ex}");
#endif
                _bankCardsLoaded = false;
                _bankCardsLoadedItemId = 0;
                SetStatus("Unable to load Bank Cards.");
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
#if DEBUG
                        Debug.WriteLine($"[ITEM-TABS][VIEW-DETECT] {name}={view} => {(view ? "VIEW" : "NOT-VIEW")}");
#endif
                        return view;
                    }
                }

                foreach (var name in new[] { "IsEditMode", "IsInEditMode", "EditUnlocked" })
                {
                    if (TryReadBool(name, out bool edit))
                    {
                        bool view = !edit;
#if DEBUG
                        Debug.WriteLine($"[ITEM-TABS][VIEW-DETECT] {name}={edit} => view={!edit}");
#endif
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
#if DEBUG
                            Debug.WriteLine($"[ITEM-TABS][VIEW-DETECT] {name}='{s}' => VIEW");
#endif
                            return true;
                        }

#if DEBUG
                        Debug.WriteLine($"[ITEM-TABS][VIEW-DETECT] {name}='{s}' => NOT-VIEW");
#endif
                    }
                }

#if DEBUG
                Debug.WriteLine("[ITEM-TABS][VIEW-DETECT] No matching view/edit members found => NOT bypassing.");
#endif
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
#if DEBUG
                        Debug.WriteLine($"[ITEM-TABS][BASIC-VIEW] Called {methodName}()");
#endif
                        break;
                    }

                    // SetReadOnly(bool) / LockEditing(bool) style
                    var m1 = t.GetMethod(methodName, flags, binder: null, types: new[] { typeof(bool) }, modifiers: null);
                    if (m1 != null)
                    {
                        m1.Invoke(BasicPanel, new object[] { true });
                        called = true;
#if DEBUG
                        Debug.WriteLine($"[ITEM-TABS][BASIC-VIEW] Called {methodName}(true)");
#endif
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
#if DEBUG
                        Debug.WriteLine($"[ITEM-TABS][BASIC-VIEW] Set {memberName}={value} (property)");
#endif
                        return;
                    }

                    var f = t.GetField(memberName, flags);
                    if (f != null && f.FieldType == typeof(bool))
                    {
                        f.SetValue(BasicPanel, value);
                        called = true;
#if DEBUG
                        Debug.WriteLine($"[ITEM-TABS][BASIC-VIEW] Set {memberName}={value} (field)");
#endif
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
#if DEBUG
                            Debug.WriteLine($"[ITEM-TABS][BASIC-VIEW] Set {memberName}='View' (string)");
#endif
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
#if DEBUG
                                Debug.WriteLine($"[ITEM-TABS][BASIC-VIEW] Set {memberName}={viewEnum} (enum)");
#endif
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
#if DEBUG
                            Debug.WriteLine($"[ITEM-TABS][BASIC-VIEW] Set {memberName}='View' (string field)");
#endif
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
#if DEBUG
                                Debug.WriteLine($"[ITEM-TABS][BASIC-VIEW] Set {memberName}={viewEnum} (enum field)");
#endif
                            }
                        }
                    }
                }

                TrySetModeLike("Mode");
                TrySetModeLike("PanelMode");
                TrySetModeLike("EditorMode");

#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][BASIC-VIEW] Completed best-effort, anyAction={called}");
#endif
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
            bool bookmarkSame = BoolEqBookmark(beforeRow.BookMarkOnly, isBookmarkOnly);

            bool pinSame = StrEq(beforeRow.PinPlain, afterPin);
            bool userSame = StrEq(beforeRow.Username, afterUsername);
            bool urlSame = StrEq(beforeRow.SignInUrl, afterUrl);
            bool phoneSame = StrEq(beforeRow.AccountPhonePlain, afterPhone);
            bool emailSame = StrEq(beforeRow.AccountEmailPlain, afterEmail);
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

        /* ======================= LOGGING HELPERS ======================= */

        private static IReadOnlyDictionary<string, string?> BuildCommonTokens(string categoryName, string categoryItemName)
        {
            return new Dictionary<string, string?>
            {
                ["CategoryName"] = categoryName,
                ["CategoryItemName"] = categoryItemName
            };
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

#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][LOG][NEW-ITEM] Inserted CATEGORYITEM_CREATED logId={logId} itemId={itemId} subject='{itemName}'");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][LOG][NEW-ITEM] FAILED (best-effort ignored): {ex}");
#endif
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
                var seqs = new List<int>(capacity: 9) { 2 };

                if (changes.PasswordUpdated) seqs.Add(3);
                if (changes.BookmarkToggled) seqs.Add(4);
                if (changes.PinUpdated) seqs.Add(5);
                if (changes.UsernameUpdated) seqs.Add(6);
                if (changes.UrlUpdated) seqs.Add(7);
                if (changes.PhoneUpdated) seqs.Add(8);
                if (changes.EmailUpdated) seqs.Add(9);
                if (changes.NotesUpdated) seqs.Add(10);

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
            BankCardsDraftRows = Array.Empty<CategoryItemBankCardsPanel.BankCardRow>();
            _bankCardsLoaded = false;
            _bankCardsLoadedItemId = 0;

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

            // IMPORTANT: sync status for inactivity timer
            UpdateIsBasicOpenFromUi_BestEffort();

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
            BankCardsDraftRows = Array.Empty<CategoryItemBankCardsPanel.BankCardRow>();
            _bankCardsLoaded = false;
            _bankCardsLoadedItemId = 0;

            // IMPORTANT: leaving editor means Basic is not open
            ClearIsBasicOpen_BestEffort();

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
                BankCardsDraftRows = Array.Empty<CategoryItemBankCardsPanel.BankCardRow>();
                _bankCardsLoaded = false;
                _bankCardsLoadedItemId = 0;

                // IMPORTANT: host close means Basic is not open
                ClearIsBasicOpen_BestEffort();

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

        /// <summary>
        /// Host-callable "press Cancel" that reuses the existing cancel path.
        /// This delegates to BasicPanel.ForceCancelFromHost(), which raises CancelRequested
        /// (same as clicking Cancel).
        /// </summary>
        public void ForceCancelFromHost()
        {
#if DEBUG
            Debug.WriteLine("[ITEM-TABS] ForceCancelFromHost -> delegating to BasicPanel");
#endif
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
                return true;

            if (BasicPanel == null)
                return true;

            // Existing item in pure view mode can close without prompting/commit.
            if (IsExistingItemAndBasicPanelIsViewMode())
                return true;

            // Keep focus on Basic for commit prompt/validation workflow.
            if (ItemTabs != null && ItemTabs.SelectedIndex != TabIndexBasic)
                ForceSelectTab(TabIndexBasic);

            bool allowed = TryValidateAndPersistOnLeaveBasic();
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
                if (trigger == PersistTrigger.LeaveBasicTab)
                {
#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][PERSIST] Existing item (id={activeId!.Value}) leave-tab: no write. bookmarkOnly={isBookmarkOnly}");
#endif
                    return true;
                }

                try
                {
                    long itemId = activeId!.Value;

#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][UPDATE] Existing Save BEGIN itemId={itemId} bookmarkOnly={isBookmarkOnly}");
#endif

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

                    if (rows < 0)
                    {
                        SetStatus("Update failed.");
#if DEBUG
                        Debug.WriteLine($"[ITEM-TABS][UPDATE] FAILED itemId={itemId} rowsAffected={rows} bookmarkOnly={isBookmarkOnly}");
#endif
                        return false;
                    }

                    if (rows == 0)
                    {
#if DEBUG
                        Debug.WriteLine($"[ITEM-TABS][UPDATE] NO-OP itemId={itemId} rowsAffected=0 bookmarkOnly={isBookmarkOnly} (treated as success)");
#endif
                    }
                    else
                    {
#if DEBUG
                        Debug.WriteLine($"[ITEM-TABS][UPDATE] OK itemId={itemId} rowsAffected={rows} bookmarkOnly={isBookmarkOnly}");
#endif
                    }

                    SetSedsContextForCategoryItem(activeId.Value);
                    _hasPersistedId = true;

                    if (!TryCaptureAfterSignaturesFromUi(emailPlain, phonePlain, pinPlain))
                    {
                        SetStatus("Saved, but after-signature capture failed (see debug output).");
#if DEBUG
                        Debug.WriteLine($"[ITEM-TABS][AFTER-SIG] FAILED itemId={itemId} (existing) - treating as save failure.");
#endif
                        return false;
                    }

#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][AFTER-SIG] OK itemId={itemId} (existing) after-signatures captured.");
#endif

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
                        Debug.WriteLine($"[ITEM-TABS][PW-HIST] Existing Save passwordCheck itemId={itemId} pwIsNull={(pw == null)} pwIsWhite={(string.IsNullOrWhiteSpace(pw))}");
#endif

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

                        if (changes.Any)
                        {
                            TryWriteExistingItemChangedLog_BestEffort(
                                itemId: itemId,
                                categoryName: _categoryName,
                                itemName: name,
                                changes: changes);
                        }
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

                TryWriteNewItemCreatedLog_BestEffort(
                    itemId: newId,
                    categoryName: _categoryName,
                    itemName: name);

                // Save (even if staying open) must capture after-signatures for consistency.
                if (!TryCaptureAfterSignaturesFromUi(emailPlain, phonePlain, pinPlain))
                {
                    SetStatus("Inserted, but after-signature capture failed (see debug output).");
#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][AFTER-SIG] FAILED newId={newId} (new) - treating as save failure.");
#endif
                    return false;
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

        /* ======================= Duplicate Password Popup ======================= */

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
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][BASIC-COMMIT] Validation FAILED bookmarkOnly={isBookmarkOnly} okName={okName} okPw={okPassword} okPin={okPin} okEmail={okEmail} okPhone={okPhone}");
#endif
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
#if DEBUG
            Debug.WriteLine("[ITEM-TABS] BasicPanel SaveRequested");
#endif
            SetStatus("");

            if (BasicPanel == null)
                return;

            if (!TryCommitBasicFromBasicFlow("Fix the highlighted errors before saving."))
                return;

            // Refresh grid after successful save
            NotifyPanel_RefreshCategoryItemGrid_BestEffort();

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
#if DEBUG
            Debug.WriteLine("[ITEM-TABS] BasicPanel CancelRequested");
#endif
            SetStatus("");

            // Refresh no matter what (cancel path)
            NotifyPanel_RefreshCategoryItemGrid_BestEffort();

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
                    return;
                }
                // For BankCards save, persist Basic first using existing orchestration.
                if (!TryPersistBasicIfNeeded(PersistTrigger.Save, isBookmarkOnly))
                {
                    if (ItemTabs != null)
                        ForceSelectTab(TabIndexBasic);
                    return;
                }
            }
#if DEBUG
            else
            {
                Debug.WriteLine("[ITEM-TABS][BANKCARDS][SAVE] Existing+VIEW => bypass Basic validate/persist.");
            }
#endif
            var activeId = TryGetActiveCategoryItemId();
            if (!activeId.HasValue || activeId.Value <= 0)
            {
                SetStatus("Save failed: missing ItemId context.");
                return;
            }
            int itemId = activeId.Value;
            try
            {
                var serviceRows = BankCardsDraftRows
                    .Select(r => new CategoryItemService.BankCardRow
                    {
                        Id = r.Id,
                        ItemId = itemId,
                        CardTypeId = r.CardTypeId,
                        CardTypeDisplay = r.CardTypeDisplay ?? string.Empty,
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
                int writes = CategoryItemService.SaveBankCardsByItemId(itemId, serviceRows);
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][BANKCARDS][SAVE] itemId={itemId} writes={writes}");
#endif
                EnsureBankCardsLoadedForActiveItem(forceReload: true);
                NotifyPanel_RefreshCategoryItemGrid_BestEffort();
                ForceSelectTab(TabIndexBankCards);
                _lastTabIndex = TabIndexBankCards;
                SetStatus("");
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[ITEM-TABS][BANKCARDS][SAVE] FAILED itemId={itemId}: {ex}");
#endif
                SetStatus("Bank Cards save failed. See debug output.");
            }
        }
        private void BankCardsPanel_CancelAndExitRequested(object? sender, EventArgs e)
        {
#if DEBUG
            Debug.WriteLine("[ITEM-TABS] BankCardsPanel CancelAndExitRequested");
#endif
            // Refresh no matter what (bankcards cancel path)
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
#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][TAB] Leaving BASIC (EXISTING+VIEW) => allow switch index={newIndex} (no validation/no writes)");
#endif
                    NotifyPanel_RefreshCategoryItemGrid_BestEffort();
                    if (newIndex == TabIndexBankCards)
                    {
                        EnsureBankCardsLoadedForActiveItem(forceReload: false);
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
#if DEBUG
                    Debug.WriteLine($"[ITEM-TABS][TAB] Leaving BASIC => requestedIndex={newIndex}");
#endif
                    int requestedIndex = newIndex;

                    ForceSelectTab(TabIndexBasic);

                    bool allowLeaveBasic = TryValidateAndPersistOnLeaveBasic();
                    if (!allowLeaveBasic)
                    {
#if DEBUG
                        Debug.WriteLine("[ITEM-TABS][TAB] Leave BASIC BLOCKED -> stay on Basic.");
#endif
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

                    // IMPORTANT: status sync after any forced selection inside bankcards
                    UpdateIsBasicOpenFromUi_BestEffort();
                }

                if (!allowLeave)
                {
#if DEBUG
                    Debug.WriteLine("[ITEM-TABS][TAB] Leave BANKCARDS BLOCKED -> force back to BankCards.");
#endif
                    ForceSelectTab(TabIndexBankCards);

                    _lastTabIndex = TabIndexBankCards;
                    return;
                }
            }
            if (oldIndex != TabIndexBankCards && newIndex == TabIndexBankCards)
            {
                EnsureBankCardsLoadedForActiveItem(forceReload: false);
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

        private bool PromptSaveBasicBeforeTabSwitch()
        {
            const string title = "Save Basic Before Leaving?";
            const string body =
                "You have uncommitted Basic changes.\n\n" +
                "Choose Yes to save from Basic now, or No to stay on Basic.";

            try
            {
                var hostPanel = FindPanelHost();
                if (hostPanel == null)
                {
                    var r = MessageBox.Show(body, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
                    return r == MessageBoxResult.Yes;
                }

                var overlayHost = hostPanel.FindName("PopupOverlayHost") as Border;
                var overlayContent = hostPanel.FindName("PopupOverlayContent") as ContentControl;

                if (overlayHost == null || overlayContent == null)
                {
                    var r = MessageBox.Show(body, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
                    return r == MessageBoxResult.Yes;
                }

                bool accepted = false;

                var popup = new PopupDialog();
                popup.Configure(
                    severity: 0,
                    title: title,
                    message: body,
                    showCancel: true,
                    primaryText: "Yes",
                    secondaryText: "No");

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
            catch
            {
                var r = MessageBox.Show(body, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
                return r == MessageBoxResult.Yes;
            }
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

 