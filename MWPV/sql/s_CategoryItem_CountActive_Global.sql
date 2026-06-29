-- File: sql/s_CategoryItem_CountActive_Global.sql
--
-- Purpose:
-- - Count all active CategoryItem records in the database.
--
SELECT COUNT(1)
FROM CategoryItem
WHERE IFNULL(IsActive, 1) = 1;
