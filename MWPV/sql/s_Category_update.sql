-- Params:
--   @CategoryKey  (INTEGER)
--   @CategoryName (TEXT)
--   @Description  (TEXT)
--   @IsActive     (INTEGER)

UPDATE Category
SET
    Category_Name        = @CategoryName,
    Category_Description = @Description,
    IsActive             = @IsActive
WHERE Category_Key = @CategoryKey;
