/*
    s_CategorySelectWithActiveItems.sql
    - 3-column layout of active categories that contain at least one active item
    - Projects the same columns as s_CategorySelectAll.sql:
      Key1/Key2/Key3, Col1/Col2/Col3, Des1/Des2/Des3
*/
WITH Numbered AS (
    SELECT
        c.Category_Key,
        c.Category_Name,
        c.Category_Description,
        ROW_NUMBER() OVER (ORDER BY c.Category_Name COLLATE NOCASE) - 1 AS rn
    FROM Category c
    WHERE IFNULL(c.IsActive, 1) = 1
      AND EXISTS (
          SELECT 1
          FROM CategoryItem ci
          WHERE ci.Category_Key = c.Category_Key
            AND IFNULL(ci.IsActive, 1) = 1
      )
),
Grouped AS (
    SELECT
        (rn / 3) AS group_id,
        rn % 3 AS col_pos,
        Category_Key,
        Category_Name,
        Category_Description
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
    MAX(CASE WHEN col_pos = 2 THEN Category_Description END) AS Des3
FROM Grouped
GROUP BY group_id
ORDER BY group_id;
