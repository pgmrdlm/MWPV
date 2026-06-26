/* ============================================================================
   MWPV - 01.07 -> 01.08 UPGRADE
   Forward-only logging consistency cleanup.

   Purpose:
   - Repair/seed Logs UI event-code filters by ComboType.Code = 'log_filters'.
   - Add BankCardsTab changed-field LogMessageTemplate rows.
   - Replace the 01.07 BankCardsTab seq 5 summary-token template with the
     template-sequence heading used by 01.08 runtime logging.

   Assumptions:
   - Existing 01.07 schema already contains LogMessageTemplate, ComboType,
     ComboDetail, and DbVersion.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

-- Ensure the Logs UI filter family exists.
INSERT INTO ComboType (Code, Description, Active)
SELECT 'log_filters', 'Log filter values for the Logs UI', 1
WHERE NOT EXISTS (
    SELECT 1
    FROM ComboType
    WHERE Code = 'log_filters'
);

-- Ensure all real event codes used by the current runtime are filterable.
WITH v(Seq, Code, Description) AS (
    VALUES
      (0,  'CATEGORY_DUPLICATE',   'Duplicate category detected'),
      (1,  'CATEGORY_CREATED',     'Category successfully inserted'),
      (2,  'LOGIN',                'Login events'),
      (3,  'EARLY_FAIL',           'Early-fail events'),
      (4,  'SESSION_START',        'Session started (post-login)'),
      (5,  'SESSION_END',          'Session ended'),
      (10, 'CATEGORYITEM_CREATED', 'Category item created'),
      (11, 'CATEGORYITEM_CHANGED', 'Category item updated (one or more fields)'),
      (12, 'ACCOUNTS_CREATED',     'Accounts Created'),
      (13, 'ACCOUNTS_CHANGED',     'Accounts Changed'),
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

-- Replace the 01.07 token-summary template with the 01.08 heading template.
UPDATE LogMessageTemplate
SET LogMessage = 'Bank card #BankCardDisplayName# has been updated for #CategoryItemName#'
WHERE UpdateForm = 'BankCardsTab'
  AND Seq = 5
  AND LogMessage LIKE '%#BankCardChangeSummary#%';

-- Insert any missing BankCardsTab templates needed by the 01.08 runtime.
INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT v.UpdateForm, v.Seq, v.LogMessage, 1
FROM (
    SELECT 'BankCardsTab' AS UpdateForm,
           4 AS Seq,
           'Bank card #BankCardDisplayName# has been created for #CategoryItemName#' AS LogMessage
    UNION ALL
    SELECT 'BankCardsTab',
           5,
           'Bank card #BankCardDisplayName# has been updated for #CategoryItemName#'
    UNION ALL
    SELECT 'BankCardsTab',
           6,
           'Bank card #BankCardDisplayName# has been deactivated for #CategoryItemName#'
    UNION ALL
    SELECT 'BankCardsTab',
           7,
           '- Card type changed'
    UNION ALL
    SELECT 'BankCardsTab',
           8,
           '- Cardholder changed'
    UNION ALL
    SELECT 'BankCardsTab',
           9,
           '- Expiration changed'
    UNION ALL
    SELECT 'BankCardsTab',
           10,
           '- Active state changed'
    UNION ALL
    SELECT 'BankCardsTab',
           11,
           '- Card number changed'
    UNION ALL
    SELECT 'BankCardsTab',
           12,
           '- CVV changed'
    UNION ALL
    SELECT 'BankCardsTab',
           13,
           '- PIN changed'
    UNION ALL
    SELECT 'BankCardsTab',
           14,
           '- Billing ZIP changed'
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
    '01.08',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.07 to 01.08',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.08'
);

UPDATE DbVersion
SET IsCurrent = CASE WHEN Version = '01.08' THEN 1 ELSE 0 END;

COMMIT;

PRAGMA foreign_keys = ON;
