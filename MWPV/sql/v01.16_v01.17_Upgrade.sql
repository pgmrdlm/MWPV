/* ============================================================================
   MWPV - 01.16 -> 01.17 UPGRADE
   Editable App Settings columns.

   Purpose:
   - Add controlled user-editable AppSettings columns used by the App Settings
     screen.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

ALTER TABLE AppSettings
ADD COLUMN AS_PW_IncludeSymbols INTEGER NOT NULL DEFAULT 1 CHECK (AS_PW_IncludeSymbols IN (0,1));

ALTER TABLE AppSettings
ADD COLUMN AS_LogRetentionDays INTEGER NOT NULL DEFAULT 30 CHECK (AS_LogRetentionDays >= 30);

ALTER TABLE AppSettings
ADD COLUMN AS_BackupRetentionCount INTEGER NOT NULL DEFAULT 5 CHECK (AS_BackupRetentionCount >= 5);

UPDATE AppSettings
SET AS_PW_IncludeSymbols = 1
WHERE AS_PW_IncludeSymbols IS NULL
   OR AS_PW_IncludeSymbols NOT IN (0,1);

UPDATE AppSettings
SET AS_LogRetentionDays = 30
WHERE AS_LogRetentionDays IS NULL
   OR AS_LogRetentionDays < 30;

UPDATE AppSettings
SET AS_BackupRetentionCount = 5
WHERE AS_BackupRetentionCount IS NULL
   OR AS_BackupRetentionCount < 5;

UPDATE DbVersion
SET IsCurrent = 0;

INSERT INTO DbVersion (
    Version,
    AppliedOn,
    Description,
    IsCurrent
)
SELECT
    '01.17',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.16 to 01.17',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.17'
);

UPDATE DbVersion
SET IsCurrent = CASE WHEN Version = '01.17' THEN 1 ELSE 0 END;

COMMIT;

PRAGMA foreign_keys = ON;
