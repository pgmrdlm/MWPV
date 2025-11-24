using System;

namespace MWPV.Models
{
    /// <summary>
    /// Represents a single bank / credit / debit card associated with a CategoryItem.
    /// All sensitive values are stored encrypted in the database.
    /// </summary>
    public sealed class BankCard
    {
        /// <summary>
        /// Primary key (BC_Id).
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Foreign key to CategoryItem (BC_ItemId).
        /// </summary>
        public int ItemId { get; set; }

        /// <summary>
        /// ComboDetailId for the card type (BC_CardType).
        /// </summary>
        public int CardTypeId { get; set; }

        /// <summary>
        /// Optional, non-secret display name (BC_Cardholder).
        /// </summary>
        public string? Cardholder { get; set; }

        /// <summary>
        /// Encrypted PAN digits (BC_Number).
        /// </summary>
        public byte[] Number { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Expiration month, 1-12 (BC_ExpMonth).
        /// </summary>
        public int ExpMonth { get; set; }

        /// <summary>
        /// Four-digit expiration year (BC_ExpYear).
        /// </summary>
        public int ExpYear { get; set; }

        /// <summary>
        /// Encrypted CVV / CVC value, optional (BC_CVV).
        /// </summary>
        public byte[]? Cvv { get; set; }

        /// <summary>
        /// Encrypted PIN, optional (BC_Pin).
        /// </summary>
        public byte[]? Pin { get; set; }

        /// <summary>
        /// Encrypted billing ZIP / postal code, optional (BC_BillingZip).
        /// </summary>
        public byte[]? BillingZip { get; set; }

        /// <summary>
        /// Indicates this is the preferred / primary card for the item (BC_IsPrimary).
        /// </summary>
        public bool IsPrimary { get; set; }

        /// <summary>
        /// Soft-delete / active flag (BC_IsActive).
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Creation time in Unix epoch seconds (BC_CreatedAt).
        /// </summary>
        public long CreatedAtUtcSeconds { get; set; }

        /// <summary>
        /// Last update time in Unix epoch seconds (BC_UpdatedAt).
        /// </summary>
        public long UpdatedAtUtcSeconds { get; set; }
    }
}
