-- InsertCatagory.sql
-- Inserts a new category with optional Description.
-- Assumes CatagoryExists.sql has already checked for duplicates (case-insensitive).

INSERT INTO Catagory (Catagory_Name, Catagory_Description, IsActive)
VALUES (@catagoryname, @description, 1);
