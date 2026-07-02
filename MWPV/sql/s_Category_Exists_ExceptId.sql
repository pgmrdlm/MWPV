-- Params:
--   @CategoryName (TEXT)
--   @CategoryKey  (INTEGER)

SELECT COUNT(1)
FROM Category
WHERE Category_Name = @CategoryName COLLATE NOCASE
  AND Category_Key <> @CategoryKey
  AND IFNULL(IsActive, 1) = 1;
