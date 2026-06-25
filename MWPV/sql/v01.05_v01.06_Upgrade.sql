/* ============================================================================
   MWPV - 01.05 -> 01.06 UPGRADE
   SQL-only migration for Bank Card logging templates.

   Purpose:
   - Add BankCardsTab LogMessageTemplate rows.
   - Add Bank Card log filter values.

   Assumptions:
   - Existing 01.05 schema already contains LogMessageTemplate, ComboType,
     ComboDetail, and DbVersion.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT v.UpdateForm, v.Seq, v.LogMessage, 1
FROM (
    SELECT 'BankCardsTab' AS UpdateForm,
           1 AS Seq,
           'Bank card has been created for #CategoryItemName#' AS LogMessage
    UNION ALL
    SELECT 'BankCardsTab',
           2,
           'Bank card has been updated for #CategoryItemName#'
    UNION ALL
    SELECT 'BankCardsTab',
           3,
           'Bank card has been deactivated for #CategoryItemName#'
) AS v
WHERE NOT EXISTS (
    SELECT 1
    FROM LogMessageTemplate t
    WHERE t.UpdateForm = v.UpdateForm
      AND t.Seq        = v.Seq
);

WITH v(Seq, Code, Description) AS (
    VALUES
      (14, 'BANKCARD_CREATED',     'Bank card created'),
      (15, 'BANKCARD_CHANGED',     'Bank card changed'),
      (16, 'BANKCARD_DEACTIVATED', 'Bank card deactivated')
)
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT
    ct.ComboTypeId,
    v.Seq,
    v.Code,
    v.Description,
    1
FROM ComboType ct
CROSS JOIN v
WHERE ct.Code = 'log_filters'
  AND NOT EXISTS (
      SELECT 1
      FROM ComboDetail d
      WHERE d.ComboTypeId = ct.ComboTypeId
        AND d.Code        = v.Code
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
    '01.06',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.05 to 01.06',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.06'
);

UPDATE DbVersion
SET IsCurrent = CASE WHEN Version = '01.06' THEN 1 ELSE 0 END;

COMMIT;

PRAGMA foreign_keys = ON;
