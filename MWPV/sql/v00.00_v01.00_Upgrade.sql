/* ============================================================================
   MWPV - 00.00 -> 01.00 UPGRADE
   SQL-only migration for the lean CategoryItemAccounts redesign.

   Assumptions:
   - Existing 00.00 schema contains CategoryItemAccounts with the CIA_* columns.
   - CategoryItemAccounts exists in every target DB for this upgrade.
   - DbVersion exists and currently has no rows.
   - All migrated accounts are treated as active in 01.00.
   - Number remains stored as BLOB.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

DROP TABLE IF EXISTS CategoryItemAccounts_0100;

CREATE TABLE CategoryItemAccounts_0100 (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId     INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    Label      TEXT,
    Number     BLOB    NOT NULL,
    CreatedAt  INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    UpdatedAt  INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    IsActive   INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0,1))
);

INSERT INTO CategoryItemAccounts_0100 (
    Id,
    ItemId,
    Label,
    Number,
    CreatedAt,
    UpdatedAt,
    IsActive
)
SELECT
    CIA_Id,
    CIA_ItemId,
    CIA_AccountLabel,
    CIA_AccountNumber,
    CIA_CreatedAt,
    CIA_UpdatedAt,
    1
FROM CategoryItemAccounts;

DROP TABLE CategoryItemAccounts;

ALTER TABLE CategoryItemAccounts_0100
RENAME TO CategoryItemAccounts;

DROP TABLE IF EXISTS KeyArchiveIntegrity;

DELETE FROM DbVersion
WHERE Version = '01.00';

UPDATE DbVersion
SET IsCurrent = 0;

INSERT INTO DbVersion (
    Version,
    AppliedOn,
    Description,
    IsCurrent
)
VALUES (
    '01.00',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Upgrade 00.00 to 01.00',
    1
);

COMMIT;

PRAGMA foreign_keys = ON;