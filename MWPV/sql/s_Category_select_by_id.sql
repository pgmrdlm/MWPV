-- Params:
--   @CategoryKey (INTEGER)

SELECT
    Category_Key         AS CategoryKey,
    Category_Name        AS CategoryName,
    Category_Description AS CategoryDescription,
    IsActive             AS IsActive
FROM Category
WHERE Category_Key = @CategoryKey
LIMIT 1;
