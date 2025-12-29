-- s_CategoryItem_exists_by_name.sql
SELECT
    ci.ItemId,
    ci.Category_Key,
    c.Category_Name,
    ci.CI_Name,
    ci.CI_CreateUTC,
    ci.CI_UpdateUTC
FROM CategoryItem ci
JOIN Category c
  ON c.Category_Key = ci.Category_Key
WHERE UPPER(TRIM(ci.CI_Name)) = UPPER(TRIM(@CI_Name))
  AND (@ExcludeItemId IS NULL OR ci.ItemId <> @ExcludeItemId)
ORDER BY c.Category_Name, ci.CI_Name;
