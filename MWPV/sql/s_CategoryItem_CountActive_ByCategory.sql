-- File: sql/s_CategoryItem_CountActive_ByCategory.sql
--
-- Purpose:
-- - Count active CategoryItem records for a single category.
--
-- Params:
--   @CategoryKey (INTEGER)
--
SELECT COUNT(1)
FROM CategoryItem
WHERE Category_Key = @CategoryKey
  AND IFNULL(IsActive, 1) = 1;
