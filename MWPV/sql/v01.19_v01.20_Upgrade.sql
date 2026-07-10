/* ============================================================================
   MWPV - 01.19 -> 01.20 UPGRADE
   App Settings Logs filter.

   Purpose:
   - Add the App Settings Logs filter for all existing AppSettings event codes.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT
    ct.ComboTypeId,
    21,
    'APP_SETTINGS',
    'App Settings',
    1
FROM ComboType ct
WHERE ct.Code = 'log_filters'
  AND NOT EXISTS (
      SELECT 1
      FROM ComboDetail d
      WHERE d.ComboTypeId = ct.ComboTypeId
        AND d.Code = 'APP_SETTINGS'
  );

UPDATE ComboDetail
SET Seq = 21,
    Description = 'App Settings',
    Active = 1,
    UpdatedUtc = datetime('now')
WHERE ComboTypeId = (
        SELECT ComboTypeId
        FROM ComboType
        WHERE Code = 'log_filters'
        LIMIT 1
    )
  AND Code = 'APP_SETTINGS';

UPDATE DbVersion
SET IsCurrent = 0;

INSERT INTO DbVersion (
    Version,
    AppliedOn,
    Description,
    IsCurrent
)
SELECT
    '01.20',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.19 to 01.20',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.20'
);

UPDATE DbVersion
SET IsCurrent = CASE WHEN Version = '01.20' THEN 1 ELSE 0 END;

COMMIT;

PRAGMA foreign_keys = ON;
