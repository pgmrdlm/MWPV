/* ============================================================================
   MWPV - 01.20 -> 01.21 UPGRADE
   AppSettings backup-on-exit prompt option.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

ALTER TABLE AppSettings
ADD COLUMN AS_BackupPromptOnExitAfterChanges INTEGER NOT NULL DEFAULT 1
    CHECK (AS_BackupPromptOnExitAfterChanges IN (0,1));

UPDATE DbVersion
SET IsCurrent = 0;

INSERT INTO DbVersion (
    Version,
    AppliedOn,
    Description,
    IsCurrent
)
SELECT
    '01.21',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.20 to 01.21',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.21'
);

UPDATE DbVersion
SET IsCurrent = CASE WHEN Version = '01.21' THEN 1 ELSE 0 END;

COMMIT;

PRAGMA foreign_keys = ON;
