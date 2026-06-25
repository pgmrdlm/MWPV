/* ============================================================================
   MWPV - 01.06 -> 01.07 UPGRADE
   SQL-only migration for enhanced Bank Card logging templates.

   Purpose:
   - Add enhanced BankCardsTab LogMessageTemplate rows.

   Assumptions:
   - Existing 01.06 schema already contains LogMessageTemplate and DbVersion.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT v.UpdateForm, v.Seq, v.LogMessage, 1
FROM (
    SELECT 'BankCardsTab' AS UpdateForm,
           4 AS Seq,
           'Bank card #BankCardDisplayName# has been created for #CategoryItemName#' AS LogMessage
    UNION ALL
    SELECT 'BankCardsTab',
           5,
           'Bank card #BankCardDisplayName# has been updated for #CategoryItemName#: #BankCardChangeSummary#'
    UNION ALL
    SELECT 'BankCardsTab',
           6,
           'Bank card #BankCardDisplayName# has been deactivated for #CategoryItemName#'
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
    '01.07',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.06 to 01.07',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.07'
);

UPDATE DbVersion
SET IsCurrent = CASE WHEN Version = '01.07' THEN 1 ELSE 0 END;

COMMIT;

PRAGMA foreign_keys = ON;
