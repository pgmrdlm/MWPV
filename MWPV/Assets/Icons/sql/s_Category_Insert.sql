-- Params:
--   @CategoryName     (TEXT)
--   @Description      (TEXT)
--   @TypeDescription  (TEXT)  -- exact UI text

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
WHERE cd.ComboTypeId = 2      -- Categories bucket (per your DB)
  AND cd.Active = 1
  AND cd.Code = @TypeDescription
LIMIT 1;
