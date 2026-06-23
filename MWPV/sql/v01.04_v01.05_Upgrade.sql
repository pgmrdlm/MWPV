/* ============================================================================
   MWPV - 01.04 -> 01.05 UPGRADE
   SQL-only migration for application password length settings.

   Purpose:
   - Add AppSettings for user-defined password length settings.
   - Seed default password length settings.

   Assumptions:
   - Existing 01.04 schema already contains DbVersion.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

CREATE TABLE IF NOT EXISTS AppSettings (
    AS_PW_Minimum          INTEGER NOT NULL,
    AS_PW_Incriments       INTEGER NOT NULL,
    AS_PW_Inctriment_Steps INTEGER NOT NULL
);

INSERT INTO AppSettings (
    AS_PW_Minimum,
    AS_PW_Incriments,
    AS_PW_Inctriment_Steps
)
SELECT
    12,
    10,
    10
WHERE NOT EXISTS (SELECT 1 FROM AppSettings);

UPDATE DbVersion
SET IsCurrent = 0;

INSERT INTO DbVersion (
    Version,
    AppliedOn,
    Description,
    IsCurrent
)
SELECT
    '01.05',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.04 to 01.05',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.05'
);

UPDATE DbVersion
SET IsCurrent = CASE WHEN Version = '01.05' THEN 1 ELSE 0 END;

COMMIT;

PRAGMA foreign_keys = ON;
