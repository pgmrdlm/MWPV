/*
    s_Category_SelectGrid.sql
    - 3-column layout of categories for the grid
    - Includes keys so the UI can route to CategoryItem grid
*/
WITH Numbered AS (
    SELECT
        Category_Key,
        Category_Name,
        Category_Description,
        IFNULL(IsActive, 1) AS IsActive,
        ROW_NUMBER() OVER (ORDER BY Category_Name COLLATE NOCASE) - 1 AS rn
    FROM Category
),
Grouped AS (
    SELECT
        (rn / 3) AS group_id,
        rn % 3 AS col_pos,
        Category_Key,
        Category_Name,
        Category_Description,
        IsActive
    FROM Numbered
)
SELECT
    MAX(CASE WHEN col_pos = 0 THEN Category_Key END)         AS Key1,
    MAX(CASE WHEN col_pos = 1 THEN Category_Key END)         AS Key2,
    MAX(CASE WHEN col_pos = 2 THEN Category_Key END)         AS Key3,

    MAX(CASE WHEN col_pos = 0 THEN Category_Name END)        AS Col1,
    MAX(CASE WHEN col_pos = 1 THEN Category_Name END)        AS Col2,
    MAX(CASE WHEN col_pos = 2 THEN Category_Name END)        AS Col3,

    MAX(CASE WHEN col_pos = 0 THEN Category_Description END) AS Des1,
    MAX(CASE WHEN col_pos = 1 THEN Category_Description END) AS Des2,
    MAX(CASE WHEN col_pos = 2 THEN Category_Description END) AS Des3,

    MAX(CASE WHEN col_pos = 0 THEN IsActive END)             AS Active1,
    MAX(CASE WHEN col_pos = 1 THEN IsActive END)             AS Active2,
    MAX(CASE WHEN col_pos = 2 THEN IsActive END)             AS Active3
FROM Grouped
GROUP BY group_id
ORDER BY group_id;
