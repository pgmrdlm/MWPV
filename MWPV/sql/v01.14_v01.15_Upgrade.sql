/* ============================================================================
   MWPV - 01.14 -> 01.15 UPGRADE
   BasicTab category reassignment and item-name logging templates.

   Purpose:
   - Add BasicTab category-change detail line.
   - Add BasicTab category item name-change detail line.
   - Keep one CATEGORYITEM_CHANGED log row per Basic save/action.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT 'BasicTab', 14, '- Category changed from #BeforeCategoryName# to #AfterCategoryName#', 1
WHERE NOT EXISTS (
    SELECT 1
    FROM LogMessageTemplate
    WHERE UpdateForm = 'BasicTab'
      AND Seq = 14
);

UPDATE LogMessageTemplate
SET LogMessage = '- Category changed from #BeforeCategoryName# to #AfterCategoryName#',
    Active = 1,
    UpdatedUtc = datetime('now')
WHERE UpdateForm = 'BasicTab'
  AND Seq = 14;

INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT 'BasicTab', 15, '- Category item name changed from #BeforeCategoryItemName# to #AfterCategoryItemName#', 1
WHERE NOT EXISTS (
    SELECT 1
    FROM LogMessageTemplate
    WHERE UpdateForm = 'BasicTab'
      AND Seq = 15
);

UPDATE LogMessageTemplate
SET LogMessage = '- Category item name changed from #BeforeCategoryItemName# to #AfterCategoryItemName#',
    Active = 1,
    UpdatedUtc = datetime('now')
WHERE UpdateForm = 'BasicTab'
  AND Seq = 15;

UPDATE DbVersion
SET IsCurrent = 0;

INSERT INTO DbVersion (
    Version,
    AppliedOn,
    Description,
    IsCurrent
)
SELECT
    '01.15',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.14 to 01.15',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.15'
);

UPDATE DbVersion
SET IsCurrent = CASE WHEN Version = '01.15' THEN 1 ELSE 0 END;

COMMIT;

PRAGMA foreign_keys = ON;
