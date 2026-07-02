-- Params:
--   @CategoryKey (INTEGER)

UPDATE Category
SET IsActive = 0
WHERE Category_Key = @CategoryKey
  AND IFNULL(IsActive, 1) = 1;
