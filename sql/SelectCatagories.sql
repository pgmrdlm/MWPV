/*
    SelectCatagories.sql
    Updated for new schema:
    - Orders deterministically by name (case-insensitive)
    - Filters to active categories (IsActive = 1)
    - Outputs 3 columns of names + descriptions
*/
WITH Numbered AS (
    SELECT
        Catagory_Name,
        Catagory_Description,
        ROW_NUMBER() OVER (ORDER BY Catagory_Name COLLATE NOCASE) - 1 AS rn
    FROM Catagory
    WHERE IFNULL(IsActive, 1) = 1
),
Grouped AS (
    SELECT
        (rn / 3) AS group_id,
        rn % 3 AS col_pos,
        Catagory_Name,
        Catagory_Description
    FROM Numbered
)
SELECT
    MAX(CASE WHEN col_pos = 0 THEN Catagory_Name END) AS Col1,
    MAX(CASE WHEN col_pos = 1 THEN Catagory_Name END) AS Col2,
    MAX(CASE WHEN col_pos = 2 THEN Catagory_Name END) AS Col3,
    MAX(CASE WHEN col_pos = 0 THEN Catagory_Description END) AS Des1,
    MAX(CASE WHEN col_pos = 1 THEN Catagory_Description END) AS Des2,
    MAX(CASE WHEN col_pos = 2 THEN Catagory_Description END) AS Des3
FROM Grouped
GROUP BY group_id
ORDER BY group_id;
