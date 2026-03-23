/* File: sql/s_CategoryItemAccounts_update.sql */

UPDATE CategoryItemAccounts
SET
    Label               = @Label,
    Number              = @Number,
    AccountTypeId       = @AccountTypeId,
    AccountTypeFreeform = @AccountTypeFreeform,
    IsActive            = @IsActive,
    UpdatedAt           = CAST(strftime('%s','now') AS INTEGER)
WHERE Id = @Id
  AND ItemId = @ItemId;
