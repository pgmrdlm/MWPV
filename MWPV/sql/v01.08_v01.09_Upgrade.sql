/* ============================================================================
   MWPV - 01.08 -> 01.09 UPGRADE
   Security Questions database foundation.

   Purpose:
   - Add soft-deactivation support for CategoryItemSecurityQuestions.
   - Add SecurityQuestionsTab LogMessageTemplate rows.
   - Add Security Questions event codes to the Logs UI filter family.

   Assumptions:
   - Existing 01.08 schema already contains CategoryItemSecurityQuestions,
     LogMessageTemplate, ComboType, ComboDetail, and DbVersion.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

ALTER TABLE CategoryItemSecurityQuestions
ADD COLUMN CISQ_IsActive INTEGER NOT NULL DEFAULT 1 CHECK (CISQ_IsActive IN (0,1));

CREATE INDEX IF NOT EXISTS IX_CategoryItemSecurityQuestions_Item_Active_Seq
ON CategoryItemSecurityQuestions (CISQ_ItemId, CISQ_IsActive, CISQ_Seq);

INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT v.UpdateForm, v.Seq, v.LogMessage, 1
FROM (
    SELECT 'SecurityQuestionsTab' AS UpdateForm,
           1 AS Seq,
           'Security question has been created for #CategoryItemName#' AS LogMessage
    UNION ALL
    SELECT 'SecurityQuestionsTab',
           2,
           'Security question has been updated for #CategoryItemName#'
    UNION ALL
    SELECT 'SecurityQuestionsTab',
           3,
           'Security question has been deactivated for #CategoryItemName#'
) AS v
WHERE NOT EXISTS (
    SELECT 1
    FROM LogMessageTemplate t
    WHERE t.UpdateForm = v.UpdateForm
      AND t.Seq        = v.Seq
);

INSERT INTO ComboType (Code, Description, Active)
SELECT 'log_filters', 'Log filter values for the Logs UI', 1
WHERE NOT EXISTS (
    SELECT 1
    FROM ComboType
    WHERE Code = 'log_filters'
);

WITH v(Seq, Code, Description) AS (
    VALUES
      (17, 'SECURITYQUESTION_CREATED', 'Security question created'),
      (18, 'SECURITYQUESTION_CHANGED', 'Security question changed'),
      (19, 'SECURITYQUESTION_DEACTIVATED', 'Security question deactivated')
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
    '01.09',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.08 to 01.09',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.09'
);

UPDATE DbVersion
SET IsCurrent = CASE WHEN Version = '01.09' THEN 1 ELSE 0 END;

COMMIT;

PRAGMA foreign_keys = ON;
