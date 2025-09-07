/* ============================================================================
   MWPV - FRESH LOAD / Nuke-and-Pave SCRIPT  (SQLite)
   ----------------------------------------------------------------------------
   ⚠️ DANGER: This drops and recreates schema. ALL EXISTING DATA WILL BE LOST.
   Purpose: rebuild the database from scratch with corrected 'Category' spelling.
   Notes:
     - DROP phase is tolerant: removes BOTH legacy 'Catagory*' and new 'Category*'.
     - CREATE phase uses ONLY the new 'Category*' names (tables/columns/FKs/views).
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

-- ---------------------------------------------------------------------------
-- DROP VIEWS FIRST (names unchanged)
-- ---------------------------------------------------------------------------
DROP VIEW IF EXISTS vw_CurrentPassword;
DROP VIEW IF EXISTS vw_CurrentPin;

-- ---------------------------------------------------------------------------
-- DROP TABLES (tolerate both old/new spellings)
-- ---------------------------------------------------------------------------
DROP TABLE IF EXISTS AppSettings;
DROP TABLE IF EXISTS Logs;
DROP TABLE IF EXISTS DbVersion;

-- Per-item history / lookups (both spellings)
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
-- CREATE OBJECTS (ONLY the corrected 'Category*' names)
-- =============================================================================
BEGIN TRANSACTION;

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
-- Table: CategoryItem  (CategoryItem_* -> CI_*, secrets moved to CI_SecretMeta)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CategoryItem (
    ItemId               INTEGER PRIMARY KEY AUTOINCREMENT,
    Category_Key         INTEGER NOT NULL
                              REFERENCES Category (Category_Key) ON DELETE CASCADE,

    CI_Name              TEXT    NOT NULL,         -- button label in the grid
    CI_Notes             TEXT,                     -- non-secret notes (plain text)

    -- Encrypted, viewable-but-not-searchable extras (AEAD-encrypted JSON):
    -- Example (before encryption):
    -- {"username":"...","email":"...","acctNbr":"...","licenseKey":"...","filePath":"...","secqa":[{"q":"...","a":"..."}]}
    CI_SecretMeta        BLOB,

    -- Timestamps (UTC as Unix epoch)
    CI_CreateUTC         INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    CI_UpdateUTC         INTEGER NOT NULL DEFAULT (strftime('%s','now')),

    IsActive             INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0,1)),

    -- Optional (redundant if you use secqa count in JSON)
    CI_NbrSecurityQuestions INTEGER DEFAULT 0,

    UNIQUE (Category_Key, CI_Name COLLATE NOCASE)
);

-- Helpful index when listing items by category
CREATE INDEX IF NOT EXISTS IX_CategoryItem_Category ON CategoryItem(Category_Key);

-- ---------------------------------------------------------------------------
-- Table: CategoryItemPasswordHistory  (columns prefixed CIPaH_)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CategoryItemPasswordHistory (
    CIPaH_PwHistId   INTEGER PRIMARY KEY AUTOINCREMENT,
    CIPaH_ItemId     INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    CIPaH_CreatedAt  INTEGER NOT NULL,               -- Unix epoch
    CIPaH_Version    INTEGER NOT NULL DEFAULT 1,     -- Crypto/key version
    CIPaH_Password   BLOB    NOT NULL,               -- version|nonce|tag|padLen|ciphertext (envelope)
    CIPaH_PadLen     INTEGER
);

-- Indexes (grouped with table)
CREATE INDEX IF NOT EXISTS IX_CIPaH_Item_CreatedAt_Desc
  ON CategoryItemPasswordHistory(CIPaH_ItemId, CIPaH_CreatedAt DESC);

-- ---------------------------------------------------------------------------
-- Table: CategoryItemPinHistory  (columns prefixed CIPiH_)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CategoryItemPinHistory (
    CIPiH_PinHistId  INTEGER PRIMARY KEY AUTOINCREMENT,
    CIPiH_ItemId     INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    CIPiH_CreatedAt  INTEGER NOT NULL,               -- Unix epoch
    CIPiH_Version    INTEGER NOT NULL DEFAULT 1,     -- Crypto/key version
    CIPiH_Pin        BLOB    NOT NULL,               -- version|nonce|tag|padLen|ciphertext (envelope)
    CIPiH_PadLen     INTEGER
);

-- Indexes (grouped with table)
CREATE INDEX IF NOT EXISTS IX_CIPiH_Item_CreatedAt_Desc
  ON CategoryItemPinHistory(CIPiH_ItemId, CIPiH_CreatedAt DESC);

-- ---------------------------------------------------------------------------
-- Table: CategoryItemSecurityQuestions  (columns prefixed CISQ_)
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
SELECT '1.0.0', strftime('%Y-%m-%d %H:%M:%S','now'), 'Initial schema creation (Category spelling fixed)', 1
WHERE NOT EXISTS (SELECT 1 FROM DbVersion);

-- ---------------------------------------------------------------------------
-- Table: Logs (canonical dev schema v1)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Logs (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    WhenUtc       TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now')),
    CreatedUtc    TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now')),
    Level         TEXT    NOT NULL CHECK (UPPER(Level) IN ('TRACE','DEBUG','INFO','WARN','ERROR','FATAL','WARNING')),
    Source        TEXT,
    EventCode     TEXT,
    SessionId     TEXT    NOT NULL DEFAULT '',
    LoginId       TEXT,                       -- GUID per successful login
    ItemId        INTEGER,                    -- affected item (no FK until review)
    MachineId     TEXT,
    DeviceMake    TEXT,                       -- e.g., Dell
    DeviceModel   TEXT,                       -- e.g., XPS 15 9520
    OSVersion     TEXT,                       -- e.g., Windows 11 23H2
    DeviceIdHash  TEXT,                       -- salted SHA-256 hex
    InstallType   TEXT,                       -- "Installed" | "Portable"
    AppVersion    TEXT    NOT NULL DEFAULT '',
    IsCrash       INTEGER NOT NULL DEFAULT 0 CHECK (IsCrash IN (0,1)),
    Payload       BLOB,        -- AES-256-GCM: nonce(12)|ciphertext|tag(16)
    PayloadFmt    TEXT,        -- e.g. 'gcm-json-v1'
    PayloadVer    INTEGER NOT NULL DEFAULT 1,
    KeySetVersion INTEGER NOT NULL DEFAULT 1,
    StackHash     TEXT
);

-- Helpful indexes
CREATE INDEX IF NOT EXISTS IX_Logs_CreatedUtc        ON Logs(CreatedUtc);
CREATE INDEX IF NOT EXISTS IX_Logs_CreatedUtc_Desc   ON Logs(CreatedUtc DESC);
CREATE INDEX IF NOT EXISTS IX_Logs_Level             ON Logs(Level);
CREATE INDEX IF NOT EXISTS IX_Logs_Level_CreatedUtc  ON Logs(Level, CreatedUtc DESC);
CREATE INDEX IF NOT EXISTS IX_Logs_EventCode         ON Logs(EventCode);
CREATE INDEX IF NOT EXISTS IX_Logs_IsCrash           ON Logs(IsCrash);
CREATE INDEX IF NOT EXISTS IX_Logs_StackHash         ON Logs(StackHash);

-- ---------------------------------------------------------------------------
-- Table: AppSettings (key/value store)
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

-- Seed defaults
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
-- Views (point to new column names)
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

COMMIT;

-- Optional tidy-up
-- VACUUM;
-- ANALYZE;
