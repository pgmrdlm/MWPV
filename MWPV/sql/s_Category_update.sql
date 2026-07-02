-- Params:
--   @CategoryKey  (INTEGER)
--   @CategoryName (TEXT)
--   @Description  (TEXT)

UPDATE Category
SET
    Category_Name        = @CategoryName,
    Category_Description = @Description
WHERE Category_Key = @CategoryKey;
