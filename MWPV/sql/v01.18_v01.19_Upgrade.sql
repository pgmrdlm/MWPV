/* ============================================================================
   MWPV - 01.18 -> 01.19 UPGRADE
   AppSettings summary logging templates.

   Purpose:
   - Update AppSettings logging templates to summarize changes per Save action.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

UPDATE LogMessageTemplate
SET LogMessage = 'App settings were updated: #ChangeDescription#.',
    Active = 1,
    UpdatedUtc = datetime('now')
WHERE UpdateForm = 'AppSettings'
  AND Seq = 1;

UPDATE LogMessageTemplate
SET LogMessage = 'App settings were reset to default: #ChangeDescription#.',
    Active = 1,
    UpdatedUtc = datetime('now')
WHERE UpdateForm = 'AppSettings'
  AND Seq = 2;

UPDATE LogMessageTemplate
SET LogMessage = 'All app settings were reset to their default values.',
    Active = 1,
    UpdatedUtc = datetime('now')
WHERE UpdateForm = 'AppSettings'
  AND Seq = 3;

UPDATE DbVersion
SET IsCurrent = 0;

INSERT INTO DbVersion (
    Version,
    AppliedOn,
    Description,
    IsCurrent
)
SELECT
    '01.19',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.18 to 01.19',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.19'
);

UPDATE DbVersion
SET IsCurrent = CASE WHEN Version = '01.19' THEN 1 ELSE 0 END;

COMMIT;

PRAGMA foreign_keys = ON;
