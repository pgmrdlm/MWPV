-- Params:
--   @CategoryName  (TEXT)
--   @Description   (TEXT)
--   @TypeCode      (TEXT)  -- ComboDetail.Code from UI

INSERT INTO Category (
    Category_Name,
    Category_Description,
    Category_Type,      -- INTEGER FK -> ComboDetail.ComboDetailId
    CreatedUtc
)
SELECT
    @CategoryName,
    @Description,
    cd.ComboDetailId,
    strftime('%Y-%m-%dT%H:%M:%fZ','now')
FROM ComboDetail cd
JOIN ComboType ct
  ON ct.ComboTypeId = cd.ComboTypeId
WHERE ct.Code = 'category_types'   -- resolve bucket by Code (no hard-coded Id)
  AND cd.Active = 1
  AND cd.Code = @TypeCode          -- match by Code from UI
LIMIT 1;
