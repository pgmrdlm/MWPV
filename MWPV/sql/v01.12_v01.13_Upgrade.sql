/* ============================================================================
   MWPV - 01.12 -> 01.13 UPGRADE
   Category logging templates and log filter values.

   Purpose:
   - Align CategoryUpdates templates with one-row category create/edit logs.
   - Add category-created/category-updated log filter rows.
   - Deactivate prior split category templates/filter rows if present.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

UPDATE LogMessageTemplate
SET LogMessage = CASE Seq
        WHEN 1 THEN 'Category #CategoryName# has been created.'
        WHEN 2 THEN 'Category #CategoryName# was updated: #ChangeSummary#.'
        ELSE LogMessage
    END,
    Active = 1,
    UpdatedUtc = datetime('now')
WHERE UpdateForm = 'CategoryUpdates'
  AND Seq IN (1, 2);

UPDATE LogMessageTemplate
SET Active = 0,
    UpdatedUtc = datetime('now')
WHERE UpdateForm = 'CategoryUpdates'
  AND Seq IN (3, 4, 5);

INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT v.UpdateForm, v.Seq, v.LogMessage, 1
FROM (
    SELECT 'CategoryUpdates' AS UpdateForm,
           1 AS Seq,
           'Category #CategoryName# has been created.' AS LogMessage
    UNION ALL SELECT 'CategoryUpdates', 2, 'Category #CategoryName# was updated: #ChangeSummary#.'
) AS v
WHERE NOT EXISTS (
    SELECT 1
    FROM LogMessageTemplate t
    WHERE t.UpdateForm = v.UpdateForm
      AND t.Seq        = v.Seq
);

WITH v(Seq, Code, Description) AS (
    VALUES
      (1, 'CATEGORY_CREATED', 'Category created'),
      (20, 'CATEGORY_UPDATED', 'Category updated')
)
UPDATE ComboDetail
SET Seq = (
        SELECT v.Seq
        FROM v
        WHERE v.Code = ComboDetail.Code
    ),
    Description = (
        SELECT v.Description
        FROM v
        WHERE v.Code = ComboDetail.Code
    ),
    Active = 1,
    UpdatedUtc = datetime('now')
WHERE ComboTypeId = (
        SELECT ComboTypeId
        FROM ComboType
        WHERE Code = 'log_filters'
        LIMIT 1
    )
  AND Code IN (
        SELECT Code
        FROM v
    );

WITH v(Seq, Code, Description) AS (
    VALUES
      (1, 'CATEGORY_CREATED', 'Category created'),
      (20, 'CATEGORY_UPDATED', 'Category updated')
)
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT
    ct.ComboTypeId,
    v.Seq,
    v.Code,
    v.Description,
    1
FROM ComboType ct
CROSS JOIN v
WHERE ct.Code = 'log_filters'
  AND NOT EXISTS (
      SELECT 1
      FROM ComboDetail d
      WHERE d.ComboTypeId = ct.ComboTypeId
        AND d.Code        = v.Code
  );

UPDATE ComboDetail
SET Active = 0,
    UpdatedUtc = datetime('now')
WHERE ComboTypeId = (
        SELECT ComboTypeId
        FROM ComboType
        WHERE Code = 'log_filters'
        LIMIT 1
    )
  AND Code IN (
        'CATEGORY_RENAMED',
        'CATEGORY_DESCRIPTION_UPDATED',
        'CATEGORY_DEACTIVATED',
        'CATEGORY_ACTIVATED'
    );

UPDATE DbVersion
SET IsCurrent = 0;

INSERT INTO DbVersion (
    Version,
    AppliedOn,
    Description,
    IsCurrent
)
SELECT
    '01.13',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.12 to 01.13',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.13'
);

UPDATE DbVersion
SET IsCurrent = CASE WHEN Version = '01.13' THEN 1 ELSE 0 END;

COMMIT;

PRAGMA foreign_keys = ON;
