/* ============================================================================
   MWPV - FRESH LOAD / Nuke-and-Pave SCRIPT  (SQLite)
   ----------------------------------------------------------------------------
   ⚠️ DANGER: This drops and recreates schema. ALL EXISTING DATA WILL BE LOST.
   Purpose: rebuild DB with corrected Category*, and universal ComboType/ComboDetail
            for all dropdowns (AUTOINCREMENT keys; no hardcoded IDs).
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

-- ---------------------------------------------------------------------------
-- DROP VIEWS (drop all known)
-- ---------------------------------------------------------------------------
DROP VIEW IF EXISTS vw_CurrentPassword;
DROP VIEW IF EXISTS vw_CurrentPin;
DROP VIEW IF EXISTS vComboTypeActive;
DROP VIEW IF EXISTS vComboDetailActive;

-- ---------------------------------------------------------------------------
-- DROP TABLES (be exhaustive/consistent)
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

DROP TABLE IF EXISTS CatagoryItem;
DROP TABLE IF EXISTS CategoryItem;

DROP TABLE IF EXISTS Catagory;
DROP TABLE IF EXISTS Category;

COMMIT;
PRAGMA foreign_keys = ON;

-- =============================================================================
-- CREATE OBJECTS
-- =============================================================================
BEGIN TRANSACTION;

-- ---------------------------------------------------------------------------
-- Universal dropdowns: ComboType, ComboDetail (AUTOINCREMENT PKs)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS ComboType (
    ComboTypeId   INTEGER  PRIMARY KEY AUTOINCREMENT,  -- auto-assigned
    Code          TEXT     NOT NULL UNIQUE,            -- e.g., 'log_filters'
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
    ComboDetailId INTEGER  PRIMARY KEY AUTOINCREMENT,  -- auto-assigned
    ComboTypeId   INTEGER  NOT NULL,                   -- FK to ComboType
    Seq           INTEGER  NOT NULL,                   -- display order
    Code          TEXT     NOT NULL,                   -- stable item code (e.g., 'SMOKE')
    Description   TEXT     NOT NULL,                   -- display text
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
-- Table: Category
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Category (
    Category_Key         INTEGER PRIMARY KEY AUTOINCREMENT,
    Category_Name        TEXT    NOT NULL COLLATE NOCASE UNIQUE,
    Category_Description TEXT,
    IsActive             INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0,1))
);

-- Seed default categories (idempotent)
INSERT INTO Category (Category_Name, Category_Description, IsActive)
SELECT 'Encryption', 'Encrypted local Files and or folders', 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name='Encryption');

INSERT INTO Category (Category_Name, Category_Description, IsActive)
SELECT 'Financial', 'Financial web sites or applications (Banking/Credit Card)', 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name='Financial');

INSERT INTO Category (Category_Name, Category_Description, IsActive)
SELECT 'Applications', 'Computer/Phone application logins', 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name='Applications');

INSERT INTO Category (Category_Name, Category_Description, IsActive)
SELECT 'Application Forums', 'Login to forums that support applications', 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name='Application Forums');

INSERT INTO Category (Category_Name, Category_Description, IsActive)
SELECT 'Goverment', 'Any government web site login', 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name='Goverment');

INSERT INTO Category (Category_Name, Category_Description, IsActive)
SELECT 'Astro Forums', 'Logins for Astro forum web sites', 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name='Astro Forums');

INSERT INTO Category (Category_Name, Category_Description, IsActive)
SELECT 'Google Accounts', 'Logins for Gmail, Google Drive, or other Google services', 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name='Google Accounts');

INSERT INTO Category (Category_Name, Category_Description, IsActive)
SELECT 'Non Google Email', 'Non Google Email logins', 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name='Non Google Email');

INSERT INTO Category (Category_Name, Category_Description, IsActive)
SELECT 'Political Forums', 'Political forum logins', 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name='Political Forums');

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
-- Table: CategoryItemPasswordHistory
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CategoryItemPasswordHistory (
    CIPaH_PwHistId   INTEGER PRIMARY KEY AUTOINCREMENT,
    CIPaH_ItemId     INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    CIPaH_CreatedAt  INTEGER NOT NULL,               -- Unix epoch
    CIPaH_Version    INTEGER NOT NULL DEFAULT 1,
    CIPaH_Password   BLOB    NOT NULL,               -- version|nonce|tag|padLen|ciphertext
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
    CIPiH_CreatedAt  INTEGER NOT NULL,               -- Unix epoch
    CIPiH_Version    INTEGER NOT NULL DEFAULT 1,
    CIPiH_Pin        BLOB    NOT NULL,               -- version|nonce|tag|padLen|ciphertext
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
SELECT '1.2.0',
       strftime('%Y-%m-%d %H:%M:%S','now'),
       'Schema refresh: Category* corrected; add universal ComboType/ComboDetail (AUTOINCREMENT) and seed log filters.',
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
    Payload       BLOB,        -- AES-256-GCM: nonce(12)|ciphertext|tag(16)
    PayloadFmt    TEXT,        -- e.g. 'gcm-json-v1'
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

-- Seed defaults (idempotent)
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

-- ---------------------------------------------------------------------------
-- SEED: Universal dropdowns (idempotent; no manual IDs)
-- ---------------------------------------------------------------------------

-- Ensure the log filter family exists
INSERT OR IGNORE INTO ComboType (Code, Description, Active)
VALUES ('log_filters', 'Filters for the Logs UI', 1);

-- Optionally, another family for categories (left here as example; safe if kept)
INSERT OR IGNORE INTO ComboType (Code, Description, Active)
VALUES ('category_types', 'Category types in vault UI', 1);

-- Insert log filter items by resolving ComboTypeId dynamically
INSERT OR IGNORE INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 0, 'SMOKE',            'Smoke Test',         1
  FROM ComboType t WHERE t.Code = 'log_filters';

INSERT OR IGNORE INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 1, 'EARLY_FAIL',       'Early Failure',      1
  FROM ComboType t WHERE t.Code = 'log_filters';

INSERT OR IGNORE INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 2, 'CATEGORY_INSERTED','Category Inserted',  1
  FROM ComboType t WHERE t.Code = 'log_filters';

-- Example category types (optional)
INSERT OR IGNORE INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 0, 'personal', 'Personal', 1
  FROM ComboType t WHERE t.Code = 'category_types';

INSERT OR IGNORE INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT t.ComboTypeId, 1, 'work',     'Work',     1
  FROM ComboType t WHERE t.Code = 'category_types';

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

-- Active-only convenience views for dropdowns
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

-- Optional tidy-up
-- VACUUM;
-- ANALYZE;
