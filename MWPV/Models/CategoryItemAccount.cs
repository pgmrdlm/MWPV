using System;

namespace MWPV.Models
{
    /// <summary>
    /// Represents a general account number associated with a CategoryItem.
    /// Backed by the CategoryItemAccounts table.
    /// </summary>
    public sealed class CategoryItemAccount
    {
        // CIA_Id
        public int Id { get; set; }

        // CIA_ItemId (FK -> CategoryItem.ItemId)
        public int ItemId { get; set; }

        // CIA_AccountLabel (friendly, non-secret)
        public string? AccountLabel { get; set; }

        // CIA_AccountNumber (encrypted)
        public byte[] AccountNumber { get; set; } = [];

        // CIA_RoutingNumber (encrypted, optional)
        public byte[]? RoutingNumber { get; set; }

        // CIA_IBAN (encrypted, optional)
        public byte[]? Iban { get; set; }

        // CIA_SWIFT (encrypted, optional)
        public byte[]? Swift { get; set; }

        // CIA_Meta (encrypted misc: branch, notes, etc.)
        public byte[]? Meta { get; set; }

        // CIA_AccountType (ComboDetail FK, optional)
        public int? AccountTypeId { get; set; }

        // CIA_AccountTypeOther (freeform when using OTHER)
        public string? AccountTypeOther { get; set; }

        // CIA_IsPrimary (0/1)
        public bool IsPrimary { get; set; }

        // CIA_CreatedAt / CIA_UpdatedAt (Unix epoch seconds in UTC)
        public long CreatedAtUtcSeconds { get; set; }
        public long UpdatedAtUtcSeconds { get; set; }
    }
}
