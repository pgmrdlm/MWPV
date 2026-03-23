/* File: sql/s_CategoryItemAccounts_select_primary_by_itemid.sql */

SELECT
    cia.Id                  AS Id,
    cia.ItemId              AS ItemId,
    cia.Label               AS Label,
    cia.Number              AS Number,
    cia.AccountTypeId       AS AccountTypeId,
    cia.AccountTypeFreeform AS AccountTypeFreeform,
    cia.IsActive            AS IsActive,
    cia.CreatedAt           AS CreatedAtUtcSeconds,
    cia.UpdatedAt           AS UpdatedAtUtcSeconds
FROM CategoryItemAccounts cia
JOIN ComboDetail cd
    ON cd.ComboDetailId = cia.AccountTypeId
JOIN ComboType ct
    ON ct.ComboTypeId = cd.ComboTypeId
WHERE cia.ItemId = @ItemId
  AND cia.IsActive = 1
  AND ct.Code = 'account_types'
  AND ct.Active = 1
  AND cd.Code = 'PRIMARY'
  AND cd.Active = 1
ORDER BY cia.CreatedAt DESC, cia.Id DESC
LIMIT 1;
