/* ============================================================================
   MWPV - 01.02 -> 01.03 UPGRADE
   SQL-only migration for Accounts logging seed support.

   Purpose:
   - Add AccountsTab LogMessageTemplate rows for account creation and deactivation.
   - Add matching log_filters ComboDetail rows so Accounts log events can be filtered in the Logs UI.

   Assumptions:
   - Existing 01.02 schema already contains LogMessageTemplate, ComboType, ComboDetail, and DbVersion.
   - The existing log_filters ComboType is the filter family used by the Logs UI.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT v.UpdateForm, v.Seq, v.LogMessage, 1
FROM (
    SELECT 'AccountsTab' AS UpdateForm,
           1 AS Seq,
           'Account #AccountTypeDisplay# (#AccountNumberMasked#) has been created for #CategoryItemName#' AS LogMessage
    UNION ALL
    SELECT 'AccountsTab',
           2,
           'Account #AccountTypeDisplay# (#AccountNumberMasked#) has been deactivated for #CategoryItemName#'
) AS v
WHERE NOT EXISTS (
    SELECT 1
    FROM LogMessageTemplate t
    WHERE t.UpdateForm = v.UpdateForm
      AND t.Seq        = v.Seq
);

WITH v(Seq, Code, Description) AS (
    VALUES
      (12, 'ACCOUNTS_CREATED',     'Accounts Created'),
      (13, 'ACCOUNTS_CHANGED', 'Accounts Changed')
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
SET IsCurrent = CASE WHEN Version = '01.03' THEN 1 ELSE 0 END;

INSERT INTO DbVersion (
    Version,
    AppliedOn,
    Description,
    IsCurrent
)
SELECT
    '01.03',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.02 to 01.03',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.03'
);

COMMIT;

PRAGMA foreign_keys = ON;
