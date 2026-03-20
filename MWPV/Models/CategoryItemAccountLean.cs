using System;

namespace MWPV.Models
{
    /// <summary>
    /// Represents a lean account row backed by the CategoryItemAccounts table.
    /// Sensitive values are stored encrypted in the database.
    /// </summary>
    public sealed class CategoryItemAccountLean
    {
        /// <summary>
        /// Primary key (Id).
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Foreign key to CategoryItem (ItemId).
        /// </summary>
        public int ItemId { get; set; }

        /// <summary>
        /// Optional, non-secret display label (Label).
        /// </summary>
        public string? Label { get; set; }

        /// <summary>
        /// Encrypted account number bytes (Number).
        /// </summary>
        public byte[] Number { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Soft-delete / active flag (IsActive).
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Creation time in Unix epoch seconds (CreatedAt).
        /// </summary>
        public long CreatedAtUtcSeconds { get; set; }

        /// <summary>
        /// Last update time in Unix epoch seconds (UpdatedAt).
        /// </summary>
        public long UpdatedAtUtcSeconds { get; set; }
    }
}
