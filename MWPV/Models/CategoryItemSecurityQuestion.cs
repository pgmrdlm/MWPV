using System;
using System.ComponentModel;
using System.Diagnostics;

namespace MWPV.Models
{
    /// <summary>
    /// Maps to table: CategoryItemSecurityQuestions
    /// </summary>
    [DebuggerDisplay("CISQ #{Id} Item={ItemId} Seq={Seq} Q='{QuestionPlain}'")]
    public sealed class CategoryItemSecurityQuestion : INotifyPropertyChanged
    {
        // --- DB column names (for future queries; no DB code here) ---
        public const string TableName = "CategoryItemSecurityQuestions";
        public const string Col_Id = "CISQ_Id";
        public const string Col_ItemId = "CISQ_ItemId";
        public const string Col_Seq = "CISQ_Seq";
        public const string Col_QuestionBlob = "CISQ_Question";
        public const string Col_AnswerBlob = "CISQ_Answer";
        public const string Col_IsActive = "CISQ_IsActive";
        public const string Col_CreatedAt = "CISQ_CreatedAt";
        public const string Col_UpdatedAt = "CISQ_UpdatedAt";

        // --- DB-mapped fields ---
        private int _id;
        public int Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(nameof(Id)); } }
        }

        private int _itemId;
        public int ItemId
        {
            get => _itemId;
            set { if (_itemId != value) { _itemId = value; OnPropertyChanged(nameof(ItemId)); } }
        }

        private int _seq;
        /// <summary>Display/order index (unique per ItemId).</summary>
        public int Seq
        {
            get => _seq;
            set { if (_seq != value) { _seq = value; Touch(); OnPropertyChanged(nameof(Seq)); } }
        }

        private byte[] _question = Array.Empty<byte>();
        /// <summary>Encrypted question bytes (DDL: BLOB NOT NULL).</summary>
        public byte[] Question
        {
            get => _question;
            set { _question = value ?? Array.Empty<byte>(); Touch(); OnPropertyChanged(nameof(Question)); }
        }

        private byte[] _answer = Array.Empty<byte>();
        /// <summary>Encrypted answer bytes (DDL: BLOB NOT NULL).</summary>
        public byte[] Answer
        {
            get => _answer;
            set { _answer = value ?? Array.Empty<byte>(); Touch(); OnPropertyChanged(nameof(Answer)); }
        }

        private bool _isActive = true;
        /// <summary>Soft-delete / active flag (CISQ_IsActive).</summary>
        public bool IsActive
        {
            get => _isActive;
            set { if (_isActive != value) { _isActive = value; Touch(); OnPropertyChanged(nameof(IsActive)); } }
        }

        private long _createdAtUnix;
        /// <summary>Unix seconds (UTC) when created.</summary>
        public long CreatedAtUnix
        {
            get => _createdAtUnix;
            set { if (_createdAtUnix != value) { _createdAtUnix = value; OnPropertyChanged(nameof(CreatedAtUnix)); OnPropertyChanged(nameof(CreatedAtUtc)); } }
        }

        private long _updatedAtUnix;
        /// <summary>Unix seconds (UTC) when last updated.</summary>
        public long UpdatedAtUnix
        {
            get => _updatedAtUnix;
            set { if (_updatedAtUnix != value) { _updatedAtUnix = value; OnPropertyChanged(nameof(UpdatedAtUnix)); OnPropertyChanged(nameof(UpdatedAtUtc)); } }
        }

        // --- Convenience projections (not stored) ---
        public DateTime CreatedAtUtc
            => DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, CreatedAtUnix)).UtcDateTime;

        public DateTime UpdatedAtUtc
            => DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, UpdatedAtUnix)).UtcDateTime;

        // --- UI-only bindable plaintext (populated post-decrypt later) ---
        private string _questionPlain = "";
        /// <summary>Plaintext question for UI binding. Not persisted as-is.</summary>
        public string QuestionPlain
        {
            get => _questionPlain;
            set
            {
                if (_questionPlain != value)
                {
                    _questionPlain = value ?? "";
                    Touch();
                    OnPropertyChanged(nameof(QuestionPlain));
                    OnPropertyChanged(nameof(HasPlainValues));
                }
            }
        }

        private string _answerPlain = "";
        /// <summary>Plaintext answer for UI binding. Not persisted as-is.</summary>
        public string AnswerPlain
        {
            get => _answerPlain;
            set
            {
                if (_answerPlain != value)
                {
                    _answerPlain = value ?? "";
                    Touch();
                    OnPropertyChanged(nameof(AnswerPlain));
                    OnPropertyChanged(nameof(HasPlainValues));
                }
            }
        }

        public bool HasPlainValues => !string.IsNullOrWhiteSpace(QuestionPlain) || !string.IsNullOrEmpty(AnswerPlain);

        // --- UI helpers (not persisted) ---
        private bool _isAnswerVisible;
        public bool IsAnswerVisible
        {
            get => _isAnswerVisible;
            set { if (_isAnswerVisible != value) { _isAnswerVisible = value; OnPropertyChanged(nameof(IsAnswerVisible)); } }
        }

        private bool _isDirty;
        /// <summary>Set whenever a mutable property changes. Reset externally after save.</summary>
        public bool IsDirty
        {
            get => _isDirty;
            private set { if (_isDirty != value) { _isDirty = value; OnPropertyChanged(nameof(IsDirty)); } }
        }

        // --- Lifecycle ---
        public static CategoryItemSecurityQuestion NewForItem(int itemId, int seq = 0)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return new CategoryItemSecurityQuestion
            {
                ItemId = itemId,
                Seq = seq,
                CreatedAtUnix = now,
                UpdatedAtUnix = now
            };
        }

        public void Touch()
        {
            UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            IsDirty = true;
        }

        public void MarkClean() => IsDirty = false;

        // --- Simple validation for UI gating (no persistence/encryption) ---
        public bool IsValid(out string? error)
        {
            if (string.IsNullOrWhiteSpace(QuestionPlain))
            {
                error = "Question is required.";
                return false;
            }
            if (string.IsNullOrEmpty(AnswerPlain))
            {
                error = "Answer is required.";
                return false;
            }
            error = null;
            return true;
        }

        // --- INotifyPropertyChanged ---
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
