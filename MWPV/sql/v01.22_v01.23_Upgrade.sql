/* ============================================================================
   MWPV - 01.22 -> 01.23 UPGRADE
   Replace the retired global password-symbol setting with inactivity timeout.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

CREATE TABLE AppSettings_01_23 (
    AS_PW_Minimum INTEGER NOT NULL,
    AS_PW_Incriments INTEGER NOT NULL,
    AS_PW_Inctriment_Steps INTEGER NOT NULL,
    AS_DisplayCategoriesWithItems INTEGER NOT NULL DEFAULT 1 CHECK (AS_DisplayCategoriesWithItems IN (0,1)),
    SensitiveClipboardClearSeconds INTEGER NOT NULL DEFAULT 45 CHECK (SensitiveClipboardClearSeconds BETWEEN 5 AND 300),
    AS_InactivityTimeoutMinutes INTEGER NOT NULL DEFAULT 4 CHECK (AS_InactivityTimeoutMinutes BETWEEN 1 AND 7),
    AS_LogRetentionDays INTEGER NOT NULL DEFAULT 30 CHECK (AS_LogRetentionDays >= 30),
    AS_BackupRetentionCount INTEGER NOT NULL DEFAULT 5 CHECK (AS_BackupRetentionCount >= 5),
    AS_BackupPromptOnExitAfterChanges INTEGER NOT NULL DEFAULT 1 CHECK (AS_BackupPromptOnExitAfterChanges IN (0,1))
);

INSERT INTO AppSettings_01_23 (
    AS_PW_Minimum,
    AS_PW_Incriments,
    AS_PW_Inctriment_Steps,
    AS_DisplayCategoriesWithItems,
    SensitiveClipboardClearSeconds,
    AS_InactivityTimeoutMinutes,
    AS_LogRetentionDays,
    AS_BackupRetentionCount,
    AS_BackupPromptOnExitAfterChanges
)
SELECT
    AS_PW_Minimum,
    AS_PW_Incriments,
    AS_PW_Inctriment_Steps,
    AS_DisplayCategoriesWithItems,
    SensitiveClipboardClearSeconds,
    4,
    AS_LogRetentionDays,
    AS_BackupRetentionCount,
    AS_BackupPromptOnExitAfterChanges
FROM AppSettings;

DROP TABLE AppSettings;
ALTER TABLE AppSettings_01_23 RENAME TO AppSettings;

UPDATE DbVersion
SET IsCurrent = 0;

INSERT INTO DbVersion (Version, AppliedOn, Description, IsCurrent)
SELECT
    '01.23',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.22 to 01.23',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.23'
);

UPDATE DbVersion
SET IsCurrent = CASE WHEN Version = '01.23' THEN 1 ELSE 0 END;

COMMIT;

PRAGMA foreign_keys = ON;
