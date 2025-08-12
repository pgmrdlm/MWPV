--
-- File generated with SQLiteStudio v3.4.4 on Mon Aug 11 09:41:56 2025
--
-- Text encoding used: System
--
PRAGMA foreign_keys = off;
BEGIN TRANSACTION;

-- Table: Catagory
CREATE TABLE Catagory (
    Catagory_Key         INTEGER PRIMARY KEY AUTOINCREMENT,
    Catagory_Name        TEXT    NOT NULL
                                 COLLATE NOCASE
                                 UNIQUE,
    Catagory_Description TEXT,
    IsActive             INTEGER NOT NULL
                                 DEFAULT 1
);

INSERT INTO Catagory (
                         Catagory_Key,
                         Catagory_Name,
                         Catagory_Description,
                         IsActive
                     )
                     VALUES (
                         2,
                         'Encryption',
                         'Encrypted local Files and or folders',
                         1
                     );

INSERT INTO Catagory (
                         Catagory_Key,
                         Catagory_Name,
                         Catagory_Description,
                         IsActive
                     )
                     VALUES (
                         3,
                         'Financial',
                         'Financial web sites or applications(Banking/Credit Card)',
                         1
                     );

INSERT INTO Catagory (
                         Catagory_Key,
                         Catagory_Name,
                         Catagory_Description,
                         IsActive
                     )
                     VALUES (
                         4,
                         'Applications',
                         'Computer/Phone application logins',
                         1
                     );

INSERT INTO Catagory (
                         Catagory_Key,
                         Catagory_Name,
                         Catagory_Description,
                         IsActive
                     )
                     VALUES (
                         5,
                         'Application Forums',
                         'Login to forums that support applications',
                         1
                     );

INSERT INTO Catagory (
                         Catagory_Key,
                         Catagory_Name,
                         Catagory_Description,
                         IsActive
                     )
                     VALUES (
                         6,
                         'Goverment',
                         'Any government web site login',
                         1
                     );

INSERT INTO Catagory (
                         Catagory_Key,
                         Catagory_Name,
                         Catagory_Description,
                         IsActive
                     )
                     VALUES (
                         7,
                         'Astro Forums',
                         'Logins for Astro forum web sites',
                         1
                     );

INSERT INTO Catagory (
                         Catagory_Key,
                         Catagory_Name,
                         Catagory_Description,
                         IsActive
                     )
                     VALUES (
                         8,
                         'Google Accounts',
                         'Logins for Gmail, google drive, or other google services',
                         1
                     );

INSERT INTO Catagory (
                         Catagory_Key,
                         Catagory_Name,
                         Catagory_Description,
                         IsActive
                     )
                     VALUES (
                         11,
                         'Non Google Email',
                         'Non Google Emails Logins',
                         1
                     );

INSERT INTO Catagory (
                         Catagory_Key,
                         Catagory_Name,
                         Catagory_Description,
                         IsActive
                     )
                     VALUES (
                         12,
                         'Political Forums',
                         'Political Forum Logins',
                         1
                     );

INSERT INTO Catagory (
                         Catagory_Key,
                         Catagory_Name,
                         Catagory_Description,
                         IsActive
                     )
                     VALUES (
                         13,
                         'new catagory for',
                         NULL,
                         1
                     );

INSERT INTO Catagory (
                         Catagory_Key,
                         Catagory_Name,
                         Catagory_Description,
                         IsActive
                     )
                     VALUES (
                         14,
                         'zzzzzzzz',
                         NULL,
                         1
                     );


-- Table: CatagoryItem
CREATE TABLE CatagoryItem (
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
    IsActive                          INTEGER NOT NULL
                                              DEFAULT 1,
    CatagoryItem_NbrSecurityQuestions INTEGER DEFAULT 0,
    UNIQUE (
        Catagory_Key,
        CatagoryItem_Name COLLATE NOCASE
    )
);


-- Table: CatagoryItemPasswordHistory
CREATE TABLE CatagoryItemPasswordHistory (
    PwHistId  INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId    INTEGER NOT NULL
                      REFERENCES CatagoryItem (ItemId) ON DELETE CASCADE,
    CreatedAt INTEGER NOT NULL,-- Unix epoch for fast sorting
    Version   INTEGER NOT NULL
                      DEFAULT 1,-- Crypto/key version
    Password  BLOB    NOT NULL,-- Encrypted envelope: version|nonce|tag|padLen|ciphertext
    PadLen    INTEGER-- Optional: if padding used in envelope
);


-- Table: CatagoryItemPinHistory
CREATE TABLE CatagoryItemPinHistory (
    PinHistId INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId    INTEGER NOT NULL
                      REFERENCES CatagoryItem (ItemId) ON DELETE CASCADE,
    CreatedAt INTEGER NOT NULL,-- Unix epoch for fast sorting
    Version   INTEGER NOT NULL
                      DEFAULT 1,-- Crypto/key version
    Pin       BLOB    NOT NULL,-- Encrypted envelope: version|nonce|tag|padLen|ciphertext
    PadLen    INTEGER-- Optional: if padding used in envelope
);


-- Table: CatagoryItemSecurityQuestions
CREATE TABLE CatagoryItemSecurityQuestions (
    SecQId   INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId   INTEGER NOT NULL
                     REFERENCES CatagoryItem (ItemId) ON DELETE CASCADE,
    Question TEXT    NOT NULL,
    Answer   BLOB    NOT NULL,
    UNIQUE (
        ItemId,
        Question COLLATE NOCASE
    )
);


-- Table: DbVersion
CREATE TABLE DbVersion (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Version     TEXT    NOT NULL,
    AppliedOn   TEXT    NOT NULL,
    Description TEXT,
    IsCurrent   INTEGER NOT NULL
                        CHECK (IsCurrent IN (0, 1) ) 
);

INSERT INTO DbVersion (
                          Id,
                          Version,
                          AppliedOn,
                          Description,
                          IsCurrent
                      )
                      VALUES (
                          1,
                          '1.0.0',
                          '2025-08-04 01:22:36',
                          'Initial schema creation',
                          1
                      );


-- Table: Logs
CREATE TABLE Logs (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    CreatedUtc    INTEGER NOT NULL,
    Level         TEXT    NOT NULL
                          CHECK (Level IN ('TRACE', 'DEBUG', 'INFO', 'WARN', 'ERROR', 'FATAL') ),
    Source        TEXT,
    EventCode     TEXT,
    CorrelationId TEXT,
    SessionId     TEXT,
    MachineId     TEXT,
    AppVersion    TEXT,
    IsCrash       INTEGER NOT NULL
                          DEFAULT 0,
    Payload       BLOB    NOT NULL,
    PayloadFmt    TEXT    NOT NULL
                          DEFAULT 'json+aesgcm',
    PayloadVer    INTEGER NOT NULL
                          DEFAULT 1,
    KeySetVersion INTEGER NOT NULL
                          DEFAULT 1,
    StackHash     TEXT,
    Reserved1     TEXT,
    Reserved2     TEXT
);


-- Index: idx_pin_history_itemid_createdat_desc
CREATE INDEX idx_pin_history_itemid_createdat_desc ON CatagoryItemPinHistory (
    ItemId,
    CreatedAt DESC
);


-- Index: idx_pw_history_itemid_createdat_desc
CREATE INDEX idx_pw_history_itemid_createdat_desc ON CatagoryItemPasswordHistory (
    ItemId,
    CreatedAt DESC
);


-- View: vw_CurrentPassword
CREATE VIEW vw_CurrentPassword AS
    SELECT h.*
      FROM CatagoryItemPasswordHistory h
           JOIN
           (
               SELECT ItemId,
                      MAX(CreatedAt) AS MaxCreated
                 FROM CatagoryItemPasswordHistory
                GROUP BY ItemId
           )
           latest ON h.ItemId = latest.ItemId AND 
                     h.CreatedAt = latest.MaxCreated;


-- View: vw_CurrentPin
CREATE VIEW vw_CurrentPin AS
    SELECT h.*
      FROM CatagoryItemPinHistory h
           JOIN
           (
               SELECT ItemId,
                      MAX(CreatedAt) AS MaxCreated
                 FROM CatagoryItemPinHistory
                GROUP BY ItemId
           )
           latest ON h.ItemId = latest.ItemId AND 
                     h.CreatedAt = latest.MaxCreated;


COMMIT TRANSACTION;
PRAGMA foreign_keys = on;
