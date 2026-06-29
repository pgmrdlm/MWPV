/* ============================================================================
   MWPV - 01.09 -> 01.10 UPGRADE
   DisplayCategoriesWithItems application setting.

   Purpose:
   - Add AppSettings option for category grid filtering.

   Assumptions:
   - Existing 01.09 schema already contains AppSettings and DbVersion.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

ALTER TABLE AppSettings
ADD COLUMN AS_DisplayCategoriesWithItems INTEGER NOT NULL DEFAULT 1 CHECK (AS_DisplayCategoriesWithItems IN (0,1));

UPDATE DbVersion
SET IsCurrent = 0;

INSERT INTO DbVersion (
    Version,
    AppliedOn,
    Description,
    IsCurrent
)
SELECT
    '01.10',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.09 to 01.10',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.10'
);

UPDATE DbVersion
SET IsCurrent = CASE WHEN Version = '01.10' THEN 1 ELSE 0 END;

COMMIT;

PRAGMA foreign_keys = ON;
