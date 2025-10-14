/* ============================================================================
   MWPV - FRESH LOAD / Nuke-and-Pave SCRIPT  (SQLite)
   ----------------------------------------------------------------------------
   ⚠️ DANGER: Drops and recreates schema. ALL EXISTING DATA WILL BE LOST.
   This build includes:
     - CategoryItem client tables + tuned indexes
     - Password history with reuse fingerprint (CIPaH_PwSig) + versioning
     - vw_CurrentPassword view for newest-first fetch
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

DROP TABLE IF EXISTS BankCards;

DROP TABLE IF EXISTS CatagoryItem;
DROP TABLE IF EXISTS CategoryItem;

DROP TABLE IF EXISTS Catagory;
DROP TABLE IF EXISTS Category;

-- Key-archive integrity (current simple approach)
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
    ComboDetailId INTEGER  PRIMARY KEY AUTOINCREMENT,
    ComboTypeId   INTEGER  NOT NULL,
    Seq           INTEGER  NOT NULL,
    Code          TEXT     NOT NULL,
    Description   TEXT     NOT NULL,
    Active        INTEGER  NOT NULL DEFAULT 1 CHECK (Active IN (0,1)),
    CreatedUtc    TEXT     NOT NULL DEFAULT (datetime('now')),
    UpdatedUtc    TEXT     NOT NULL DEFAULT (datetime('now')),
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
-- Seed: Combo families and items (category_types)
-- ---------------------------------------------------------------------------
INSERT OR IGNORE INTO ComboType (Code, Description, Active)
VALUES ('log_filters','Filters for the Logs UI',1),
       ('category_types','Category types in vault UI',1),
       ('debit_credit_cards','Debit and Credit Cards',1);

DELETE FROM ComboDetail
 WHERE ComboTypeId=(SELECT ComboTypeId FROM ComboType WHERE Code='category_types');

INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 0, '0', 'Subscribed Web Pages', 1 FROM ComboType t WHERE t.Code='category_types';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 1, '1', 'Paid Subscription Web Pages', 1 FROM ComboType t WHERE t.Code='category_types';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 2, '2', 'Government/Retirement/Investment Web Pages', 1 FROM ComboType t WHERE t.Code='category_types';
---
-- *** Need additional inserts for category_types
-- Please add Utilities, Banks/Savings and Loans, Encrypted Files/Folders, Applications 
-- *** These will be inserts for category_types
---
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 3, '3', 'Utilities', 1 FROM ComboType t WHERE t.Code='category_types';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 4, '4', 'Banks/Savings and Loans', 1 FROM ComboType t WHERE t.Code='category_types';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 5, '5', 'Encrypted Files/Folders', 1 FROM ComboType t WHERE t.Code='category_types';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 6, '6', 'Applications', 1 FROM ComboType t WHERE t.Code='category_types';

-- *** Need additional inserts for log_filters
-- Please add  CATEGORY_DUPLICATE, CATEGORY_INSERTED, LOGIN, EARLY_FAIL
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 0, 'CATEGORY_DUPLICATE', 'Duplicate category detected', 1 FROM ComboType t WHERE t.Code='log_filters';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 1, 'CATEGORY_INSERTED', 'Category successfully inserted', 1 FROM ComboType t WHERE t.Code='log_filters';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 2, 'LOGIN', 'Login events', 1 FROM ComboType t WHERE t.Code='log_filters';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 3, 'EARLY_FAIL', 'Early-fail events', 1 FROM ComboType t WHERE t.Code='log_filters';
-- Additional inserts for log events.
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 4, 'APP_START', 'Application started', 1 FROM ComboType t WHERE t.Code='log_filters';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 5, 'APP_EXIT', 'Application exited', 1 FROM ComboType t WHERE t.Code='log_filters';
-- LOGIN + EARLY_FAIL already seeded; LOGIN_FAILED exists per your note


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

-- Example seeds (same pattern you used)
INSERT INTO Category (Category_Name, Category_Description, Category_Type, CreatedUtc, IsActive)
SELECT 'Application Forums','Login to forums that support applications',
       (SELECT d.ComboDetailId FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId=d.ComboTypeId
        WHERE t.Code='category_types' AND d.Seq=0),
       STRFTIME('%Y-%m-%dT%H:%M:%fZ','now'),1;

INSERT INTO Category (Category_Name, Category_Description, Category_Type, CreatedUtc, IsActive)
SELECT 'Government','Any government web site login',
       (SELECT d.ComboDetailId FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId=d.ComboTypeId
        WHERE t.Code='category_types' AND d.Seq=0),
       STRFTIME('%Y-%m-%dT%H:%M:%fZ','now'),1;

INSERT INTO Category (Category_Name, Category_Description, Category_Type, CreatedUtc, IsActive)
SELECT 'Astro Forums','Logins for Astro forum web sites',
       (SELECT d.ComboDetailId FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId=d.ComboTypeId
        WHERE t.Code='category_types' AND d.Seq=0),
       STRFTIME('%Y-%m-%dT%H:%M:%fZ','now'),1;

INSERT INTO Category (Category_Name, Category_Description, Category_Type, CreatedUtc, IsActive)
SELECT 'Google Accounts','Logins for Gmail, Google Drive, or other Google services',
       (SELECT d.ComboDetailId FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId=d.ComboTypeId
        WHERE t.Code='category_types' AND d.Seq=0),
       STRFTIME('%Y-%m-%dT%H:%M:%fZ','now'),1;

INSERT INTO Category (Category_Name, Category_Description, Category_Type, CreatedUtc, IsActive)
SELECT 'Non Google Email','Non Google Email logins',
       (SELECT d.ComboDetailId FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId=d.ComboTypeId
        WHERE t.Code='category_types' AND d.Seq=0),
       STRFTIME('%Y-%m-%dT%H:%M:%fZ','now'),1;

INSERT INTO Category (Category_Name, Category_Description, Category_Type, CreatedUtc, IsActive)
SELECT 'Political Forums','Political forum logins',
       (SELECT d.ComboDetailId FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId=d.ComboTypeId
        WHERE t.Code='category_types' AND d.Seq=0),
       STRFTIME('%Y-%m-%dT%H:%M:%fZ','now'),1;

-- ---------------------------------------------------------------------------
-- CategoryItem  (FOCUS AREA)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CategoryItem (
    ItemId                   INTEGER PRIMARY KEY AUTOINCREMENT,
    Category_Key             INTEGER NOT NULL
                                  REFERENCES Category (Category_Key) ON DELETE CASCADE,

    CI_Name                  TEXT    NOT NULL,
	CI_Description			 TEXT,
    CI_Notes                 TEXT,
    CI_SecretMeta            BLOB,

    CI_CreateUTC             INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    CI_UpdateUTC             INTEGER NOT NULL DEFAULT (strftime('%s','now')),

    IsActive                 INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0,1)),
    CI_NbrSecurityQuestions  INTEGER DEFAULT 0,

    -- prevent dup names in same category (case-insensitive)
    UNIQUE (Category_Key, CI_Name COLLATE NOCASE)
);

-- Keep simple category lookup
CREATE INDEX IF NOT EXISTS IX_CategoryItem_Category
  ON CategoryItem(Category_Key);

-- Fast list/search in category by active + name
CREATE INDEX IF NOT EXISTS ix_CategoryItem_CategoryActiveName
  ON CategoryItem(Category_Key, IsActive, CI_Name);

-- Recently updated in a category (DESC helps LIMIT queries)
CREATE INDEX IF NOT EXISTS ix_CategoryItem_CategoryUpdatedDesc
  ON CategoryItem(Category_Key, CI_UpdateUTC DESC);

-- Touch CI_UpdateUTC on any UPDATE (keeps “recently updated” fresh)
CREATE TRIGGER IF NOT EXISTS trg_CategoryItem_TouchUpdateUtc
AFTER UPDATE ON CategoryItem
BEGIN
  UPDATE CategoryItem
     SET CI_UpdateUTC = strftime('%s','now')
   WHERE ItemId = NEW.ItemId;
END;

-- ---------------------------------------------------------------------------
-- BankCards (kept)
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
-- CategoryItemPasswordHistory  (FOCUS AREA: adds PwSig + version)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CategoryItemPasswordHistory (
    CIPaH_PwHistId    INTEGER PRIMARY KEY AUTOINCREMENT,
    CIPaH_ItemId      INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    CIPaH_CreatedAt   INTEGER NOT NULL,          -- unix epoch seconds
    CIPaH_Version     INTEGER NOT NULL DEFAULT 1,
    CIPaH_Password    BLOB    NOT NULL,          -- encrypted password
    CIPaH_PadLen      INTEGER,

    -- NEW: deterministic, secret fingerprint for reuse checks
    CIPaH_PwSig       BLOB    NOT NULL,
    CIPaH_SigVersion  INTEGER NOT NULL DEFAULT 1
);

-- Newest-first lookups
CREATE INDEX IF NOT EXISTS IX_CIPaH_Item_CreatedAt_Desc
  ON CategoryItemPasswordHistory(CIPaH_ItemId, CIPaH_CreatedAt DESC);

-- Reuse check is instant: per-item + signature
CREATE INDEX IF NOT EXISTS ix_CIPaH_Item_PwSig
  ON CategoryItemPasswordHistory(CIPaH_ItemId, CIPaH_PwSig);

-- ---------------------------------------------------------------------------
-- CategoryItemPinHistory (kept)
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
-- CategoryItemSecurityQuestions (kept)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CategoryItemSecurityQuestions (
    CISQ_SecQId    INTEGER PRIMARY KEY AUTOINCREMENT,
    CISQ_ItemId    INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    CISQ_Question  TEXT    NOT NULL,
    CISQ_Answer    BLOB    NOT NULL,
    UNIQUE (CISQ_ItemId, CISQ_Question COLLATE NOCASE)
);

-- ---------------------------------------------------------------------------
-- KeyArchiveIntegrity (simple size + hash)
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
-- Current password = newest per ItemId (fast via IX_CIPaH_Item_CreatedAt_Desc)
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

-- (Optional future) vw_CurrentPin similar pattern when needed

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
SELECT '1.4.0',
       strftime('%Y-%m-%d %H:%M:%S','now'),
       'Add password-reuse fingerprint (CIPaH_PwSig + CIPaH_SigVersion) and index; keep newest-first index; keep CategoryItem active/name & updated-desc indexes; include simple KeyArchiveIntegrity.',
       1
WHERE NOT EXISTS (SELECT 1 FROM DbVersion);

-- ---------------------------------------------------------------------------
-- Logs (kept)
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
