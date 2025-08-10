--
-- File generated with SQLiteStudio v3.4.4 on Mon Aug 4 18:46:45 2025
--
-- Text encoding used: System
--
PRAGMA foreign_keys = off;
BEGIN TRANSACTION;

-- Table: Catagory
CREATE TABLE Catagory (
    Catagory_Key                INTEGER PRIMARY KEY AUTOINCREMENT
                                        UNIQUE
                                        NOT NULL,
    Catagory_Name               TEXT    NOT NULL,
    Catagory_Description        TEXT,
    CatagoryItem_ActiveInactive TEXT    DEFAULT A
                                        NOT NULL
);

INSERT INTO Catagory (
                         Catagory_Key,
                         Catagory_Name,
                         Catagory_Description,
                         CatagoryItem_ActiveInactive
                     )
                     VALUES (
                         2,
                         'Encryption',
                         'Encrypted local Files and or folders',
                         'A'
                     );

INSERT INTO Catagory (
                         Catagory_Key,
                         Catagory_Name,
                         Catagory_Description,
                         CatagoryItem_ActiveInactive
                     )
                     VALUES (
                         3,
                         'Financial',
                         'Fipnancial web sites or applications(Banking/Credit Card)',
                         'A'
                     );

INSERT INTO Catagory (
                         Catagory_Key,
                         Catagory_Name,
                         Catagory_Description,
                         CatagoryItem_ActiveInactive
                     )
                     VALUES (
                         4,
                         'Applications',
                         'Computer/Phone application logins',
                         'A'
                     );

INSERT INTO Catagory (
                         Catagory_Key,
                         Catagory_Name,
                         Catagory_Description,
                         CatagoryItem_ActiveInactive
                     )
                     VALUES (
                         5,
                         'Application Forums',
                         'Login to forums that support applications ',
                         'A'
                     );

INSERT INTO Catagory (
                         Catagory_Key,
                         Catagory_Name,
                         Catagory_Description,
                         CatagoryItem_ActiveInactive
                     )
                     VALUES (
                         6,
                         'Goverment',
                         'Any goverment web site login',
                         'A'
                     );

INSERT INTO Catagory (
                         Catagory_Key,
                         Catagory_Name,
                         Catagory_Description,
                         CatagoryItem_ActiveInactive
                     )
                     VALUES (
                         7,
                         'Astro Forums',
                         'Logins for Astro forum web sites',
                         'A'
                     );

INSERT INTO Catagory (
                         Catagory_Key,
                         Catagory_Name,
                         Catagory_Description,
                         CatagoryItem_ActiveInactive
                     )
                     VALUES (
                         8,
                         'Google Accounts',
                         'Logins for Gmail, google drive, or other google services',
                         'A'
                     );

INSERT INTO Catagory (
                         Catagory_Key,
                         Catagory_Name,
                         Catagory_Description,
                         CatagoryItem_ActiveInactive
                     )
                     VALUES (
                         11,
                         'Non Google Email',
                         'Non Google Emails Logins',
                         'A'
                     );

INSERT INTO Catagory (
                         Catagory_Key,
                         Catagory_Name,
                         Catagory_Description,
                         CatagoryItem_ActiveInactive
                     )
                     VALUES (
                         12,
                         'Political Forums',
                         'Political Forum Logins',
                         'A'
                     );


-- Table: CatagoryItem
CREATE TABLE CatagoryItem (
    Catagory_Key                      INTEGER REFERENCES Catagory (Catagory_Key) 
                                              NOT NULL,
    CatagoryItem_Name                 TEXT    NOT NULL,
    CatagoryItem_Password             TEXT    DEFAULT (0) 
                                              UNIQUE,
    CatagoryItem_Pin                  TEXT,
    CatagoryItem_AcctNbr              TEXT,
    CatagoryId_LicenceKey             TEXT,
    CatagoryItem_LoginId              TEXT,
    CatagoryItem_Email,
    CatagoryItem_UpdateDate           TEXT    NOT NULL,
    CatagoryItem_Notes                TEXT,
    CatagoryItem_ActiveInd            TEXT    NOT NULL
                                              DEFAULT Y,
    CatagoryItem_NbrSecurityQuestions INTEGER AS (0),
    PRIMARY KEY (
        Catagory_Key,
        CatagoryItem_Name
    )
);


-- Table: CatagoryItemPasswordHistory
CREATE TABLE CatagoryItemPasswordHistory (
    Catagory_Key                          INTEGER REFERENCES CatagoryItem (Catagory_Key) 
                                                  NOT NULL,
    CatagoryItem_Name                     TEXT    REFERENCES CatagoryItem (CatagoryItem_Name) 
                                                  NOT NULL,
    CatagoryItemPasswordHistory_DateAdded TEXT    NOT NULL,
    CatagoryItemPasswordHistory_Password  TEXT    NOT NULL,
    PRIMARY KEY (
        Catagory_Key,
        CatagoryItem_Name,
        CatagoryItemPasswordHistory_DateAdded
    )
);


-- Table: CatagoryItemPinHistory
CREATE TABLE CatagoryItemPinHistory (
    Catagory_Key                     INTEGER REFERENCES CatagoryItem (Catagory_Key) 
                                             NOT NULL,
    CatagoryItem_Name                TEXT    REFERENCES CatagoryItem (CatagoryItem_Name) 
                                             NOT NULL,
    CatagoryItemPinHistory_DateAdded TEXT    NOT NULL,
    CatagoryItemPinHistory_Pin       TEXT    NOT NULL,
    PRIMARY KEY (
        Catagory_Key,
        CatagoryItem_Name,
        CatagoryItemPinHistory_DateAdded
    )
);


-- Table: CatagoryItemSecurityQuestions
CREATE TABLE CatagoryItemSecurityQuestions (
    Catagory_Key                          INTEGER NOT NULL
                                                  REFERENCES CatagoryItem (Catagory_Key),
    CatagoryItem_Name                     TEXT    REFERENCES CatagoryItem (CatagoryItem_Name) 
                                                  NOT NULL,
    CatagoryItemSecurityQuestion_Question TEXT    NOT NULL,
    CatagoryItemSecurityQuestion_Answer   TEXT    NOT NULL
);


-- Table: DbVersion
CREATE TABLE DbVersion (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Version     TEXT    NOT NULL,-- Semantic versioning: e.g., '1.0.0', '1.1.2'
    AppliedOn   TEXT    NOT NULL,-- ISO 8601 format timestamp: '2025-08-04T21:15:00Z'
    Description TEXT,-- Optional: short description of what changed
    IsCurrent   INTEGER NOT NULL-- 0 = false, 1 = true; only one row should have 1
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


COMMIT TRANSACTION;
PRAGMA foreign_keys = on;
