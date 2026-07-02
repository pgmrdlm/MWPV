/* ============================================================================
   MWPV - 01.11 -> 01.12 UPGRADE
   Category edit/deactivate logging templates.

   Purpose:
   - Add CategoryUpdates templates for category update and deactivation.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT v.UpdateForm, v.Seq, v.LogMessage, 1
FROM (
    SELECT 'CategoryUpdates' AS UpdateForm,
           2 AS Seq,
           'Category #CategoryName# has been updated' AS LogMessage
    UNION ALL SELECT 'CategoryUpdates', 3, 'Category #CategoryName# has been deactivated'
) AS v
WHERE NOT EXISTS (
    SELECT 1
    FROM LogMessageTemplate t
    WHERE t.UpdateForm = v.UpdateForm
      AND t.Seq        = v.Seq
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
    '01.12',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.11 to 01.12',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.12'
);

UPDATE DbVersion
SET IsCurrent = CASE WHEN Version = '01.12' THEN 1 ELSE 0 END;

COMMIT;

PRAGMA foreign_keys = ON;
