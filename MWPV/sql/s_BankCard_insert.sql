/* File: sql/s_BankCard_insert.sql */

INSERT INTO BankCards
(
    BC_ItemId,
    BC_CardType,
    BC_Cardholder,
    BC_Number,
    BC_ExpMonth,
    BC_ExpYear,
    BC_CVV,
    BC_Pin,
    BC_BillingZip,
    BC_IsPrimary,
    BC_IsActive
)
VALUES
(
    @ItemId,
    @CardTypeId,
    @Cardholder,
    @Number,
    @ExpMonth,
    @ExpYear,
    @Cvv,
    @Pin,
    @BillingZip,
    @IsPrimary,
    @IsActive
);

SELECT last_insert_rowid() AS Id;
