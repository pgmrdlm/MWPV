-- CategoryExists.sql
-- Returns count of active categories matching the provided name (case-insensitive).
SELECT COUNT(1)
FROM Category
WHERE Category_Name = @CategoryName COLLATE NOCASE
  AND IsActive = 1;
