-- Inserts a new category with its type
-- Expected params:
--   @CategoryName  (TEXT)
--   @Description   (TEXT)
--   @TypeCode      (TEXT)  -- FK to ComboDetail.Code for header 'CATEGORY_TYPE'

INSERT INTO Category (
    Category_Name,
    Category_Description,
    Category_Type,
    CreatedUtc
)
VALUES (
    @CategoryName,
    @Description,
    @TypeCode,
    strftime('%Y-%m-%dT%H:%M:%fZ','now')
);
