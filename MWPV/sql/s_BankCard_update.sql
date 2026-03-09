/* File: sql/s_BankCard_update.sql */

UPDATE BankCards
SET
    BC_CardType   = @CardTypeId,
    BC_Cardholder = @Cardholder,
    BC_Number     = @Number,
    BC_ExpMonth   = @ExpMonth,
    BC_ExpYear    = @ExpYear,
    BC_CVV        = @Cvv,
    BC_Pin        = @Pin,
    BC_BillingZip = @BillingZip,
    BC_IsPrimary  = @IsPrimary,
    BC_IsActive   = @IsActive,
    BC_UpdatedAt  = CAST(strftime('%s','now') AS INTEGER)
WHERE BC_Id = @Id
  AND BC_ItemId = @ItemId;
