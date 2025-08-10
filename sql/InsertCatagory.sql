-- InsertCatagory.sql
-- Simple insert for a new category.
-- Assumes CatagoryExists.sql has already checked for duplicates (case-insensitive).
INSERT INTO Catagory (Catagory_Name, IsActive)
VALUES (@catagoryname, 1);
