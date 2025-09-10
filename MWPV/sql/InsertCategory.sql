-- InsertCategory.sql
-- Inserts a new category with optional Description.
-- Assumes CategoryExists.sql has already checked for duplicates (case-insensitive).

INSERT INTO Category (Category_Name, Category_Description, IsActive)
VALUES (@CategoryName, @Description, 1);
