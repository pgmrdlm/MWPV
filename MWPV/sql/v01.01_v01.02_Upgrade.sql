/* ============================================================================
   MWPV - 01.01 -> 01.02 UPGRADE
   SQL-only migration for BankCards freeform card-type support.

   Purpose:
   - Preserve existing credit_cards combo rows, including VIRTUAL_CARD.
   - Add a new FREEFORM combo row for BankCards so the UI can move to a
     dedicated freeform option without changing the meaning of existing data.

   Assumptions:
   - Existing 01.01 schema already contains ComboType, ComboDetail, and DbVersion.
   - BankCards.BC_CardType stores ComboDetailId values for the credit_cards combo.
   - Existing rows that point at VIRTUAL_CARD should keep their current meaning.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

UPDATE ComboDetail
SET Seq = 99,
    Description = 'Freeform',
    Active = 1,
    UpdatedUtc = datetime('now')
WHERE ComboTypeId = (
        SELECT ComboTypeId
        FROM ComboType
        WHERE Code = 'credit_cards'
    )
  AND Code = 'FREEFORM';

INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT
    ct.ComboTypeId,
    99,
    'FREEFORM',
    'Freeform',
    1
FROM ComboType ct
WHERE ct.Code = 'credit_cards'
  AND NOT EXISTS (
      SELECT 1
      FROM ComboDetail cd
      WHERE cd.ComboTypeId = ct.ComboTypeId
        AND cd.Code = 'FREEFORM'
  );

UPDATE DbVersion
SET IsCurrent = CASE WHEN Version = '01.02' THEN 1 ELSE 0 END;

INSERT INTO DbVersion (
    Version,
    AppliedOn,
    Description,
    IsCurrent
)
SELECT
    '01.02',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.01 to 01.02',
    1
WHERE NOT EXISTS (
    SELECT 1
    FROM DbVersion
    WHERE Version = '01.02'
);

COMMIT;

PRAGMA foreign_keys = ON;
