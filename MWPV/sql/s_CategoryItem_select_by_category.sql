-- s_CategoryItem_select_by_category.sql
SELECT
    ItemId,
    Category_Key,
    CI_Name,
    CI_UpdateUTC,
    IsActive
FROM CategoryItem
WHERE Category_Key = @Category_Key
  AND IsActive = COALESCE(@IsActive, IsActive)
ORDER BY CI_Name COLLATE NOCASE;
