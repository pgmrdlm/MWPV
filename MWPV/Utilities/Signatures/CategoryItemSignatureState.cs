// File: Utilities/Signatures/CategoryItemSignatureState.cs
//
// FULL REWRITE
//
// PURPOSE
// - Lightweight, UI-agnostic container for tracking ORIGINAL vs CURRENT state
//   for both masked/sensitive fields (signatures) AND non-sensitive plain fields.
// - Used to detect "real change occurred" without snapshotting plaintext sensitive values.
// - Stores ONLY signature bytes (HMAC) for masked fields; non-sensitive fields store normalized values.
//
// RULES
// - MASKED fields must use SignatureSlot (HMAC bytes only).
// - Non-sensitive fields use NonSensitiveSlot / BoolSlot (normalized values only).
// - Logging decisions are based on which slots changed (AFTER DB success).
//
// MASKED / SENSITIVE FIELDS (from DDL)
// - CategoryItem.CI_AccountEmail (BLOB)
// - CategoryItem.CI_AccountPhoneNumber (BLOB)
// - CategoryItem.CI_Pin (BLOB)
// - CategoryItemPasswordHistory.CIPaH_PwFp (BLOB)  [most-recent row only]
//
// NON-SENSITIVE FIELDS (BasicTab candidates)
// - CategoryItem.CI_Name
// - CategoryItem.CI_Description / Notes (depends on schema usage)
// - CategoryItem.CI_UserName
// - CategoryItem.CI_SignInUrl
// - CategoryItem.CI_Notes
// - CategoryItem.CI_BookMarkOnly
//
// NOTES
// - This class does NOT compute signatures. It only stores and compares state.
// - Sensitive signature bytes can be wiped via Clear().
// - Non-sensitive values are cleared by nulling references (best-effort).
//

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Security.Utility.Wiping;

namespace MWPV.Utilities.Signatures
{
    /// <summary>
    /// Holds original/current HMAC signatures for masked fields AND original/current values
    /// for non-sensitive fields so we can detect real changes consistently.
    /// </summary>
    public sealed class CategoryItemSignatureState
    {
        // -----------------------------
        // Column-name keys (per your DDL / naming conventions)
        // -----------------------------

        // MASKED fields (signature compare)
        public const string Field_CI_AccountEmail = "CI_AccountEmail";
        public const string Field_CI_AccountPhoneNumber = "CI_AccountPhoneNumber";
        public const string Field_CI_Pin = "CI_Pin";
        public const string Field_CIPaH_PwFp = "CIPaH_PwFp";

        // NON-SENSITIVE fields (plain compare)
        public const string Field_CI_Name = "CI_Name";
        public const string Field_CI_Description = "CI_Description";
        public const string Field_CI_UserName = "CI_UserName";
        public const string Field_CI_SignInUrl = "CI_SignInUrl";
        public const string Field_CI_Notes = "CI_Notes";
        public const string Field_CI_BookMarkOnly = "CI_BookMarkOnly";

        // -----------------------------
        // Signature slots (orig + curr)
        // -----------------------------
        public SignatureSlot AccountEmail { get; } = new(Field_CI_AccountEmail);
        public SignatureSlot AccountPhoneNumber { get; } = new(Field_CI_AccountPhoneNumber);
        public SignatureSlot Pin { get; } = new(Field_CI_Pin);

        /// <summary>
        /// The most-recent password fingerprint for this item (from CategoryItemPasswordHistory).
        /// Current value is computed from the UI password input before Save.
        /// </summary>
        public SignatureSlot PasswordFingerprint { get; } = new(Field_CIPaH_PwFp);

        // -----------------------------
        // Non-sensitive slots (orig + curr)
        // -----------------------------
        public NonSensitiveSlot Name { get; } = new(Field_CI_Name);
        public NonSensitiveSlot Description { get; } = new(Field_CI_Description);
        public NonSensitiveSlot UserName { get; } = new(Field_CI_UserName);
        public NonSensitiveSlot SignInUrl { get; } = new(Field_CI_SignInUrl);
        public NonSensitiveSlot Notes { get; } = new(Field_CI_Notes);
        public BoolSlot BookMarkOnly { get; } = new(Field_CI_BookMarkOnly);

        /// <summary>
        /// Clears all stored state (best-effort wipe for signatures).
        /// Call when leaving the editor/panel.
        /// </summary>
        public void Clear()
        {
            // Masked signature bytes (wipe)
            AccountEmail.Clear();
            AccountPhoneNumber.Clear();
            Pin.Clear();
            PasswordFingerprint.Clear();

            // Non-sensitive values (best-effort null)
            Name.Clear();
            Description.Clear();
            UserName.Clear();
            SignInUrl.Clear();
            Notes.Clear();
            BookMarkOnly.Clear();
        }

        /// <summary>
        /// Returns a list of field keys that have changed (orig != curr).
        /// Only returns keys for slots that have both Original and Current populated.
        /// </summary>
        public IReadOnlyList<string> GetChangedFields()
        {
            var changed = new List<string>(capacity: 12);

            // Sensitive signature-backed fields
            AddIfChanged(changed, AccountEmail);
            AddIfChanged(changed, AccountPhoneNumber);
            AddIfChanged(changed, Pin);
            AddIfChanged(changed, PasswordFingerprint);

            // Non-sensitive fields
            AddIfChanged(changed, Name);
            AddIfChanged(changed, Description);
            AddIfChanged(changed, UserName);
            AddIfChanged(changed, SignInUrl);
            AddIfChanged(changed, Notes);
            AddIfChanged(changed, BookMarkOnly);

            return changed;
        }

        /// <summary>
        /// Convenience: any changed?
        /// </summary>
        public bool HasAnyChanges()
            => AccountEmail.IsChanged
            || AccountPhoneNumber.IsChanged
            || Pin.IsChanged
            || PasswordFingerprint.IsChanged
            || Name.IsChanged
            || Description.IsChanged
            || UserName.IsChanged
            || SignInUrl.IsChanged
            || Notes.IsChanged
            || BookMarkOnly.IsChanged;

        private static void AddIfChanged(List<string> list, SignatureSlot slot)
        {
            if (slot.IsChanged)
                list.Add(slot.FieldKey);
        }

        private static void AddIfChanged(List<string> list, NonSensitiveSlot slot)
        {
            if (slot.IsChanged)
                list.Add(slot.FieldKey);
        }

        private static void AddIfChanged(List<string> list, BoolSlot slot)
        {
            if (slot.IsChanged)
                list.Add(slot.FieldKey);
        }
    }

    /// <summary>
    /// Holds Original + Current signature bytes for one logical masked field.
    /// Does constant-time comparison.
    /// </summary>
    public sealed class SignatureSlot
    {
        public string FieldKey { get; }

        // Backing fields (must be fields, not properties, because we wipe/replace by ref)
        private byte[]? _originalSig;
        private byte[]? _currentSig;

        /// <summary>Original signature captured at load time (defensive copy stored).</summary>
        public byte[]? OriginalSig => _originalSig;

        /// <summary>Current signature captured right before Save (defensive copy stored).</summary>
        public byte[]? CurrentSig => _currentSig;

        public SignatureSlot(string fieldKey)
        {
            FieldKey = fieldKey ?? throw new ArgumentNullException(nameof(fieldKey));
        }

        /// <summary>Set original signature (load time).</summary>
        public void SetOriginal(byte[]? sig)
        {
            ReplaceAndWipe(ref _originalSig, sig);
        }

        /// <summary>Set current signature (right before Save).</summary>
        public void SetCurrent(byte[]? sig)
        {
            ReplaceAndWipe(ref _currentSig, sig);
        }

        /// <summary>
        /// True only if both Original and Current are present and differ.
        /// </summary>
        public bool IsChanged
        {
            get
            {
                if (_originalSig is null || _currentSig is null) return false;
                if (_originalSig.Length != _currentSig.Length) return true;
                return !CryptographicOperations.FixedTimeEquals(_originalSig, _currentSig);
            }
        }

        /// <summary>
        /// Clear both signatures (best-effort wipe).
        /// </summary>
        public void Clear()
        {
            SensitiveDataCleaner.Zero(_originalSig);
            SensitiveDataCleaner.Zero(_currentSig);
            _originalSig = null;
            _currentSig = null;
        }

        private static void ReplaceAndWipe(ref byte[]? target, byte[]? replacement)
        {
            // Wipe existing stored bytes first.
            SensitiveDataCleaner.Zero(target);

            // Defensive copy so caller can wipe/reuse their buffer without affecting our stored state.
            target = replacement is null ? null : replacement.ToArray();
        }
    }

    /// <summary>
    /// Holds Original + Current normalized string values for a non-sensitive field.
    /// Normalization is applied on set (trim + null => empty).
    /// </summary>
    public sealed class NonSensitiveSlot
    {
        public string FieldKey { get; }

        private string? _original;
        private string? _current;

        public string? OriginalValue => _original;
        public string? CurrentValue => _current;

        public NonSensitiveSlot(string fieldKey)
        {
            FieldKey = fieldKey ?? throw new ArgumentNullException(nameof(fieldKey));
        }

        public void SetOriginal(string? value)
        {
            _original = Normalize(value);
        }

        public void SetCurrent(string? value)
        {
            _current = Normalize(value);
        }

        /// <summary>
        /// True only if both Original and Current are present and differ (ordinal compare).
        /// </summary>
        public bool IsChanged
        {
            get
            {
                if (_original is null || _current is null) return false;
                return !string.Equals(_original, _current, StringComparison.Ordinal);
            }
        }

        public void Clear()
        {
            _original = null;
            _current = null;
        }

        private static string Normalize(string? value)
            => (value ?? string.Empty).Trim();
    }

    /// <summary>
    /// Holds Original + Current bool values for a non-sensitive toggle field (e.g., bookmark).
    /// </summary>
    public sealed class BoolSlot
    {
        public string FieldKey { get; }

        private bool? _original;
        private bool? _current;

        public bool? OriginalValue => _original;
        public bool? CurrentValue => _current;

        public BoolSlot(string fieldKey)
        {
            FieldKey = fieldKey ?? throw new ArgumentNullException(nameof(fieldKey));
        }

        public void SetOriginal(bool? value)
        {
            _original = value;
        }

        public void SetCurrent(bool? value)
        {
            _current = value;
        }

        /// <summary>
        /// True only if both Original and Current are present and differ.
        /// </summary>
        public bool IsChanged
        {
            get
            {
                if (!_original.HasValue || !_current.HasValue) return false;
                return _original.Value != _current.Value;
            }
        }

        public void Clear()
        {
            _original = null;
            _current = null;
        }
    }
}
