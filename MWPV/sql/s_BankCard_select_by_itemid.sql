/* File: sql/s_BankCard_select_by_itemid.sql */

SELECT
    BC_Id          AS Id,
    BC_ItemId      AS ItemId,
    BC_CardType    AS CardTypeId,
    BC_Cardholder  AS Cardholder,
    BC_Number      AS Number,
    BC_ExpMonth    AS ExpMonth,
    BC_ExpYear     AS ExpYear,
    BC_CVV         AS Cvv,
    BC_Pin         AS Pin,
    BC_BillingZip  AS BillingZip,
    BC_IsPrimary   AS IsPrimary,
    BC_IsActive    AS IsActive,
    BC_CreatedAt   AS CreatedAtUtcSeconds,
    BC_UpdatedAt   AS UpdatedAtUtcSeconds
FROM BankCards
WHERE BC_ItemId = @ItemId
  AND BC_IsActive = 1
ORDER BY BC_IsPrimary DESC, BC_ExpYear DESC, BC_ExpMonth DESC, BC_Id DESC;
