/* ============================================================================
   MWPV - FRESH LOAD / Nuke-and-Pave SCRIPT  (SQLite)
   ----------------------------------------------------------------------------
   ⚠️ DANGER: Drops and recreates schema. ALL EXISTING DATA WILL BE LOST.
   This build includes:
     - Category / CategoryItem core tables
     - CategoryItem* history tables (password/pin/security-questions)
     - BankCards + NEW CategoryItemAccounts (encrypted payload siblings)
     - ComboType/ComboDetail with requirement flags + seeds
     - Views: vw_CurrentPassword, active Combo views
     - Logs table + indexes
     - DbVersion + KeyArchiveIntegrity
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

-- ---------------------------------------------------------------------------
-- DROP VIEWS
-- ---------------------------------------------------------------------------
DROP VIEW IF EXISTS vw_CurrentPassword;
DROP VIEW IF EXISTS vw_CurrentPin;
DROP VIEW IF EXISTS vComboTypeActive;
DROP VIEW IF EXISTS vComboDetailActive;

-- ---------------------------------------------------------------------------
-- DROP TABLES (be exhaustive; includes legacy misspellings)
-- ---------------------------------------------------------------------------
DROP TABLE IF EXISTS AppSettings;
DROP TABLE IF EXISTS Logs;
DROP TABLE IF EXISTS DbVersion;

-- Universal dropdowns
DROP TABLE IF EXISTS ComboDetail;
DROP TABLE IF EXISTS ComboType;

-- Per-item history / lookups (legacy + current names)
DROP TABLE IF EXISTS CatagoryItemSecurityQuestions;
DROP TABLE IF EXISTS CategoryItemSecurityQuestions;

DROP TABLE IF EXISTS CatagoryItemPinHistory;
DROP TABLE IF EXISTS CategoryItemPinHistory;

DROP TABLE IF EXISTS CatagoryItemPasswordHistory;
DROP TABLE IF EXISTS CategoryItemPasswordHistory;

-- Sibling tables for item details
DROP TABLE IF EXISTS BankCards;
DROP TABLE IF EXISTS CategoryItemAccounts;

-- Core
DROP TABLE IF EXISTS CatagoryItem;
DROP TABLE IF EXISTS CategoryItem;

DROP TABLE IF EXISTS Catagory;
DROP TABLE IF EXISTS Category;

-- Integrity/meta
DROP TABLE IF EXISTS KeyArchiveIntegrity;

COMMIT;
PRAGMA foreign_keys = ON;

-- =============================================================================
-- CREATE OBJECTS
-- =============================================================================
BEGIN TRANSACTION;

-- ---------------------------------------------------------------------------
-- Universal dropdowns: ComboType, ComboDetail
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS ComboType (
    ComboTypeId   INTEGER  PRIMARY KEY AUTOINCREMENT,
    Code          TEXT     NOT NULL UNIQUE,
    Description   TEXT     NOT NULL,
    Active        INTEGER  NOT NULL DEFAULT 1 CHECK (Active IN (0,1)),
    CreatedUtc    TEXT     NOT NULL DEFAULT (datetime('now')),
    UpdatedUtc    TEXT     NOT NULL DEFAULT (datetime('now'))
);

CREATE TRIGGER IF NOT EXISTS trg_ComboType_UpdateUtc
AFTER UPDATE ON ComboType
BEGIN
  UPDATE ComboType SET UpdatedUtc = datetime('now') WHERE ComboTypeId = NEW.ComboTypeId;
END;

CREATE TABLE IF NOT EXISTS ComboDetail (
    ComboDetailId   INTEGER  PRIMARY KEY AUTOINCREMENT,
    ComboTypeId     INTEGER  NOT NULL,
    Seq             INTEGER  NOT NULL,
    Code            TEXT     NOT NULL,
    Description     TEXT     NOT NULL,

    -- requirement flags for account & card types
    IsAccountType     INTEGER NOT NULL DEFAULT 0 CHECK (IsAccountType IN (0,1)),
    IsCardType        INTEGER NOT NULL DEFAULT 0 CHECK (IsCardType    IN (0,1)),

    -- account requirements
    ReqAccountNumber  INTEGER NOT NULL DEFAULT 0 CHECK (ReqAccountNumber IN (0,1)),
    ReqRoutingNumber  INTEGER NOT NULL DEFAULT 0 CHECK (ReqRoutingNumber IN (0,1)),

    -- card requirements
    ReqCardNumber     INTEGER NOT NULL DEFAULT 0 CHECK (ReqCardNumber  IN (0,1)),
    ReqCardExpiry     INTEGER NOT NULL DEFAULT 0 CHECK (ReqCardExpiry  IN (0,1)),
    ReqCardCvv        INTEGER NOT NULL DEFAULT 0 CHECK (ReqCardCvv     IN (0,1)),

    -- whether UI should allow 1..N rows of this kind under a single item
    AllowsMultiple    INTEGER NOT NULL DEFAULT 0 CHECK (AllowsMultiple IN (0,1)),

    Active          INTEGER  NOT NULL DEFAULT 1 CHECK (Active IN (0,1)),
    CreatedUtc      TEXT     NOT NULL DEFAULT (datetime('now')),
    UpdatedUtc      TEXT     NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (ComboTypeId) REFERENCES ComboType (ComboTypeId) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_ComboDetail_Type_Seq
    ON ComboDetail (ComboTypeId, Seq);

CREATE UNIQUE INDEX IF NOT EXISTS ux_ComboDetail_Type_Code
    ON ComboDetail (ComboTypeId, Code);

CREATE INDEX IF NOT EXISTS ix_ComboDetail_TypeActiveSeq
    ON ComboDetail (ComboTypeId, Active, Seq);

CREATE TRIGGER IF NOT EXISTS trg_ComboDetail_UpdateUtc
AFTER UPDATE ON ComboDetail
BEGIN
  UPDATE ComboDetail
     SET UpdatedUtc = datetime('now')
   WHERE ComboDetailId = NEW.ComboDetailId;
END;

-- ---------------------------------------------------------------------------
-- Seed: Combo families
-- ---------------------------------------------------------------------------
INSERT OR IGNORE INTO ComboType (Code, Description, Active)
VALUES ('log_filters','Filters for the Logs UI',1),
       ('category_types','Category types in vault UI',1),
       ('debit_credit_cards','Debit and Credit Cards',1),
       ('bank_cards','Card brands and types used with financial accounts',1),
       ('account_number_types','Types of financial accounts and numbers',1);

-- Reset and seed category_types
DELETE FROM ComboDetail
 WHERE ComboTypeId=(SELECT ComboTypeId FROM ComboType WHERE Code='category_types');

INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 0, '0', 'Subscribed Web Pages', 1 FROM ComboType t WHERE t.Code='category_types';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 1, '1', 'Paid Subscription Web Pages', 1 FROM ComboType t WHERE t.Code='category_types';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 2, '2', 'Government/Retirement/Investment Web Pages', 1 FROM ComboType t WHERE t.Code='category_types';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 3, '3', 'Utilities', 1 FROM ComboType t WHERE t.Code='category_types';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 4, '4', 'Banks/Savings and Loans', 1 FROM ComboType t WHERE t.Code='category_types';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 5, '5', 'Encrypted Files/Folders', 1 FROM ComboType t WHERE t.Code='category_types';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 6, '6', 'Applications', 1 FROM ComboType t WHERE t.Code='category_types';

-- log_filters ensure
INSERT OR IGNORE INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 0, 'CATEGORY_DUPLICATE', 'Duplicate category detected', 1 FROM ComboType t WHERE t.Code='log_filters';
INSERT OR IGNORE INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 1, 'CATEGORY_INSERTED', 'Category successfully inserted', 1 FROM ComboType t WHERE t.Code='log_filters';
INSERT OR IGNORE INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 2, 'LOGIN', 'Login events', 1 FROM ComboType t WHERE t.Code='log_filters';
INSERT OR IGNORE INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 3, 'EARLY_FAIL', 'Early-fail events', 1 FROM ComboType t WHERE t.Code='log_filters';
-- CHANGED: replace APP_START/APP_EXIT with SESSION_START/SESSION_END
INSERT OR IGNORE INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 4, 'SESSION_START', 'Session started (post-login)', 1 FROM ComboType t WHERE t.Code='log_filters';
INSERT OR IGNORE INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 5, 'SESSION_END', 'Session ended', 1 FROM ComboType t WHERE t.Code='log_filters';

-- Bank Cards using UNION ALL subquery
WITH ct AS (SELECT ComboTypeId AS id FROM ComboType WHERE Code='bank_cards'),
sub AS (
  SELECT 10 AS seq, 'VISA'        AS code, 'VISA'           AS label UNION ALL
  SELECT 20       , 'MASTERCARD'  , 'MasterCard'                        UNION ALL
  SELECT 30       , 'DISCOVER'    , 'Discover'                          UNION ALL
  SELECT 40       , 'AMEX'        , 'American Express'                  UNION ALL
  SELECT 50       , 'DEBIT'       , 'Debit'
)
INSERT OR IGNORE INTO ComboDetail
(ComboTypeId, Seq, Code, Description, IsAccountType, IsCardType,
 ReqAccountNumber, ReqRoutingNumber, ReqCardNumber, ReqCardExpiry, ReqCardCvv, AllowsMultiple, Active)
SELECT ct.id, s.seq, s.code, s.label, 0, 1,
       0, 0, 1, 1, 1, 1, 1
FROM ct CROSS JOIN sub s;

-- Account Number Types using UNION ALL subquery
WITH ct AS (SELECT ComboTypeId AS id FROM ComboType WHERE Code='account_number_types'),
sub AS (
  SELECT 10 AS seq, 'BANK'         AS code, 'Bank'               AS label, 1 AS reqAcct, 1 AS reqRoute UNION ALL
  SELECT 20       , 'CREDIT_CARD'           , 'Credit Card'                    , 0          , 0          UNION ALL
  SELECT 30       , 'SAVINGS'               , 'Savings'                        , 1          , 1          UNION ALL
  SELECT 40       , 'CHECKING'              , 'Checking'                       , 1          , 1          UNION ALL
  SELECT 50       , 'CHRISTMAS'             , 'Christmas'                      , 1          , 0          UNION ALL
  SELECT 60       , 'STORE'                 , 'Store'                          , 0          , 0          UNION ALL
  SELECT 70       , 'LOAN_MORTGAGE'         , 'Loan - Mortgage'                , 1          , 0          UNION ALL
  SELECT 80       , 'LOAN_BUSINESS'         , 'Loan - Business'                , 1          , 0          UNION ALL
  SELECT 90       , 'LOAN_PERSONAL'         , 'Loan - Personal'                , 1          , 0          UNION ALL
  SELECT 100      , 'LOAN_AUTO'             , 'Loan - Auto'                    , 1          , 0          UNION ALL
  SELECT 110      , 'INVESTMENT'            , 'Investment'                     , 1          , 0
)
INSERT OR IGNORE INTO ComboDetail
(ComboTypeId, Seq, Code, Description, IsAccountType, IsCardType,
 ReqAccountNumber, ReqRoutingNumber, ReqCardNumber, ReqCardExpiry, ReqCardCvv, AllowsMultiple, Active)
SELECT ct.id, s.seq, s.code, s.label, 1, 0,
       s.reqAcct, s.reqRoute, 0, 0, 0, 1, 1
FROM ct CROSS JOIN sub s;

-- ---------------------------------------------------------------------------
-- Category
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Category (
    Category_Key         INTEGER PRIMARY KEY AUTOINCREMENT,
    Category_Name        TEXT    NOT NULL UNIQUE,
    Category_Description TEXT,
    Category_Type        INTEGER NOT NULL
                              REFERENCES ComboDetail (ComboDetailId),
    CreatedUtc           TEXT    NOT NULL DEFAULT (STRFTIME('%Y-%m-%dT%H:%M:%fZ','now')),
    IsActive             INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0,1))
);

-- Default Categories (correctly mapped to category_types)
INSERT OR IGNORE INTO Category (Category_Name, Category_Description, Category_Type, CreatedUtc, IsActive)
SELECT 'Application Forums','Login to forums that support applications',
       (SELECT d.ComboDetailId FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId=d.ComboTypeId
        WHERE t.Code='category_types' AND d.Seq=0),
       STRFTIME('%Y-%m-%dT%H:%M:%fZ','now'),1;

INSERT OR IGNORE INTO Category (Category_Name, Category_Description, Category_Type, CreatedUtc, IsActive)
SELECT 'Government','Any government web site login',
       (SELECT d.ComboDetailId FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId=d.ComboTypeId
        WHERE t.Code='category_types' AND d.Seq=2),
       STRFTIME('%Y-%m-%dT%H:%M:%fZ','now'),1;

INSERT OR IGNORE INTO Category (Category_Name, Category_Description, Category_Type, CreatedUtc, IsActive)
SELECT 'Astro Forums','Logins for Astro forum web sites',
       (SELECT d.ComboDetailId FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId=d.ComboTypeId
        WHERE t.Code='category_types' AND d.Seq=0),
       STRFTIME('%Y-%m-%dT%H:%M:%fZ','now'),1;

INSERT OR IGNORE INTO Category (Category_Name, Category_Description, Category_Type, CreatedUtc, IsActive)
SELECT 'Google Accounts','Logins for Gmail, Google Drive, or other Google services',
       (SELECT d.ComboDetailId FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId=d.ComboTypeId
        WHERE t.Code='category_types' AND d.Seq=0),
       STRFTIME('%Y-%m-%dT%H:%M:%fZ','now'),1;

INSERT OR IGNORE INTO Category (Category_Name, Category_Description, Category_Type, CreatedUtc, IsActive)
SELECT 'Non Google Email','Non Google Email logins',
       (SELECT d.ComboDetailId FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId=d.ComboTypeId
        WHERE t.Code='category_types' AND d.Seq=0),
       STRFTIME('%Y-%m-%dT%H:%M:%fZ','now'),1;

INSERT OR IGNORE INTO Category (Category_Name, Category_Description, Category_Type, CreatedUtc, IsActive)
SELECT 'Political Forums','Political forum logins',
       (SELECT d.ComboDetailId FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId=d.ComboTypeId
        WHERE t.Code='category_types' AND d.Seq=0),
       STRFTIME('%Y-%m-%dT%H:%M:%fZ','now'),1;

-- Newly requested default categories
INSERT OR IGNORE INTO Category (Category_Name, Category_Description, Category_Type, CreatedUtc, IsActive)
SELECT 'Utilities','Utility provider logins',
       (SELECT d.ComboDetailId FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId=d.ComboTypeId
        WHERE t.Code='category_types' AND d.Seq=3),
       STRFTIME('%Y-%m-%dT%H:%M:%fZ','now'),1;

INSERT OR IGNORE INTO Category (Category_Name, Category_Description, Category_Type, CreatedUtc, IsActive)
SELECT 'Banks/Savings and Loans','Banking and S&L logins',
       (SELECT d.ComboDetailId FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId=d.ComboTypeId
        WHERE t.Code='category_types' AND d.Seq=4),
       STRFTIME('%Y-%m-%dT%H:%M:%fZ','now'),1;

INSERT OR IGNORE INTO Category (Category_Name, Category_Description, Category_Type, CreatedUtc, IsActive)
SELECT 'Encrypted Files/Folders','Local encrypted file/folder entries',
       (SELECT d.ComboDetailId FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId=d.ComboTypeId
        WHERE t.Code='category_types' AND d.Seq=5),
       STRFTIME('%Y-%m-%dT%H:%M:%fZ','now'),1;

INSERT OR IGNORE INTO Category (Category_Name, Category_Description, Category_Type, CreatedUtc, IsActive)
SELECT 'Applications','Local desktop/application credentials',
       (SELECT d.ComboDetailId FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId=d.ComboTypeId
        WHERE t.Code='category_types' AND d.Seq=6),
       STRFTIME('%Y-%m-%dT%H:%M:%fZ','now'),1;

-- ---------------------------------------------------------------------------
-- CategoryItem
-- ---------------------------------------------------------------------------
CREATE TABLE CategoryItem (
    ItemId                   INTEGER PRIMARY KEY AUTOINCREMENT,
    Category_Key             INTEGER NOT NULL
                                  REFERENCES Category (Category_Key) ON DELETE CASCADE,

    CI_Name                  TEXT    NOT NULL,
    CI_Description           TEXT,
    CI_Notes                 TEXT,
    CI_SecretMeta            BLOB,

    -- New: catch-all encrypted data + storage indicator
    CI_SecretData            BLOB,
    CI_SecretStorage         TEXT    NOT NULL DEFAULT '0'   -- '0'=none, '1'=CI_SecretData, '2'=Accounts
                                  CHECK (CI_SecretStorage IN ('0','1','2')),

    CI_CreateUTC             INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    CI_UpdateUTC             INTEGER NOT NULL DEFAULT (strftime('%s','now')),

    IsActive                 INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0,1)),
    CI_NbrSecurityQuestions  INTEGER DEFAULT 0,

    CHECK (length(trim(CI_Name)) > 0),
    UNIQUE (Category_Key, CI_Name COLLATE NOCASE)
);


CREATE INDEX IF NOT EXISTS IX_CategoryItem_Category
  ON CategoryItem(Category_Key);

CREATE INDEX IF NOT EXISTS ix_CategoryItem_CategoryActiveName
  ON CategoryItem(Category_Key, IsActive, CI_Name);

CREATE INDEX IF NOT EXISTS ix_CategoryItem_CategoryUpdatedDesc
  ON CategoryItem(Category_Key, CI_UpdateUTC DESC);

CREATE TRIGGER IF NOT EXISTS trg_CategoryItem_TouchUpdateUtc
AFTER UPDATE ON CategoryItem
BEGIN
  UPDATE CategoryItem
     SET CI_UpdateUTC = strftime('%s','now')
   WHERE ItemId = NEW.ItemId;
END;

-- ---------------------------------------------------------------------------
-- Seed: Default storage guidance as inactive templates per Category
-- ---------------------------------------------------------------------------
-- Utilities: store account/customer number in CI_SecretData ('1')
INSERT OR IGNORE INTO CategoryItem
  (Category_Key, CI_Name, CI_Description, CI_Notes, CI_SecretMeta, CI_SecretData, CI_SecretStorage, CI_CreateUTC, CI_UpdateUTC, IsActive, CI_NbrSecurityQuestions)
SELECT c.Category_Key,
       'Template: Utilities storage','Default storage indicator for Utilities','Inactive template; set to active or duplicate as needed.',
       NULL, NULL, '1',
       strftime('%s','now'), strftime('%s','now'), 0, 0
FROM Category c WHERE c.Category_Name='Utilities';

-- Banks/S&L: store account/routing in CI_SecretData ('1')
INSERT OR IGNORE INTO CategoryItem
  (Category_Key, CI_Name, CI_Description, CI_Notes, CI_SecretMeta, CI_SecretData, CI_SecretStorage, CI_CreateUTC, CI_UpdateUTC, IsActive, CI_NbrSecurityQuestions)
SELECT c.Category_Key,
       'Template: Bank/S&L storage','Default storage indicator for Banks/S&L','Inactive template; set to active or duplicate as needed.',
       NULL, NULL, '1',
       strftime('%s','now'), strftime('%s','now'), 0, 0
FROM Category c WHERE c.Category_Name='Banks/Savings and Loans';

-- Encrypted Files/Folders: no account number ('0')
INSERT OR IGNORE INTO CategoryItem
  (Category_Key, CI_Name, CI_Description, CI_Notes, CI_SecretMeta, CI_SecretData, CI_SecretStorage, CI_CreateUTC, CI_UpdateUTC, IsActive, CI_NbrSecurityQuestions)
SELECT c.Category_Key,
       'Template: Encrypted Files/Folders storage','No account number storage for this category','Inactive template.',
       NULL, NULL, '0',
       strftime('%s','now'), strftime('%s','now'), 0, 0
FROM Category c WHERE c.Category_Name='Encrypted Files/Folders';

-- Applications: no account number ('0')
INSERT OR IGNORE INTO CategoryItem
  (Category_Key, CI_Name, CI_Description, CI_Notes, CI_SecretMeta, CI_SecretData, CI_SecretStorage, CI_CreateUTC, CI_UpdateUTC, IsActive, CI_NbrSecurityQuestions)
SELECT c.Category_Key,
       'Template: Applications storage','No account number storage for this category','Inactive template.',
       NULL, NULL, '0',
       strftime('%s','now'), strftime('%s','now'), 0, 0
FROM Category c WHERE c.Category_Name='Applications';

-- ---------------------------------------------------------------------------
-- BankCards (encrypted payload)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS BankCards (
    Bc_BankCardId   INTEGER PRIMARY KEY AUTOINCREMENT,
    Bc_ItemId       INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    Bc_Secret       BLOB    NOT NULL,
    Bc_IsActive     INTEGER NOT NULL DEFAULT 1 CHECK (Bc_IsActive IN (0,1)),
    Bc_CreatedAt    INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    Bc_UpdatedAt    INTEGER NOT NULL DEFAULT (strftime('%s','now'))
);

CREATE INDEX IF NOT EXISTS IX_BankCards_Item
  ON BankCards(Bc_ItemId);

CREATE INDEX IF NOT EXISTS IX_BankCards_ItemActive
  ON BankCards(Bc_ItemId, Bc_IsActive);

-- ---------------------------------------------------------------------------
-- CategoryItemAccounts (encrypted account/routing payloads)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CategoryItemAccounts (
    Cia_AccountId     INTEGER PRIMARY KEY AUTOINCREMENT,
    Cia_ItemId        INTEGER NOT NULL
                           REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,

    -- points to ComboDetail under 'account_number_types' (e.g., BANK, CHECKING…)
    Cia_TypeDetailId  INTEGER NOT NULL
                           REFERENCES ComboDetail (ComboDetailId),

    -- encrypted blob (e.g., JSON: {accountNumber, routingNumber, label, last4, notes})
    Cia_Secret        BLOB    NOT NULL,

    Cia_IsActive      INTEGER NOT NULL DEFAULT 1 CHECK (Cia_IsActive IN (0,1)),
    Cia_CreatedAt     INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    Cia_UpdatedAt     INTEGER NOT NULL DEFAULT (strftime('%s','now')),

    CHECK (Cia_TypeDetailId > 0)
);

CREATE INDEX IF NOT EXISTS IX_CIA_Item
  ON CategoryItemAccounts (Cia_ItemId);

CREATE INDEX IF NOT EXISTS IX_CIA_ItemActive
  ON CategoryItemAccounts (Cia_ItemId, Cia_IsActive);

CREATE INDEX IF NOT EXISTS IX_CIA_ItemType
  ON CategoryItemAccounts (Cia_ItemId, Cia_TypeDetailId);

CREATE TRIGGER IF NOT EXISTS trg_CIA_TouchUpdateUtc
AFTER UPDATE ON CategoryItemAccounts
BEGIN
  UPDATE CategoryItemAccounts
     SET Cia_UpdatedAt = strftime('%s','now')
   WHERE Cia_AccountId = NEW.Cia_AccountId;
END;

-- ---------------------------------------------------------------------------
-- CategoryItemPasswordHistory
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CategoryItemPasswordHistory (
    CIPaH_PwHistId    INTEGER PRIMARY KEY AUTOINCREMENT,
    CIPaH_ItemId      INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    CIPaH_CreatedAt   INTEGER NOT NULL,
    CIPaH_Version     INTEGER NOT NULL DEFAULT 1,
    CIPaH_Password    BLOB    NOT NULL,
    CIPaH_PadLen      INTEGER,
    CIPaH_PwSig       BLOB    NOT NULL,
    CIPaH_SigVersion  INTEGER NOT NULL DEFAULT 1
);

CREATE INDEX IF NOT EXISTS IX_CIPaH_Item_CreatedAt_Desc
  ON CategoryItemPasswordHistory(CIPaH_ItemId, CIPaH_CreatedAt DESC);

CREATE INDEX IF NOT EXISTS ix_CIPaH_Item_PwSig
  ON CategoryItemPasswordHistory(CIPaH_ItemId, CIPaH_PwSig);

-- ---------------------------------------------------------------------------
-- CategoryItemPinHistory
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CategoryItemPinHistory (
    CIPiH_PinHistId  INTEGER PRIMARY KEY AUTOINCREMENT,
    CIPiH_ItemId     INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    CIPiH_CreatedAt  INTEGER NOT NULL,
    CIPiH_Version    INTEGER NOT NULL DEFAULT 1,
    CIPiH_Pin        BLOB    NOT NULL,
    CIPiH_PadLen     INTEGER
);

CREATE INDEX IF NOT EXISTS IX_CIPiH_Item_CreatedAt_Desc
  ON CategoryItemPinHistory(CIPiH_ItemId, CIPiH_CreatedAt DESC);

-- ---------------------------------------------------------------------------
-- CategoryItemSecurityQuestions
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CategoryItemSecurityQuestions (
    CISQ_SecQId    INTEGER PRIMARY KEY AUTOINCREMENT,
    CISQ_ItemId    INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    CISQ_Question  TEXT    NOT NULL,
    CISQ_Answer    BLOB    NOT NULL,
    UNIQUE (CISQ_ItemId, CISQ_Question COLLATE NOCASE)
);

-- ---------------------------------------------------------------------------
-- KeyArchiveIntegrity
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS KeyArchiveIntegrity (
    Kai_Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    Kai_SizeBytes    INTEGER NOT NULL,
    Kai_Sha256Hex    TEXT    NOT NULL,
    Kai_RecordedUtc  TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_KeyArchiveIntegrity_Size_Hash
  ON KeyArchiveIntegrity(Kai_SizeBytes, Kai_Sha256Hex);

-- ---------------------------------------------------------------------------
-- Views
-- ---------------------------------------------------------------------------
CREATE VIEW IF NOT EXISTS vw_CurrentPassword AS
SELECT h.*
FROM CategoryItemPasswordHistory h
JOIN (
  SELECT CIPaH_ItemId, MAX(CIPaH_CreatedAt) AS MaxCreated
  FROM CategoryItemPasswordHistory
  GROUP BY CIPaH_ItemId
) latest
  ON latest.CIPaH_ItemId = h.CIPaH_ItemId
 AND latest.MaxCreated   = h.CIPaH_CreatedAt;

CREATE VIEW IF NOT EXISTS vComboTypeActive AS
SELECT * FROM ComboType WHERE Active = 1;

CREATE VIEW IF NOT EXISTS vComboDetailActive AS
SELECT * FROM ComboDetail WHERE Active = 1;

-- ---------------------------------------------------------------------------
-- DbVersion
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS DbVersion (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Version     TEXT    NOT NULL,
    AppliedOn   TEXT    NOT NULL,
    Description TEXT,
    IsCurrent   INTEGER NOT NULL CHECK (IsCurrent IN (0, 1))
);

INSERT INTO DbVersion (Version, AppliedOn, Description, IsCurrent)
SELECT '1.5.1',
       strftime('%Y-%m-%d %H:%M:%S','now'),
       'Add CategoryItemAccounts (encrypted), keep BankCards; seeds for bank_cards and account_number_types; requirement flags.',
       1
WHERE NOT EXISTS (SELECT 1 FROM DbVersion);

-- ---------------------------------------------------------------------------
-- Logs
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Logs (
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

CREATE INDEX IF NOT EXISTS IX_Logs_CreatedUtc        ON Logs(CreatedUtc);
CREATE INDEX IF NOT EXISTS IX_Logs_CreatedUtc_Desc   ON Logs(CreatedUtc DESC);
CREATE INDEX IF NOT EXISTS IX_Logs_Level             ON Logs(Level);
CREATE INDEX IF NOT EXISTS IX_Logs_Level_CreatedUtc  ON Logs(Level, CreatedUtc DESC);
CREATE INDEX IF NOT EXISTS IX_Logs_EventCode         ON Logs(EventCode);
CREATE INDEX IF NOT EXISTS IX_Logs_IsCrash           ON Logs(IsCrash);
CREATE INDEX IF NOT EXISTS IX_Logs_StackHash         ON Logs(StackHash);

COMMIT;
PRAGMA foreign_keys = ON;
