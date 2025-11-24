-- Params:
--   @CategoryName (TEXT)
--   @Description  (TEXT)

INSERT INTO Category (
    Category_Name,
    Category_Description,
    Category_Type,
    CreatedUtc
)
VALUES (
    @CategoryName,
    @Description,
    0,  -- Category_Type: unused for now, default bucket
    strftime('%Y-%m-%dT%H:%M:%fZ','now')
);
