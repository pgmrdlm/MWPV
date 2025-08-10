-- Updated DB Creation Script with Schema Improvements
-- Generated: 2025-08-08
PRAGMA foreign_keys = OFF;
BEGIN TRANSACTION;

-- Drop tables in reverse dependency order
DROP TABLE IF EXISTS CatagoryItemPasswordHistory;
DROP TABLE IF EXISTS CatagoryItemPinHistory;
DROP TABLE IF EXISTS CatagoryItemSecurityQuestions;
DROP TABLE IF EXISTS CatagoryItem;
DROP TABLE IF EXISTS Catagory;
DROP TABLE IF EXISTS DbVersion;

-- Table: Catagory
CREATE TABLE Catagory (
    Catagory_Key INTEGER PRIMARY KEY AUTOINCREMENT,
    Catagory_Name TEXT NOT NULL COLLATE NOCASE UNIQUE,
    Catagory_Description TEXT,
    IsActive INTEGER NOT NULL DEFAULT 1
);

-- Seed data
INSERT INTO Catagory (Catagory_Key, Catagory_Name, Catagory_Description, IsActive) VALUES
(2, 'Encryption', 'Encrypted local Files and or folders', 1),
(3, 'Financial', 'Financial web sites or applications(Banking/Credit Card)', 1),
(4, 'Applications', 'Computer/Phone application logins', 1),
(5, 'Application Forums', 'Login to forums that support applications', 1),
(6, 'Goverment', 'Any government web site login', 1),
(7, 'Astro Forums', 'Logins for Astro forum web sites', 1),
(8, 'Google Accounts', 'Logins for Gmail, google drive, or other google services', 1),
(11, 'Non Google Email', 'Non Google Emails Logins', 1),
(12, 'Political Forums', 'Political Forum Logins', 1);

-- Table: CatagoryItem
CREATE TABLE CatagoryItem (
    ItemId INTEGER PRIMARY KEY AUTOINCREMENT,
    Catagory_Key INTEGER NOT NULL REFERENCES Catagory (Catagory_Key) ON DELETE CASCADE,
    CatagoryItem_Name TEXT NOT NULL,
    CatagoryItem_Password TEXT,
    CatagoryItem_Pin TEXT,
    CatagoryItem_AcctNbr TEXT,
    CatagoryId_LicenceKey TEXT,
    CatagoryItem_LoginId TEXT,
    CatagoryItem_Email TEXT,
    CatagoryItem_UpdateDate TEXT NOT NULL,
    CatagoryItem_Notes TEXT,
    IsActive INTEGER NOT NULL DEFAULT 1,
    CatagoryItem_NbrSecurityQuestions INTEGER DEFAULT 0,
    UNIQUE (Catagory_Key, CatagoryItem_Name COLLATE NOCASE)
);

-- Table: CatagoryItemPasswordHistory
CREATE TABLE CatagoryItemPasswordHistory (
    PwHistId INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId INTEGER NOT NULL REFERENCES CatagoryItem (ItemId) ON DELETE CASCADE,
    DateAdded TEXT NOT NULL,
    Password TEXT NOT NULL
);

-- Table: CatagoryItemPinHistory
CREATE TABLE CatagoryItemPinHistory (
    PinHistId INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId INTEGER NOT NULL REFERENCES CatagoryItem (ItemId) ON DELETE CASCADE,
    DateAdded TEXT NOT NULL,
    Pin TEXT NOT NULL
);

-- Table: CatagoryItemSecurityQuestions
CREATE TABLE CatagoryItemSecurityQuestions (
    SecQId INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId INTEGER NOT NULL REFERENCES CatagoryItem (ItemId) ON DELETE CASCADE,
    Question TEXT NOT NULL,
    Answer TEXT NOT NULL,
    UNIQUE (ItemId, Question COLLATE NOCASE)
);

-- Table: DbVersion
CREATE TABLE DbVersion (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Version TEXT NOT NULL,
    AppliedOn TEXT NOT NULL,
    Description TEXT,
    IsCurrent INTEGER NOT NULL CHECK (IsCurrent IN (0,1))
);

INSERT INTO DbVersion (Id, Version, AppliedOn, Description, IsCurrent) VALUES
(1, '1.0.0', '2025-08-04 01:22:36', 'Initial schema creation', 1);

COMMIT TRANSACTION;
PRAGMA foreign_keys = ON;
