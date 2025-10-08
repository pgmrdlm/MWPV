/* ============================================================================
   MWPV - FRESH LOAD / Nuke-and-Pave SCRIPT  (SQLite)
   ----------------------------------------------------------------------------
   ⚠️ DANGER: Drops and recreates schema. ALL EXISTING DATA WILL BE LOST.
   Purpose: Rebuild DB with Category.Category_Type, universal ComboType/ComboDetail,
            Logs schema matching app, and a SINGLE key-archive integrity table
            (size + hash only).
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
-- DROP TABLES (be exhaustive)
-- ---------------------------------------------------------------------------
DROP TABLE IF EXISTS AppSettings;
DROP TABLE IF EXISTS Logs;
DROP TABLE IF EXISTS DbVersion;

-- Universal dropdowns
DROP TABLE IF EXISTS ComboDetail;
DROP TABLE IF EXISTS ComboType;

-- Per-item history / lookups (support both legacy/new spellings)
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

-- Manifest / integrity-related (old tables)
DROP TABLE IF EXISTS KeyFileMeta;
DROP TABLE IF EXISTS KeyFileManifest;

-- New single-row integrity table (if present from prior runs)
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
-- Seed: Combo families and items
-- ---------------------------------------------------------------------------
INSERT OR IGNORE INTO ComboType (Code, Description, Active)
VALUES ('log_filters','Filters for the Logs UI',1),
       ('category_types','Category types in vault UI',1),
       ('debit_credit_cards','Debit and Credit Cards',1);

INSERT OR IGNORE INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 1, 'EARLY_FAIL',       'Early Failure',      1 FROM ComboType t WHERE t.Code='log_filters';
INSERT OR IGNORE INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 2, 'CATEGORY_INSERTED','Category Inserted',  1 FROM ComboType t WHERE t.Code='log_filters';

DELETE FROM ComboDetail
 WHERE ComboTypeId=(SELECT ComboTypeId FROM ComboType WHERE Code='category_types');

INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 0, '0', 'Subscribed Web Pages', 1 FROM ComboType t WHERE t.Code='category_types';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 1, '1', 'Paid Subscription Web Pages', 1 FROM ComboType t WHERE t.Code='category_types';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 2, '2', 'Government/Retirement/Investment Web Pages', 1 FROM ComboType t WHERE t.Code='category_types';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 3, '3', 'App/File/Folder Logins', 1 FROM ComboType t WHERE t.Code='category_types';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 4, '4', 'Banks/Credit unions', 1 FROM ComboType t WHERE t.Code='category_types';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 5, '5', 'Store web sites', 1 FROM ComboType t WHERE t.Code='category_types';

INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 0, '0', 'Debit Card', 1
FROM ComboType t WHERE t.Code='debit_credit_cards';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 1, '1', 'VISA', 1
FROM ComboType t WHERE t.Code='debit_credit_cards';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 2, '2', 'Master Card', 1
FROM ComboType t WHERE t.Code='debit_credit_cards';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 3, '3', 'Discover Card', 1
FROM ComboType t WHERE t.Code='debit_credit_cards';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 4, '4', 'American Express', 1
FROM ComboType t WHERE t.Code='debit_credit_cards';
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 5, '5', 'Store Credit Card', 1
FROM ComboType t WHERE t.Code='debit_credit_cards';

-- ---------------------------------------------------------------------------
-- Table: Category
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Category (
    Category_Key         INTEGER PRIMARY KEY AUTOINCREMENT,
    Category_Name        TEXT    NOT NULL COLLATE NOCASE UNIQUE,
    Category_Description TEXT,
    Category_Type        INTEGER NOT NULL,
    CreatedUtc           TEXT    NOT NULL DEFAULT (STRFTIME('%Y-%m-%dT%H:%M:%fZ','now')),
    IsActive             INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0,1))
);

-- Seeds
INSERT INTO Category (Category_Name, Category_Description, Category_Type, CreatedUtc, IsActive)
SELECT 'Encryption','Encrypted local Files and or folders',
       (SELECT d.ComboDetailId FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId=d.ComboTypeId
        WHERE t.Code='category_types' AND d.Seq=0),
       STRFTIME('%Y-%m-%dT%H:%M:%fZ','now'),1;

INSERT INTO Category (Category_Name, Category_Description, Category_Type, CreatedUtc, IsActive)
SELECT 'Financial','Financial web sites or applications (Banking/Credit Card)',
       (SELECT d.ComboDetailId FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId=d.ComboTypeId
        WHERE t.Code='category_types' AND d.Seq=0),
       STRFTIME('%Y-%m-%dT%H:%M:%fZ','now'),1;

INSERT INTO Category (Category_Name, Category_Description, Category_Type, CreatedUtc, IsActive)
SELECT 'Applications','Computer/Phone application logins',
       (SELECT d.ComboDetailId FROM ComboDetail d JOIN ComboType t ON t.ComboTypeId=d.ComboTypeId
        WHERE t.Code='category_types' AND d.Seq=0),
       STRFTIME('%Y-%m-%dT%H:%M:%fZ','now'),1;

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
-- Table: CategoryItem
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CategoryItem (
    ItemId               INTEGER PRIMARY KEY AUTOINCREMENT,
    Category_Key         INTEGER NOT NULL
                              REFERENCES Category (Category_Key) ON DELETE CASCADE,

    CI_Name              TEXT    NOT NULL,
    CI_Notes             TEXT,
    CI_SecretMeta        BLOB,

    CI_CreateUTC         INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    CI_UpdateUTC         INTEGER NOT NULL DEFAULT (strftime('%s','now')),

    IsActive             INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0,1)),
    CI_NbrSecurityQuestions INTEGER DEFAULT 0,

    UNIQUE (Category_Key, CI_Name COLLATE NOCASE)
);

CREATE INDEX IF NOT EXISTS IX_CategoryItem_Category ON CategoryItem(Category_Key);

-- ---------------------------------------------------------------------------
-- NEW: BankCards
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS BankCards (
    Bc_BankCardId   INTEGER PRIMARY KEY AUTOINCREMENT,
    Bc_ItemId       INTEGER NOT NULL
                        REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
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
-- Table: CategoryItemPasswordHistory
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CategoryItemPasswordHistory (
    CIPaH_PwHistId   INTEGER PRIMARY KEY AUTOINCREMENT,
    CIPaH_ItemId     INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    CIPaH_CreatedAt  INTEGER NOT NULL,
    CIPaH_Version    INTEGER NOT NULL DEFAULT 1,
    CIPaH_Password   BLOB    NOT NULL,
    CIPaH_PadLen     INTEGER
);
CREATE INDEX IF NOT EXISTS IX_CIPaH_Item_CreatedAt_Desc
  ON CategoryItemPasswordHistory(CIPaH_ItemId, CIPaH_CreatedAt DESC);

-- ---------------------------------------------------------------------------
-- Table: CategoryItemPinHistory
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
-- Table: CategoryItemSecurityQuestions
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CategoryItemSecurityQuestions (
    CISQ_SecQId    INTEGER PRIMARY KEY AUTOINCREMENT,
    CISQ_ItemId    INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    CISQ_Question  TEXT    NOT NULL,
    CISQ_Answer    BLOB    NOT NULL,
    UNIQUE (CISQ_ItemId, CISQ_Question COLLATE NOCASE)
);

-- ---------------------------------------------------------------------------
-- Table: DbVersion
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS DbVersion (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Version     TEXT    NOT NULL,
    AppliedOn   TEXT    NOT NULL,
    Description TEXT,
    IsCurrent   INTEGER NOT NULL CHECK (IsCurrent IN (0, 1))
);

INSERT INTO DbVersion (Version, AppliedOn, Description, IsCurrent)
SELECT '1.3.1',
       strftime('%Y-%m-%d %H:%M:%S','now'),
       'Replace manifest tables with single KeyArchiveIntegrity (size + hash).',
       1
WHERE NOT EXISTS (SELECT 1 FROM DbVersion);

-- ---------------------------------------------------------------------------
-- Table: Logs
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

-- ---------------------------------------------------------------------------
-- Table: AppSettings (key/value)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS AppSettings (
    Key            TEXT NOT NULL,
    Scope          TEXT NOT NULL DEFAULT 'Global',
    Value          TEXT NOT NULL,
    ValueType      TEXT NOT NULL,              -- 'string' | 'int' | 'bool' | 'json'
    Description    TEXT,
    LastUpdatedUtc INTEGER,
    PRIMARY KEY (Key, Scope)
);
CREATE INDEX IF NOT EXISTS IX_AppSettings_Key ON AppSettings(Key);

INSERT INTO AppSettings (Key, Scope, Value, ValueType, Description, LastUpdatedUtc)
SELECT 'Portable.Enabled','Global','false','bool','Run in portable mode',strftime('%s','now')
WHERE NOT EXISTS (SELECT 1 FROM AppSettings WHERE Key='Portable.Enabled' AND Scope='Global');

INSERT INTO AppSettings (Key, Scope, Value, ValueType, Description, LastUpdatedUtc)
SELECT 'Portable.DbPath','Global','','string','Database file path in portable mode',strftime('%s','now')
WHERE NOT EXISTS (SELECT 1 FROM AppSettings WHERE Key='Portable.DbPath' AND Scope='Global');

INSERT INTO AppSettings (Key, Scope, Value, ValueType, Description, LastUpdatedUtc)
SELECT 'Portable.KeyFilePath','Global','','string','Key file path in portable mode',strftime('%s','now')
WHERE NOT EXISTS (SELECT 1 FROM AppSettings WHERE Key='Portable.KeyFilePath' AND Scope='Global');

INSERT INTO AppSettings (Key, Scope, Value, ValueType, Description, LastUpdatedUtc)
SELECT 'Portable.SqlCatalog','Global','[]','json','SQL scripts to load at startup (portable)',strftime('%s','now')
WHERE NOT EXISTS (SELECT 1 FROM AppSettings WHERE Key='Portable.SqlCatalog' AND Scope='Global');

-- =========================
-- Single-row key archive integrity (size + hash only)
-- =========================
CREATE TABLE IF NOT EXISTS KeyArchiveIntegrity (
    kai_Id            INTEGER PRIMARY KEY CHECK (kai_Id = 1),
    kai_ArchiveSha256 TEXT    NOT NULL,             -- SHA-256 of entire key archive
    kai_ArchiveSize   INTEGER NOT NULL,             -- bytes
    kai_WrittenUtc    TEXT    NOT NULL DEFAULT (datetime('now'))
);

-- ---------------------------------------------------------------------------
-- VIEWS (recreate)
-- ---------------------------------------------------------------------------
DROP VIEW IF EXISTS vw_CurrentPassword;
CREATE VIEW IF NOT EXISTS vw_CurrentPassword AS
    SELECT h.*
    FROM CategoryItemPasswordHistory h
    JOIN (
        SELECT CIPaH_ItemId, MAX(CIPaH_CreatedAt) AS MaxCreated
        FROM CategoryItemPasswordHistory
        GROUP BY CIPaH_ItemId
    ) latest
      ON h.CIPaH_ItemId = latest.CIPaH_ItemId
     AND h.CIPaH_CreatedAt = latest.MaxCreated;

DROP VIEW IF EXISTS vw_CurrentPin;
CREATE VIEW IF NOT EXISTS vw_CurrentPin AS
    SELECT h.*
    FROM CategoryItemPinHistory h
    JOIN (
        SELECT CIPiH_ItemId, MAX(CIPiH_CreatedAt) AS MaxCreated
        FROM CategoryItemPinHistory
        GROUP BY CIPiH_ItemId
    ) latest
      ON h.CIPiH_ItemId = latest.CIPiH_ItemId
     AND h.CIPiH_CreatedAt = latest.MaxCreated;

DROP VIEW IF EXISTS vComboTypeActive;
CREATE VIEW IF NOT EXISTS vComboTypeActive AS
SELECT ComboTypeId, Code, Description
  FROM ComboType
 WHERE Active = 1;

DROP VIEW IF EXISTS vComboDetailActive;
CREATE VIEW IF NOT EXISTS vComboDetailActive AS
SELECT d.ComboDetailId, d.ComboTypeId, t.Code AS TypeCode, d.Seq, d.Code, d.Description
  FROM ComboDetail d
  JOIN ComboType t ON t.ComboTypeId = d.ComboTypeId
 WHERE t.Active = 1 AND d.Active = 1
 ORDER BY d.ComboTypeId, d.Seq;

COMMIT;
-- VACUUM;
-- ANALYZE;
