public sealed class BankCard
{
    public int Id { get; set; }                // BC_Id
    public int ItemId { get; set; }            // BC_ItemId (CategoryItem FK)
    public int CardTypeId { get; set; }        // BC_CardType (ComboDetail FK)

    public string? Cardholder { get; set; }    // BC_Cardholder (optional, non-secret)

    public byte[] Number { get; set; } = [];   // BC_Number (encrypted PAN)
    public int ExpMonth { get; set; }          // BC_ExpMonth
    public int ExpYear { get; set; }           // BC_ExpYear

    public byte[]? Cvv { get; set; }           // BC_CVV (encrypted, optional)
    public byte[]? Pin { get; set; }           // BC_Pin (encrypted, optional)
    public byte[]? BillingZip { get; set; }    // BC_BillingZip (encrypted, optional)

    public bool IsPrimary { get; set; }        // BC_IsPrimary (0/1)

    // Stored as Unix epoch seconds in SQLite (strftime('%s','now'))
    public long CreatedAtUtcSeconds { get; set; }  // BC_CreatedAt
    public long UpdatedAtUtcSeconds { get; set; }  // BC_UpdatedAt
}
