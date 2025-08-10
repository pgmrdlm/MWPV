-- CatagoryExists.sql
-- Returns count of categories matching the provided name (case-insensitive).
-- Only counts active categories (IsActive = 1).
SELECT EXISTS(
  SELECT 1
  FROM Catagory c
  WHERE c.Catagory_Name = @catagoryname COLLATE NOCASE
    AND IFNULL(c.IsActive, 1) = 1
);

