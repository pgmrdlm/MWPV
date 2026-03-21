/* ============================================================================
   MWPV - 01.00 -> 01.01 UPGRADE
   SQL-only migration for lean CategoryItemAccounts account type persistence.

   Assumptions:
   - Existing 01.00 schema contains the lean CategoryItemAccounts table.
   - Existing CategoryItemAccounts rows do not yet store account type selections.
   - account_types combo rows may be missing or may contain older active values.
   - Existing account rows should be preserved with NULL account-type fields.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

DROP TABLE IF EXISTS CategoryItemAccounts_0101;

CREATE TABLE CategoryItemAccounts_0101 (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId              INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    Label               TEXT,
    Number              BLOB    NOT NULL,
    AccountTypeId       INTEGER REFERENCES ComboDetail (ComboDetailId),
    AccountTypeFreeform TEXT,
    CreatedAt           INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    UpdatedAt           INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    IsActive            INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0,1))
);

INSERT INTO CategoryItemAccounts_0101 (
    Id,
    ItemId,
    Label,
    Number,
    AccountTypeId,
    AccountTypeFreeform,
    CreatedAt,
    UpdatedAt,
    IsActive
)
SELECT
    Id,
    ItemId,
    Label,
    Number,
    NULL,
    NULL,
    CreatedAt,
    UpdatedAt,
    IsActive
FROM CategoryItemAccounts;

DROP TABLE CategoryItemAccounts;

ALTER TABLE CategoryItemAccounts_0101
RENAME TO CategoryItemAccounts;

INSERT INTO ComboType (Code, Description, Active)
SELECT 'account_types', 'Common financial account types', 1
WHERE NOT EXISTS (
    SELECT 1
    FROM ComboType
    WHERE Code = 'account_types'
);

UPDATE ComboDetail
SET Seq = 0,
    Description = 'Primary',
    Active = 1
WHERE ComboTypeId = (
        SELECT ComboTypeId
        FROM ComboType
        WHERE Code = 'account_types'
    )
  AND Code = 'PRIMARY';

UPDATE ComboDetail
SET Seq = 1,
    Description = 'Checking',
    Active = 1
WHERE ComboTypeId = (
        SELECT ComboTypeId
        FROM ComboType
        WHERE Code = 'account_types'
    )
  AND Code = 'CHECKING';

UPDATE ComboDetail
SET Seq = 2,
    Description = 'Savings',
    Active = 1
WHERE ComboTypeId = (
        SELECT ComboTypeId
        FROM ComboType
        WHERE Code = 'account_types'
    )
  AND Code = 'SAVINGS';

UPDATE ComboDetail
SET Seq = 3,
    Description = 'Christmas Savings',
    Active = 1
WHERE ComboTypeId = (
        SELECT ComboTypeId
        FROM ComboType
        WHERE Code = 'account_types'
    )
  AND Code = 'CHRISTMAS_SAVINGS';

UPDATE ComboDetail
SET Seq = 4,
    Description = 'IRA',
    Active = 1
WHERE ComboTypeId = (
        SELECT ComboTypeId
        FROM ComboType
        WHERE Code = 'account_types'
    )
  AND Code = 'IRA';

UPDATE ComboDetail
SET Seq = 5,
    Description = 'Loan',
    Active = 1
WHERE ComboTypeId = (
        SELECT ComboTypeId
        FROM ComboType
        WHERE Code = 'account_types'
    )
  AND Code = 'LOAN';

UPDATE ComboDetail
SET Seq = 6,
    Description = 'Mortgage',
    Active = 1
WHERE ComboTypeId = (
        SELECT ComboTypeId
        FROM ComboType
        WHERE Code = 'account_types'
    )
  AND Code = 'MORTGAGE';

UPDATE ComboDetail
SET Seq = 99,
    Description = 'Freeform',
    Active = 1
WHERE ComboTypeId = (
        SELECT ComboTypeId
        FROM ComboType
        WHERE Code = 'account_types'
    )
  AND Code = 'FREEFORM';

INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT
    ct.ComboTypeId,
    v.Seq,
    v.Code,
    v.Description,
    1
FROM ComboType ct
JOIN (
    SELECT 0  AS Seq, 'PRIMARY'            AS Code, 'Primary'            AS Description
    UNION ALL SELECT 1,  'CHECKING',           'Checking'
    UNION ALL SELECT 2,  'SAVINGS',            'Savings'
    UNION ALL SELECT 3,  'CHRISTMAS_SAVINGS',  'Christmas Savings'
    UNION ALL SELECT 4,  'IRA',                'IRA'
    UNION ALL SELECT 5,  'LOAN',               'Loan'
    UNION ALL SELECT 6,  'MORTGAGE',           'Mortgage'
    UNION ALL SELECT 99, 'FREEFORM',           'Freeform'
) AS v
WHERE ct.Code = 'account_types'
  AND NOT EXISTS (
      SELECT 1
      FROM ComboDetail cd
      WHERE cd.ComboTypeId = ct.ComboTypeId
        AND cd.Code        = v.Code
  );

UPDATE ComboDetail
SET Active = 0
WHERE ComboTypeId = (
        SELECT ComboTypeId
        FROM ComboType
        WHERE Code = 'account_types'
    )
  AND Code NOT IN (
        'PRIMARY',
        'CHECKING',
        'SAVINGS',
        'CHRISTMAS_SAVINGS',
        'IRA',
        'LOAN',
        'MORTGAGE',
        'FREEFORM'
  );

DELETE FROM DbVersion
WHERE Version = '01.01';

UPDATE DbVersion
SET IsCurrent = 0;

INSERT INTO DbVersion (
    Version,
    AppliedOn,
    Description,
    IsCurrent
)
VALUES (
    '01.01',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 01.00 to 01.01',
    1
);

COMMIT;

PRAGMA foreign_keys = ON;
