/* ============================================================================
   MWPV - FRESH LOAD / Nuke-and-Pave SCRIPT  (SQLite)
   ----------------------------------------------------------------------------
   ⚠️ DANGER: This drops and recreates schema. ALL EXISTING DATA WILL BE LOST.
   Purpose: rebuild the database from scratch with corrected 'Category' spelling
            and enhanced Logs schema (dedupe/quarantine/provenance/archiving).
   Notes:
     - DROP phase is tolerant: removes BOTH legacy 'Catagory*' and new 'Category*'.
     - CREATE phase uses ONLY the new 'Category*' names (tables/columns/FKs/views).
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

-- ---------------------------------------------------------------------------
-- DROP VIEWS FIRST (names unchanged + new log views)
-- ---------------------------------------------------------------------------
DROP VIEW IF EXISTS vw_CurrentPassword;
DROP VIEW IF EXISTS vw_CurrentPin;

-- New log views (drop if exist)
DROP VIEW IF EXISTS vw_Logs_RecentActive;
DROP VIEW IF EXISTS vw_Logs_ErrorsRecent;
DROP VIEW IF EXISTS vw_Logs_Quarantine;
DROP VIEW IF EXISTS vw_Logs_EarlyIngest;
DROP VIEW IF EXISTS vw_Logs_Archive;
DROP VIEW IF EXISTS vw_Logs_StackHotspots;

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
-- Table: CategoryItem
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CategoryItem (
    ItemId                               INTEGER PRIMARY KEY AUTOINCREMENT,
    Category_Key                         INTEGER NOT NULL
                                               REFERENCES Category (Category_Key) ON DELETE CASCADE,
    CategoryItem_Name                    TEXT    NOT NULL,
    CategoryItem_Password                BLOB,
    CategoryItem_Pin                     BLOB,
    CategoryItem_AcctNbr                 BLOB,
    CategoryId_LicenceKey                BLOB,   -- kept original field intent; only prefix corrected
    CategoryItem_LoginId                 BLOB,
    CategoryItem_Email                   BLOB,
    CategoryItem_UpdateDate              TEXT    NOT NULL,
    CategoryItem_Notes                   TEXT,
    IsActive                             INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0,1)),
    CategoryItem_NbrSecurityQuestions    INTEGER DEFAULT 0,
    UNIQUE (Category_Key, CategoryItem_Name COLLATE NOCASE)
);

-- ---------------------------------------------------------------------------
-- Table: CategoryItemPasswordHistory
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CategoryItemPasswordHistory (
    PwHistId  INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId    INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    CreatedAt INTEGER NOT NULL,               -- Unix epoch
    Version   INTEGER NOT NULL DEFAULT 1,     -- Crypto/key version
    Password  BLOB    NOT NULL,               -- Envelope (e.g., version|nonce|tag|padLen|ciphertext)
    PadLen    INTEGER
);

-- ---------------------------------------------------------------------------
-- Table: CategoryItemPinHistory
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CategoryItemPinHistory (
    PinHistId INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId    INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    CreatedAt INTEGER NOT NULL,               -- Unix epoch
    Version   INTEGER NOT NULL DEFAULT 1,     -- Crypto/key version
    Pin       BLOB    NOT NULL,               -- Envelope (e.g., version|nonce|tag|padLen|ciphertext)
    PadLen    INTEGER
);

-- ---------------------------------------------------------------------------
-- Table: CategoryItemSecurityQuestions
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CategoryItemSecurityQuestions (
    SecQId   INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId   INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    Question TEXT    NOT NULL,
    Answer   BLOB    NOT NULL,
    UNIQUE (ItemId, Question COLLATE NOCASE)
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
SELECT '1.0.0', strftime('%Y-%m-%d %H:%M:%S','now'), 'Initial schema creation (Category spelling fixed + enhanced Logs)', 1
WHERE NOT EXISTS (SELECT 1 FROM DbVersion);

-- ---------------------------------------------------------------------------
-- Table: Logs (canonical dev schema v1 + enhancements)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Logs (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    WhenUtc        TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now')),
    CreatedUtc     TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now')),
    Level          TEXT    NOT NULL CHECK (UPPER(Level) IN ('TRACE','DEBUG','INFO','WARN','ERROR','FATAL','WARNING')),
    Source         TEXT,
    EventCode      TEXT,
    SessionId      TEXT    NOT NULL DEFAULT '',
    MachineId      TEXT,
    AppVersion     TEXT    NOT NULL DEFAULT '',
    IsCrash        INTEGER NOT NULL DEFAULT 0 CHECK (IsCrash IN (0,1)),
    -- Encrypted payload (AES-256-GCM: nonce(12)|ciphertext|tag(16))
    Payload        BLOB,
    PayloadFmt     TEXT,        -- e.g. 'gcm-json-v1'
    PayloadVer     INTEGER NOT NULL DEFAULT 1,
    KeySetVersion  INTEGER NOT NULL DEFAULT 1,
    StackHash      TEXT,

    -- ✅ New: bulletproof dedupe + provenance + quarantine + archive
    PayloadHash    TEXT,        -- hex SHA-256 of encrypted payload (or canonical JSON) — must be consistent end-to-end
    IngestOrigin   TEXT    NOT NULL DEFAULT 'Runtime' CHECK (IngestOrigin IN ('Early','Runtime','Import')),
    IsQuarantined  INTEGER NOT NULL DEFAULT 0 CHECK (IsQuarantined IN (0,1)),
    QuarantineReason TEXT,      -- e.g. 'DPAPI_Fail','Malformed','KeyMismatch','Unknown'
    ArchivedAt     TEXT,        -- when set, row is considered archived/out of hot path

    UNIQUE (PayloadHash)
);

-- Helpful indexes (focused + partial)
-- Primary recency path
CREATE INDEX IF NOT EXISTS IX_Logs_WhenUtc_Desc           ON Logs(WhenUtc DESC);
-- Severity filters
CREATE INDEX IF NOT EXISTS IX_Logs_Level_WhenUtc_Desc     ON Logs(Level, WhenUtc DESC);
-- Source / Event drilldowns
CREATE INDEX IF NOT EXISTS IX_Logs_Source_WhenUtc_Desc    ON Logs(Source, WhenUtc DESC);
CREATE INDEX IF NOT EXISTS IX_Logs_EventCode_WhenUtc_Desc ON Logs(EventCode, WhenUtc DESC);
-- Sessions
CREATE INDEX IF NOT EXISTS IX_Logs_Session_WhenUtc_Desc   ON Logs(SessionId, WhenUtc DESC);
-- Crashes (partial)
CREATE INDEX IF NOT EXISTS IX_Logs_Crash_Recent           ON Logs(WhenUtc DESC) WHERE IsCrash = 1;
-- Quarantine (partial)
CREATE INDEX IF NOT EXISTS IX_Logs_Quarantine_Recent      ON Logs(WhenUtc DESC) WHERE IsQuarantined = 1;
-- Archive (partial)
CREATE INDEX IF NOT EXISTS IX_Logs_Archive_Recent         ON Logs(ArchivedAt DESC) WHERE ArchivedAt IS NOT NULL;
-- Stack hotspots
CREATE INDEX IF NOT EXISTS IX_Logs_StackHash_WhenUtc_Desc ON Logs(StackHash, WhenUtc DESC);

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
-- Views (point to new 'Category*' tables + helpful log views)
-- ---------------------------------------------------------------------------
CREATE VIEW IF NOT EXISTS vw_CurrentPassword AS
    SELECT h.*
    FROM CategoryItemPasswordHistory h
    JOIN (
        SELECT ItemId, MAX(CreatedAt) AS MaxCreated
        FROM CategoryItemPasswordHistory
        GROUP BY ItemId
    ) latest
      ON h.ItemId = latest.ItemId AND h.CreatedAt = latest.MaxCreated;

CREATE VIEW IF NOT EXISTS vw_CurrentPin AS
    SELECT h.*
    FROM CategoryItemPinHistory h
    JOIN (
        SELECT ItemId, MAX(CreatedAt) AS MaxCreated
        FROM CategoryItemPinHistory
        GROUP BY ItemId
    ) latest
      ON h.ItemId = latest.ItemId AND h.CreatedAt = latest.MaxCreated;

-- Helper severity mapping (inline via CASE in views below)
-- TRACE(0), DEBUG(10), INFO(20), WARN/WARNING(30), ERROR(40), FATAL(50)

CREATE VIEW IF NOT EXISTS vw_Logs_RecentActive AS
    SELECT *,
           CASE UPPER(Level)
               WHEN 'TRACE' THEN 0
               WHEN 'DEBUG' THEN 10
               WHEN 'INFO' THEN 20
               WHEN 'WARN' THEN 30
               WHEN 'WARNING' THEN 30
               WHEN 'ERROR' THEN 40
               WHEN 'FATAL' THEN 50
               ELSE 20
           END AS SeverityRank
    FROM Logs
    WHERE IsQuarantined = 0
      AND ArchivedAt IS NULL
    ORDER BY WhenUtc DESC;

CREATE VIEW IF NOT EXISTS vw_Logs_ErrorsRecent AS
    SELECT *
    FROM (
        SELECT *,
               CASE UPPER(Level)
                   WHEN 'TRACE' THEN 0
                   WHEN 'DEBUG' THEN 10
                   WHEN 'INFO' THEN 20
                   WHEN 'WARN' THEN 30
                   WHEN 'WARNING' THEN 30
                   WHEN 'ERROR' THEN 40
                   WHEN 'FATAL' THEN 50
                   ELSE 20
               END AS SeverityRank
        FROM Logs
        WHERE IsQuarantined = 0 AND ArchivedAt IS NULL
    )
    WHERE SeverityRank >= 40
    ORDER BY WhenUtc DESC;

CREATE VIEW IF NOT EXISTS vw_Logs_Quarantine AS
    SELECT *
    FROM Logs
    WHERE IsQuarantined = 1
    ORDER BY WhenUtc DESC;

CREATE VIEW IF NOT EXISTS vw_Logs_EarlyIngest AS
    SELECT *
    FROM Logs
    WHERE IngestOrigin = 'Early'
      AND IsQuarantined = 0
    ORDER BY WhenUtc DESC;

CREATE VIEW IF NOT EXISTS vw_Logs_Archive AS
    SELECT *
    FROM Logs
    WHERE ArchivedAt IS NOT NULL
    ORDER BY ArchivedAt DESC;

CREATE VIEW IF NOT EXISTS vw_Logs_StackHotspots AS
    SELECT StackHash,
           COUNT(*)      AS N,
           MAX(WhenUtc)  AS LastSeen,
           SUM(CASE WHEN IsQuarantined=1 THEN 1 ELSE 0 END) AS QuarantinedCount
    FROM Logs
    WHERE StackHash IS NOT NULL
      AND ArchivedAt IS NULL
    GROUP BY StackHash
    ORDER BY N DESC, LastSeen DESC;

COMMIT;

-- Optional tidy-up
-- VACUUM;
-- ANALYZE;
