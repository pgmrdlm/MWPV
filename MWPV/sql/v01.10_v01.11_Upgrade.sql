/* ============================================================================
   MWPV - 01.10 -> 01.11 UPGRADE
   Sensitive clipboard clear timer setting.

   Purpose:
   - Add AppSettings.SensitiveClipboardClearSeconds.
   - Default: 45 seconds; valid range: 5 to 300 seconds.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

ALTER TABLE AppSettings
ADD COLUMN SensitiveClipboardClearSeconds INTEGER NOT NULL DEFAULT 45 CHECK (SensitiveClipboardClearSeconds BETWEEN 5 AND 300);

UPDATE AppSettings
SET SensitiveClipboardClearSeconds = 45
WHERE SensitiveClipboardClearSeconds IS NULL
   OR SensitiveClipboardClearSeconds < 5
   OR SensitiveClipboardClearSeconds > 300;

UPDATE DbVersion
SET IsCurrent = 0;

INSERT INTO DbVersion (
    Version,
    AppliedOn,
    Description,
    IsCurrent
)
SELECT
    '01.11',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.10 to 01.11',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.11'
);

UPDATE DbVersion
SET IsCurrent = CASE WHEN Version = '01.11' THEN 1 ELSE 0 END;

COMMIT;

PRAGMA foreign_keys = ON;
