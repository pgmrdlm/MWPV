/* File: sql/s_CategoryItemAccounts_select_by_itemid.sql */

SELECT
    Id                  AS Id,
    ItemId              AS ItemId,
    Label               AS Label,
    Number              AS Number,
    AccountTypeId       AS AccountTypeId,
    AccountTypeFreeform AS AccountTypeFreeform,
    IsActive            AS IsActive,
    CreatedAt           AS CreatedAtUtcSeconds,
    UpdatedAt           AS UpdatedAtUtcSeconds
FROM CategoryItemAccounts
WHERE ItemId = @ItemId
  AND IsActive = 1
ORDER BY Id DESC;
