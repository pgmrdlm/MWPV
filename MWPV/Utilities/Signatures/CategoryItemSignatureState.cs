// File: Utilities/Signatures/CategoryItemSignatureState.cs
//
// FULL REWRITE
//
// PURPOSE
// - Lightweight, UI-agnostic container for tracking ORIGINAL vs CURRENT signatures
//   for masked/sensitive fields in CategoryItem and PasswordHistory.
// - Used to detect "real change occurred" without snapshotting plaintext values.
// - Stores ONLY signature bytes (HMAC-based), never the plaintext.
//
// RULE
// - Any field that is MASKED in the UI (encrypted BLOB in DB) must have ORIGINAL signature
//   captured at load time.
// - Before Save, compute CURRENT signatures and compare.
// - Logging decisions are based on which signatures changed (AFTER DB success).
//
// MASKED / SENSITIVE FIELDS (from DDL)
// - CategoryItem.CI_AccountEmail (BLOB)
// - CategoryItem.CI_AccountPhoneNumber (BLOB)
// - CategoryItem.CI_Pin (BLOB)
// - CategoryItemPasswordHistory.CIPaH_PwFp (BLOB)  [most-recent row only]
//
// NOTES
// - This class does NOT compute signatures. It only stores and compares them.
// - Signature bytes are treated as sensitive state and can be wiped via Clear().
//

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Security.Utility.Wiping;

namespace MWPV.Utilities.Signatures
{
    /// <summary>
    /// Holds original/current HMAC signatures for masked fields so we can detect real changes
    /// without snapshotting sensitive plaintext values.
    /// </summary>
    public sealed class CategoryItemSignatureState
    {
        // -----------------------------
        // Column-name keys (per your DDL)
        // -----------------------------
        public const string Field_CI_AccountEmail = "CI_AccountEmail";
        public const string Field_CI_AccountPhoneNumber = "CI_AccountPhoneNumber";
        public const string Field_CI_Pin = "CI_Pin";

        // Password fingerprint lives in the history table
        public const string Field_CIPaH_PwFp = "CIPaH_PwFp";

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

        /// <summary>
        /// Clears all stored signature bytes (best-effort wipe).
        /// Call when leaving the editor/panel.
        /// </summary>
        public void Clear()
        {
            AccountEmail.Clear();
            AccountPhoneNumber.Clear();
            Pin.Clear();
            PasswordFingerprint.Clear();
        }

        /// <summary>
        /// Returns a list of field keys that have changed (orig != curr).
        /// Only returns keys for slots that have both Original and Current populated.
        /// </summary>
        public IReadOnlyList<string> GetChangedFields()
        {
            var changed = new List<string>(capacity: 4);

            AddIfChanged(changed, AccountEmail);
            AddIfChanged(changed, AccountPhoneNumber);
            AddIfChanged(changed, Pin);
            AddIfChanged(changed, PasswordFingerprint);

            return changed;
        }

        /// <summary>
        /// Convenience: any changed?
        /// </summary>
        public bool HasAnyChanges()
            => AccountEmail.IsChanged
            || AccountPhoneNumber.IsChanged
            || Pin.IsChanged
            || PasswordFingerprint.IsChanged;

        private static void AddIfChanged(List<string> list, SignatureSlot slot)
        {
            if (slot.IsChanged)
                list.Add(slot.FieldKey);
        }
    }

    /// <summary>
    /// Holds Original + Current signature bytes for one logical field.
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
}
