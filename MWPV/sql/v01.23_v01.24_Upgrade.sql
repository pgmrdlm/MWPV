/* ============================================================================
   MWPV - 01.23 -> 01.24 UPGRADE
   Add the trusted filter entry for successful session-log purge audit records.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT ct.ComboTypeId, 23, 'LOG_PURGE_COMPLETED', 'Session log purge completed', 1
FROM ComboType ct
WHERE ct.Code = 'log_filters'
  AND NOT EXISTS (
      SELECT 1 FROM ComboDetail cd
      WHERE cd.ComboTypeId = ct.ComboTypeId AND cd.Code = 'LOG_PURGE_COMPLETED'
  );

UPDATE DbVersion SET IsCurrent = 0;

INSERT INTO DbVersion (Version, AppliedOn, Description, IsCurrent)
SELECT '01.24', strftime('%Y-%m-%dT%H:%M:%SZ','now'), 'Upgrade 01.23 to 01.24', 1
WHERE NOT EXISTS (SELECT 1 FROM DbVersion WHERE Version = '01.24');

UPDATE DbVersion SET IsCurrent = CASE WHEN Version = '01.24' THEN 1 ELSE 0 END;

COMMIT;

PRAGMA foreign_keys = ON;
