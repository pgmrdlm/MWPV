/* ============================================================================
   MWPV - MASTER DDL (FULL REWRITE WITH SEEDS)  -- v2025-10-16b
   Fix: seed inserts now use UNION ALL SELECT blocks (no VALUES(...) alias).
   Change in this revision: add back ComboType 'log_filters' + ComboDetail rows.
   This edition: REMOVED CREATE TABLE for CategoryItemSecurityQuestions, BankCards, CategoryItemAccounts.
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

-- Drops
DROP VIEW  IF EXISTS vw_CurrentPassword;
DROP VIEW  IF EXISTS vw_CurrentPin;

DROP TABLE IF EXISTS ComboDetail;
DROP TABLE IF EXISTS ComboType;
DROP TABLE IF EXISTS CategoryItemPasswordHistory;
DROP TABLE IF EXISTS CategoryItemSecurityQuestions;
DROP TABLE IF EXISTS CategoryItemAccounts;
DROP TABLE IF EXISTS BankCards;
DROP TABLE IF EXISTS CategoryItem;
DROP TABLE IF EXISTS Category;
DROP TABLE IF EXISTS KeyArchiveIntegrity;
DROP TABLE IF EXISTS DbVersion;
DROP TABLE IF EXISTS Logs;

DROP TABLE IF EXISTS CategoryItemPinHistory;

COMMIT;
PRAGMA foreign_keys = ON;

BEGIN TRANSACTION;

-- Lookups
CREATE TABLE ComboType (
    ComboTypeId   INTEGER  PRIMARY KEY AUTOINCREMENT,
    Code          TEXT     NOT NULL UNIQUE,
    Description   TEXT     NOT NULL,
    Active        INTEGER  NOT NULL DEFAULT 1 CHECK (Active IN (0,1)),
    CreatedUtc    TEXT     NOT NULL DEFAULT (datetime('now')),
    UpdatedUtc    TEXT     NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE ComboDetail (
    ComboDetailId   INTEGER  PRIMARY KEY AUTOINCREMENT,
    ComboTypeId     INTEGER  NOT NULL REFERENCES ComboType (ComboTypeId) ON DELETE CASCADE,
    Seq             INTEGER  NOT NULL,
    Code            TEXT     NOT NULL,
    Description     TEXT     NOT NULL,
    Active          INTEGER  NOT NULL DEFAULT 1 CHECK (Active IN (0,1)),
    CreatedUtc      TEXT     NOT NULL DEFAULT (datetime('now')),
    UpdatedUtc      TEXT     NOT NULL DEFAULT (datetime('now'))
);

-- Categories
CREATE TABLE Category (
    Category_Key         INTEGER PRIMARY KEY AUTOINCREMENT,
    Category_Name        TEXT    NOT NULL UNIQUE,
    Category_Description TEXT,
    Category_Type        INTEGER NOT NULL REFERENCES ComboDetail (ComboDetailId),
    CreatedUtc           TEXT    NOT NULL DEFAULT (STRFTIME('%Y-%m-%dT%H:%M:%fZ','now')),
    IsActive             INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0,1))
);

-- CategoryItem (+ added CI_* fields)
CREATE TABLE CategoryItem (
    ItemId                   INTEGER PRIMARY KEY AUTOINCREMENT,
    Category_Key             INTEGER NOT NULL REFERENCES Category (Category_Key) ON DELETE CASCADE,
    CI_Name                  TEXT    NOT NULL,
    CI_Description           TEXT,
    CI_Notes                 TEXT,
    CI_Username              TEXT,
    CI_SignInUrl             TEXT,
    CI_AccountEmail          BLOB,
    CI_AccountPhoneNumber    BLOB,
    CI_MFAType               TEXT,
    CI_MFABackupCodes        BLOB,
    CI_SecretMeta            BLOB,
    CI_SecretData            BLOB,  -- Primary Bank card type(credit/debit/store), card #, exp date, csv, pin #
                                    -- Account number, other payment types(bank account)
    CI_SecretStorage         TEXT    NOT NULL DEFAULT '0' CHECK (CI_SecretStorage IN ('0','1','2')),
    CI_CreateUTC             INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    CI_UpdateUTC             INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    IsActive                 INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0,1)),
    CI_NbrSecurityQuestions  INTEGER DEFAULT 0,
    CHECK (length(trim(CI_Name)) > 0),
    UNIQUE (Category_Key, CI_Name COLLATE NOCASE)
);

-- Encrypted detail siblings
CREATE TABLE CategoryItemPasswordHistory (
    CIPaH_PwHistId    INTEGER PRIMARY KEY AUTOINCREMENT,
    CIPaH_ItemId      INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    CIPaH_CreatedAt   INTEGER NOT NULL,
    CIPaH_Version     INTEGER NOT NULL DEFAULT 1,
    CIPaH_Password    BLOB    NOT NULL,
    CIPaH_PadLen      INTEGER,
    CIPaH_PwSig       BLOB    NOT NULL,
    CIPaH_SigVersion  INTEGER NOT NULL DEFAULT 1
);

/* NOTE:
   The following child tables were intentionally removed in this edition:
   - CategoryItemSecurityQuestions
   - BankCards
   - CategoryItemAccounts
*/

-- Integrity / versions / logs
CREATE TABLE KeyArchiveIntegrity (
    Kai_Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    Kai_SizeBytes    INTEGER NOT NULL,
    Kai_Sha256Hex    TEXT    NOT NULL,
    Kai_RecordedUtc  TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now'))
);

CREATE TABLE DbVersion (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Version     TEXT    NOT NULL,
    AppliedOn   TEXT    NOT NULL,
    Description TEXT,
    IsCurrent   INTEGER NOT NULL CHECK (IsCurrent IN (0, 1))
);

CREATE TABLE Logs (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    WhenUtc       TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now')),
    CreatedUtc    TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now')),
    Level         TEXT    NOT NULL CHECK (UPPER(Level) IN ('TRACE','DEBUG','INFO','WARN','WARNING','ERROR','FATAL')),
    Source        TEXT,
    EventCode     TEXT,
    SessionId     TEXT    NOT NULL DEFAULT '',
    LoginId       TEXT,
    ItemId        INTEGER,
    MachineId     TEXT,
    DeviceMake    TEXT,
    DeviceModel   TEXT,
    OSVersion     TEXT,
    DeviceIdHash  TEXT,
    InstallType   TEXT,
    AppVersion    TEXT    NOT NULL DEFAULT '',
    IsCrash       INTEGER NOT NULL DEFAULT 0 CHECK (IsCrash IN (0,1)),
    Payload       BLOB,
    PayloadFmt    TEXT,
    PayloadVer    INTEGER NOT NULL DEFAULT 1,
    KeySetVersion INTEGER NOT NULL DEFAULT 1,
    StackHash     TEXT
);

COMMIT;

-- =========================
-- SEEDS (idempotent)
-- =========================
BEGIN TRANSACTION;

-- Ensure ComboType: category_types
INSERT INTO ComboType (Code, Description, Active)
SELECT 'category_types', 'Category Types', 1
WHERE NOT EXISTS (SELECT 1 FROM ComboType WHERE Code = 'category_types');

-- Ensure ComboDetail for category_types (UNION ALL pattern)
WITH ct AS (SELECT ComboTypeId AS Id FROM ComboType WHERE Code = 'category_types'),
vals AS (
  SELECT 10  AS Seq, 'UTILITIES'     AS Code, 'Utilities'                 AS Description UNION ALL
  SELECT 20,         'GOVERNMENT',            'Government'                           UNION ALL
  SELECT 30,         'BANKS',                 'Banks / Credit Unions'                UNION ALL
  SELECT 40,         'SHOPPING',              'Shopping & Retail'                    UNION ALL
  SELECT 50,         'ENTERTAINMENT',         'Entertainment / Streaming'            UNION ALL
  SELECT 60,         'HEALTHCARE',            'Healthcare & Medical'                 UNION ALL
  SELECT 70,         'INSURANCE',             'Insurance'                            UNION ALL
  SELECT 80,         'SOCIAL/CLOUD',          'Social / Messaging / CLOUD'           UNION ALL
  SELECT 90,         'EMAIL',                 'Email & Identity'                     UNION ALL
  SELECT 100,        'APP/FILE/FOLDER',       'Application / Encrypted File or Folder'       UNION ALL
  SELECT 900,        'MISC',                  'Misc / Other'
)
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT ct.Id, v.Seq, v.Code, v.Description, 1
FROM ct CROSS JOIN vals v
WHERE NOT EXISTS (
  SELECT 1 FROM ComboDetail d
  WHERE d.ComboTypeId = ct.Id AND d.Code = v.Code
);

-- *** NEW in this revision: Ensure ComboType/ComboDetail for LOG FILTERS ***

-- Ensure ComboType: log_filters
INSERT INTO ComboType (Code, Description, Active)
SELECT 'log_filters', 'Filters for the Logs UI', 1
WHERE NOT EXISTS (SELECT 1 FROM ComboType WHERE Code = 'log_filters');

-- Ensure ComboDetail for log_filters (mirrors original list)
WITH ct AS (SELECT ComboTypeId AS Id FROM ComboType WHERE Code = 'log_filters'),
vals AS (
  SELECT 0 AS Seq, 'CATEGORY_DUPLICATE' AS Code, 'Duplicate category detected'      AS Description UNION ALL
  SELECT 1,        'CATEGORY_INSERTED',           'Category successfully inserted'                    UNION ALL
  SELECT 2,        'LOGIN',                       'Login events'                                     UNION ALL
  SELECT 3,        'EARLY_FAIL',                  'Early-fail events'                                UNION ALL
  SELECT 4,        'SESSION_START',               'Session started (post-login)'                      UNION ALL
  SELECT 5,        'SESSION_END',                 'Session ended'
)
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT ct.Id, v.Seq, v.Code, v.Description, 1
FROM ct CROSS JOIN vals v
WHERE NOT EXISTS (
  SELECT 1 FROM ComboDetail d
  WHERE d.ComboTypeId = ct.Id AND d.Code = v.Code
);

-- Starter Categories (unchanged)
INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Utilities', 'Bills and essential services', d.ComboDetailId, 1
FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId = d.ComboTypeId
WHERE t.Code = 'category_types' AND d.Code = 'UTILITIES'
  AND NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Utilities');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Government', 'Government portals & services', d.ComboDetailId, 1
FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId = d.ComboTypeId
WHERE t.Code = 'category_types' AND d.Code = 'GOVERNMENT'
  AND NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Government');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Banks', 'Banks & credit unions', d.ComboDetailId, 1
FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId = d.ComboTypeId
WHERE t.Code = 'category_types' AND d.Code = 'BANKS'
  AND NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Banks');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Shopping', 'Retail & e-commerce', d.ComboDetailId, 1
FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId = d.ComboTypeId
WHERE t.Code = 'category_types' AND d.Code = 'SHOPPING'
  AND NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Shopping');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Entertainment', 'Streaming & media', d.ComboDetailId, 1
FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId = d.ComboTypeId
WHERE t.Code = 'category_types' AND d.Code = 'ENTERTAINMENT'
  AND NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Entertainment');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Healthcare', 'Health & medical portals', d.ComboDetailId, 1
FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId = d.ComboTypeId
WHERE t.Code = 'category_types' AND d.Code = 'HEALTHCARE'
  AND NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Healthcare');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Insurance', 'Insurance accounts', d.ComboDetailId, 1
FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId = d.ComboTypeId
WHERE t.Code = 'category_types' AND d.Code = 'INSURANCE'
  AND NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Insurance');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Social', 'Social networks & messaging', d.ComboDetailId, 1
FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId = d.ComboTypeId
WHERE t.Code = 'category_types' AND d.Code = 'SOCIAL'
  AND NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Social');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Email', 'Email providers & identity', d.ComboDetailId, 1
FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId = d.ComboTypeId
WHERE t.Code = 'category_types' AND d.Code = 'EMAIL'
  AND NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Email');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Cloud', 'Cloud & hosting', d.ComboDetailId, 1
FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId = d.ComboTypeId
WHERE t.Code = 'category_types' AND d.Code = 'CLOUD'
  AND NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Cloud');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Development', 'Dev tools & repos', d.ComboDetailId, 1
FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId = d.ComboTypeId
WHERE t.Code = 'category_types' AND d.Code = 'DEVELOPMENT'
  AND NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Development');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Education', 'Schools, courses, LMS', d.ComboDetailId, 1
FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId = d.ComboTypeId
WHERE t.Code = 'category_types' AND d.Code = 'EDUCATION'
  AND NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Education');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Travel', 'Airlines, hotels, transport', d.ComboDetailId, 1
FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId = d.ComboTypeId
WHERE t.Code = 'category_types' AND d.Code = 'TRAVEL'
  AND NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Travel');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Misc', 'Everything else', d.ComboDetailId, 1
FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId = d.ComboTypeId
WHERE t.Code = 'category_types' AND d.Code = 'MISC'
  AND NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Misc');

COMMIT;
