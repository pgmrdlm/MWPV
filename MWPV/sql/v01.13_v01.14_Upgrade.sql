/* ============================================================================
   MWPV - 01.13 -> 01.14 UPGRADE
   Category item activation/deactivation logging templates.

   Purpose:
   - Ensure BasicTab category item status changes use separate detail lines.
   - Keep one CATEGORYITEM_CHANGED log row per save/action.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

UPDATE LogMessageTemplate
SET LogMessage = '- Category Item #CategoryItemName# was deactivated',
    Active = 1,
    UpdatedUtc = datetime('now')
WHERE UpdateForm = 'BasicTab'
  AND Seq = 12;

INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT 'BasicTab', 12, '- Category Item #CategoryItemName# was deactivated', 1
WHERE NOT EXISTS (
    SELECT 1
    FROM LogMessageTemplate
    WHERE UpdateForm = 'BasicTab'
      AND Seq = 12
);

UPDATE LogMessageTemplate
SET LogMessage = '- Category Item #CategoryItemName# was activated',
    Active = 1,
    UpdatedUtc = datetime('now')
WHERE UpdateForm = 'BasicTab'
  AND Seq = 13;

INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT 'BasicTab', 13, '- Category Item #CategoryItemName# was activated', 1
WHERE NOT EXISTS (
    SELECT 1
    FROM LogMessageTemplate
    WHERE UpdateForm = 'BasicTab'
      AND Seq = 13
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
    '01.14',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.13 to 01.14',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.14'
);

UPDATE DbVersion
SET IsCurrent = CASE WHEN Version = '01.14' THEN 1 ELSE 0 END;

COMMIT;

PRAGMA foreign_keys = ON;
