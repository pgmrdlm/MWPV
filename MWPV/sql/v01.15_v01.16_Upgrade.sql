/* ============================================================================
   MWPV - 01.15 -> 01.16 UPGRADE
   BasicTab category item name-change logging template repair.

   Purpose:
   - Ensure databases already upgraded to 01.15 receive the dedicated
     Category Item name-change detail template.
   - Keep one CATEGORYITEM_CHANGED log row per Basic save/action.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

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
    '01.16',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.15 to 01.16',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.16'
);

UPDATE DbVersion
SET IsCurrent = CASE WHEN Version = '01.16' THEN 1 ELSE 0 END;

COMMIT;

PRAGMA foreign_keys = ON;
