/* ============================================================================
   MWPV - 01.03 -> 01.04 UPGRADE
   SQL-only migration for CategoryItem Basic deactivation logging template.

   Purpose:
   - Add BasicTab LogMessageTemplate row for CategoryItem deactivation.

   Assumptions:
   - Existing 01.03 schema already contains LogMessageTemplate and DbVersion.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT v.UpdateForm, v.Seq, v.LogMessage, 1
FROM (
    SELECT 'BasicTab' AS UpdateForm,
           12 AS Seq,
           '- Category Item #CategoryItemName# was deactivated' AS LogMessage
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
    '01.04',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.03 to 01.04',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.04'
);

UPDATE DbVersion
SET IsCurrent = CASE WHEN Version = '01.04' THEN 1 ELSE 0 END;

COMMIT;

PRAGMA foreign_keys = ON;
