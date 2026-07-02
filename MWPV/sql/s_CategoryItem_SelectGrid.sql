/*
    s_CategoryItem_SelectGrid.sql
    Inputs:
      @Category_Key  INTEGER  -- required: which category to show
    Behavior:
      - Orders deterministically by item name (case-insensitive)
      - Filters to active items (IsActive = 1 or NULL treated as 1)
      - Outputs 3 columns of item keys + names + descriptions
*/
WITH Numbered AS (
    SELECT
        ItemId,
        CI_Name,
        CI_Description,
        IsActive,
        ROW_NUMBER() OVER (ORDER BY CI_Name COLLATE NOCASE) - 1 AS rn
    FROM CategoryItem
    WHERE Category_Key = @Category_Key
      AND IFNULL(IsActive, 1) = 1
),
Grouped AS (
    SELECT
        (rn / 3) AS group_id,
        rn % 3 AS col_pos,
        ItemId,
        CI_Name,
        CI_Description,
        IsActive
    FROM Numbered
)
SELECT
    -- keys for click-through to details
    MAX(CASE WHEN col_pos = 0 THEN ItemId END)        AS Key1,
    MAX(CASE WHEN col_pos = 1 THEN ItemId END)        AS Key2,
    MAX(CASE WHEN col_pos = 2 THEN ItemId END)        AS Key3,

    -- display names
    MAX(CASE WHEN col_pos = 0 THEN CI_Name END)       AS Col1,
    MAX(CASE WHEN col_pos = 1 THEN CI_Name END)       AS Col2,
    MAX(CASE WHEN col_pos = 2 THEN CI_Name END)       AS Col3,

    -- tooltips / descriptions
    MAX(CASE WHEN col_pos = 0 THEN CI_Description END) AS Des1,
    MAX(CASE WHEN col_pos = 1 THEN CI_Description END) AS Des2,
    MAX(CASE WHEN col_pos = 2 THEN CI_Description END) AS Des3,

    -- active-state for visual styling
    MAX(CASE WHEN col_pos = 0 THEN IsActive END) AS Active1,
    MAX(CASE WHEN col_pos = 1 THEN IsActive END) AS Active2,
    MAX(CASE WHEN col_pos = 2 THEN IsActive END) AS Active3
FROM Grouped
GROUP BY group_id
ORDER BY group_id;
