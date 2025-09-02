-- CategoryExists.sql
-- Returns count of categories matching the provided name (case-insensitive).
-- Only counts active categories (IsActive = 1).
SELECT EXISTS(
  SELECT 1
  FROM Category c
  WHERE c.Category_Name = @Categoryname COLLATE NOCASE
    AND IFNULL(c.IsActive, 1) = 1
);

