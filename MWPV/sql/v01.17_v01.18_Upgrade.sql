/* ============================================================================
   MWPV - 01.17 -> 01.18 UPGRADE
   AppSettings change logging templates.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT 'AppSettings', 1, 'App settings were updated: #ChangeDescription#.', 1
WHERE NOT EXISTS (
    SELECT 1
    FROM LogMessageTemplate
    WHERE UpdateForm = 'AppSettings'
      AND Seq = 1
);

UPDATE LogMessageTemplate
SET LogMessage = 'App settings were updated: #ChangeDescription#.',
    Active = 1,
    UpdatedUtc = datetime('now')
WHERE UpdateForm = 'AppSettings'
  AND Seq = 1;

INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT 'AppSettings', 2, 'App settings were reset to default: #ChangeDescription#.', 1
WHERE NOT EXISTS (
    SELECT 1
    FROM LogMessageTemplate
    WHERE UpdateForm = 'AppSettings'
      AND Seq = 2
);

UPDATE LogMessageTemplate
SET LogMessage = 'App settings were reset to default: #ChangeDescription#.',
    Active = 1,
    UpdatedUtc = datetime('now')
WHERE UpdateForm = 'AppSettings'
  AND Seq = 2;

INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT 'AppSettings', 3, 'All app settings were reset to their default values.', 1
WHERE NOT EXISTS (
    SELECT 1
    FROM LogMessageTemplate
    WHERE UpdateForm = 'AppSettings'
      AND Seq = 3
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
    '01.18',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.17 to 01.18',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.18'
);

UPDATE DbVersion
SET IsCurrent = CASE WHEN Version = '01.18' THEN 1 ELSE 0 END;

COMMIT;

PRAGMA foreign_keys = ON;
