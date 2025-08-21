/* ============================================================================
   MWPV - FRESH LOAD / Nuke-and-Pave SCRIPT
   ----------------------------------------------------------------------------
   ⚠️  DANGER: RUNNING THIS WILL DROP TABLES/VIEW(S) AND DELETE ALL DATA.
   ⚠️  PURPOSE: Use ONLY for a complete rebuild of the database (test or prod).
   ⚠️  CONSEQUENCE: ALL EXISTING DATA WILL BE LOST.

   Use your migration/upgrade script for normal changes.
   Make a backup before running this. You have been warned.
============================================================================ */

PRAGMA foreign_keys = OFF;
BEGIN TRANSACTION;

-- ---------------------------------------------------------------------------
-- DROP VIEWS (if they exist)  [views must go first]
-- ---------------------------------------------------------------------------
DROP VIEW IF EXISTS vw_CurrentPassword;
DROP VIEW IF EXISTS vw_CurrentPin;

-- ---------------------------------------------------------------------------
-- DROP TABLES (if they exist)
-- ---------------------------------------------------------------------------
DROP TABLE IF EXISTS AppSettings;
DROP TABLE IF EXISTS Logs;
DROP TABLE IF EXISTS DbVersion;
DROP TABLE IF EXISTS CatagoryItemSecurityQuestions;
DROP TABLE IF EXISTS CatagoryItemPinHistory;
DROP TABLE IF EXISTS CatagoryItemPasswordHistory;
DROP TABLE IF EXISTS CatagoryItem;
DROP TABLE IF EXISTS Catagory;

COMMIT;
PRAGMA foreign_keys = ON;

-- =============================================================================
-- CREATE OBJECTS (all IF NOT EXISTS)
-- =============================================================================
BEGIN TRANSACTION;

-- ---------------------------------------------------------------------------
-- Table: Catagory
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Catagory (
    Catagory_Key         INTEGER PRIMARY KEY AUTOINCREMENT,
    Catagory_Name        TEXT    NOT NULL COLLATE NOCASE UNIQUE,
    Catagory_Description TEXT,
    IsActive             INTEGER NOT NULL DEFAULT 1
);

-- Seed default categories (idempotent)
INSERT INTO Catagory (Catagory_Name, Catagory_Description, IsActive)
SELECT 'Encryption', 'Encrypted local Files and or folders', 1
WHERE NOT EXISTS (SELECT 1 FROM Catagory WHERE Catagory_Name = 'Encryption');

INSERT INTO Catagory (Catagory_Name, Catagory_Description, IsActive)
SELECT 'Financial', 'Financial web sites or applications (Banking/Credit Card)', 1
WHERE NOT EXISTS (SELECT 1 FROM Catagory WHERE Catagory_Name = 'Financial');

INSERT INTO Catagory (Catagory_Name, Catagory_Description, IsActive)
SELECT 'Applications', 'Computer/Phone application logins', 1
WHERE NOT EXISTS (SELECT 1 FROM Catagory WHERE Catagory_Name = 'Applications');

INSERT INTO Catagory (Catagory_Name, Catagory_Description, IsActive)
SELECT 'Application Forums', 'Login to forums that support applications', 1
WHERE NOT EXISTS (SELECT 1 FROM Catagory WHERE Catagory_Name = 'Application Forums');

INSERT INTO Catagory (Catagory_Name, Catagory_Description, IsActive)
SELECT 'Goverment', 'Any government web site login', 1
WHERE NOT EXISTS (SELECT 1 FROM Catagory WHERE Catagory_Name = 'Goverment');

INSERT INTO Catagory (Catagory_Name, Catagory_Description, IsActive)
SELECT 'Astro Forums', 'Logins for Astro forum web sites', 1
WHERE NOT EXISTS (SELECT 1 FROM Catagory WHERE Catagory_Name = 'Astro Forums');

INSERT INTO Catagory (Catagory_Name, Catagory_Description, IsActive)
SELECT 'Google Accounts', 'Logins for Gmail, Google Drive, or other Google services', 1
WHERE NOT EXISTS (SELECT 1 FROM Catagory WHERE Catagory_Name = 'Google Accounts');

INSERT INTO Catagory (Catagory_Name, Catagory_Description, IsActive)
SELECT 'Non Google Email', 'Non Google Email logins', 1
WHERE NOT EXISTS (SELECT 1 FROM Catagory WHERE Catagory_Name = 'Non Google Email');

INSERT INTO Catagory (Catagory_Name, Catagory_Description, IsActive)
SELECT 'Political Forums', 'Political forum logins', 1
WHERE NOT EXISTS (SELECT 1 FROM Catagory WHERE Catagory_Name = 'Political Forums');

-- ---------------------------------------------------------------------------
-- Table: CatagoryItem
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CatagoryItem (
    ItemId                            INTEGER PRIMARY KEY AUTOINCREMENT,
    Catagory_Key                      INTEGER NOT NULL
                                              REFERENCES Catagory (Catagory_Key) ON DELETE CASCADE,
    CatagoryItem_Name                 TEXT    NOT NULL,
    CatagoryItem_Password             BLOB,
    CatagoryItem_Pin                  BLOB,
    CatagoryItem_AcctNbr              BLOB,
    CatagoryId_LicenceKey             BLOB,
    CatagoryItem_LoginId              BLOB,
    CatagoryItem_Email                BLOB,
    CatagoryItem_UpdateDate           TEXT    NOT NULL,
    CatagoryItem_Notes                TEXT,
    IsActive                          INTEGER NOT NULL DEFAULT 1,
    CatagoryItem_NbrSecurityQuestions INTEGER DEFAULT 0,
    UNIQUE (Catagory_Key, CatagoryItem_Name COLLATE NOCASE)
);

-- ---------------------------------------------------------------------------
-- Table: CatagoryItemPasswordHistory
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CatagoryItemPasswordHistory (
    PwHistId  INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId    INTEGER NOT NULL REFERENCES CatagoryItem (ItemId) ON DELETE CASCADE,
    CreatedAt INTEGER NOT NULL,               -- Unix epoch for fast sorting
    Version   INTEGER NOT NULL DEFAULT 1,     -- Crypto/key version
    Password  BLOB    NOT NULL,               -- Encrypted envelope: version|nonce|tag|padLen|ciphertext
    PadLen    INTEGER                          -- Optional: if padding used in envelope
);

-- ---------------------------------------------------------------------------
-- Table: CatagoryItemPinHistory
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CatagoryItemPinHistory (
    PinHistId INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId    INTEGER NOT NULL REFERENCES CatagoryItem (ItemId) ON DELETE CASCADE,
    CreatedAt INTEGER NOT NULL,               -- Unix epoch for fast sorting
    Version   INTEGER NOT NULL DEFAULT 1,     -- Crypto/key version
    Pin       BLOB    NOT NULL,               -- Encrypted envelope: version|nonce|tag|padLen|ciphertext
    PadLen    INTEGER                          -- Optional: if padding used in envelope
);

-- ---------------------------------------------------------------------------
-- Table: CatagoryItemSecurityQuestions
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CatagoryItemSecurityQuestions (
    SecQId   INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId   INTEGER NOT NULL REFERENCES CatagoryItem (ItemId) ON DELETE CASCADE,
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

-- Optional initial DbVersion row (idempotent)
INSERT INTO DbVersion (Version, AppliedOn, Description, IsCurrent)
SELECT '1.0.0', strftime('%Y-%m-%d %H:%M:%S','now'), 'Initial schema creation', 1
WHERE NOT EXISTS (SELECT 1 FROM DbVersion);

-- ---------------------------------------------------------------------------
-- Table: Logs (canonical v2 schema)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Logs (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    WhenUtc       TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now')),
    CreatedUtc    TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now')),
    Level         TEXT    NOT NULL CHECK (UPPER(Level) IN ('TRACE','DEBUG','INFO','WARN','ERROR','FATAL','WARNING')),
    Source        TEXT,
    EventCode     TEXT,
    SessionId     TEXT    NOT NULL DEFAULT '',
    MachineId     TEXT,
    AppVersion    TEXT    NOT NULL DEFAULT '',
    IsCrash       INTEGER NOT NULL DEFAULT 0 CHECK (IsCrash IN (0,1)),
    Payload       BLOB,
    PayloadFmt    TEXT,
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
-- AppSettings (Key/Value store)
-- ---------------------------------------------------------------------------
/*
================================================================================
AppSettings Table
--------------------------------------------------------------------------------
Purpose:
    Stores application settings in a flexible, key–value format.
    Each setting is stored as its own row, with a unique Key and optional Scope.

Typical Usage:
    - Store boolean flags (e.g., Portable.Enabled)
    - Store file paths (e.g., Portable.DbPath)
    - Store configuration arrays in JSON (e.g., Portable.SqlCatalog)

Example: Portable Mode Configuration
-------------------------------------
INSERT INTO AppSettings (Key, Scope, Value, ValueType, Description, LastUpdatedUtc)
VALUES ('Portable.Enabled', 'Global', 'true', 'bool', 'Run application in portable mode', strftime('%s','now'));

SELECT * FROM AppSettings WHERE Key LIKE 'Portable.%';
================================================================================
*/
CREATE TABLE IF NOT EXISTS AppSettings (
    Key TEXT NOT NULL,                         -- e.g., 'Portable.Enabled', 'Logging.Level'
    Scope TEXT NOT NULL DEFAULT 'Global',      -- e.g., 'Global', 'User', 'Machine'
    Value TEXT NOT NULL,                       -- string or JSON text
    ValueType TEXT NOT NULL,                   -- 'string' | 'int' | 'bool' | 'json'
    Description TEXT,                          -- optional
    LastUpdatedUtc INTEGER,                    -- Unix epoch (UTC)
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
-- Views (recreate if missing)
-- ---------------------------------------------------------------------------
CREATE VIEW IF NOT EXISTS vw_CurrentPassword AS
    SELECT h.*
    FROM CatagoryItemPasswordHistory h
    JOIN (
        SELECT ItemId, MAX(CreatedAt) AS MaxCreated
        FROM CatagoryItemPasswordHistory
        GROUP BY ItemId
    ) latest
      ON h.ItemId = latest.ItemId AND h.CreatedAt = latest.MaxCreated;

CREATE VIEW IF NOT EXISTS vw_CurrentPin AS
    SELECT h.*
    FROM CatagoryItemPinHistory h
    JOIN (
        SELECT ItemId, MAX(CreatedAt) AS MaxCreated
        FROM CatagoryItemPinHistory
        GROUP BY ItemId
    ) latest
      ON h.ItemId = latest.ItemId AND h.CreatedAt = latest.MaxCreated;

COMMIT;

-- Optional: compact after rebuild
-- VACUUM;
