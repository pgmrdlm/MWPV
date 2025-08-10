--
-- File generated with SQLiteStudio v3.4.4 on Sun Aug 10 16:31:24 2025
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
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    CreatedUtc TEXT    NOT NULL,
    Level      TEXT    NOT NULL,
    Source     TEXT,
    EventCode  TEXT,
    SessionId  TEXT,
    MachineId  TEXT,
    AppVersion TEXT,
    IsCrash    INTEGER NOT NULL
                       DEFAULT 0,
    Payload    BLOB    NOT NULL,
    PayloadFmt TEXT    NOT NULL
                       DEFAULT 'json+aesgcm',
    StackHash  TEXT,
    Reserved1  TEXT,
    Reserved2  TEXT
);

INSERT INTO Logs (
                     Id,
                     CreatedUtc,
                     Level,
                     Source,
                     EventCode,
                     SessionId,
                     MachineId,
                     AppVersion,
                     IsCrash,
                     Payload,
                     PayloadFmt,
                     StackHash,
                     Reserved1,
                     Reserved2
                 )
                 VALUES (
                     1,
                     '2025-08-09T17:54:07.9801895Z',
                     'Error',
                     'ExportService.ExportEncryptedDbAsync():70',
                     'ABEND_EXPORT',
                     '',
                     'F519B045DE8FF1F4',
                     '1.0.0.0',
                     0,
                     '{"message":"Failed to export encrypted database","stage":"export","severity":"Error","caller":{"member":"ExportEncryptedDbAsync","file":"C:\\Users\\pgmrd\\My Drive\\MWPV\\MWPV\\Services\\ExportService.cs","line":70,"location":"ExportService.ExportEncryptedDbAsync():70"},"exception":{"type":"IOException","message":"The process cannot access the file \u0027C:\\Users\\pgmrd\\AppData\\Local\\MWPV\\MWPV.db\u0027 because it is being used by another process.","stackTrace":"   at Microsoft.Win32.SafeHandles.SafeFileHandle.CreateFile(String fullPath, FileMode mode, FileAccess access, FileShare share, FileOptions options)\r\n   at Microsoft.Win32.SafeHandles.SafeFileHandle.Open(String fullPath, FileMode mode, FileAccess access, FileShare share, FileOptions options, Int64 preallocationSize, Nullable\u00601 unixCreateMode)\r\n   at System.IO.Strategies.OSFileStreamStrategy..ctor(String path, FileMode mode, FileAccess access, FileShare share, FileOptions options, Int64 preallocationSize, Nullable\u00601 unixCreateMode)\r\n   at System.IO.Strategies.FileStreamHelpers.ChooseStrategyCore(String path, FileMode mode, FileAccess access, FileShare share, FileOptions options, Int64 preallocationSize, Nullable\u00601 unixCreateMode)\r\n   at System.IO.FileStream..ctor(String path, FileMode mode, FileAccess access, FileShare share)\r\n   at Utilities.Services.ExportService.CopyFileWithRetryAsync(String src, String dest, Boolean overwrite, CancellationToken ct) in C:\\Users\\pgmrd\\My Drive\\MWPV\\MWPV\\Services\\ExportService.cs:line 118\r\n   at Utilities.Services.ExportService.ExportEncryptedDbAsync(String dbPath, Func\u00601 openAppConnection, String defaultFileName, CancellationToken ct) in C:\\Users\\pgmrd\\My Drive\\MWPV\\MWPV\\Services\\ExportService.cs:line 57"},"environment":{"utc":"2025-08-09T17:54:07.2409433Z","machine":"LAPTOP-EKE0KKRA","processId":17748,"os":"Microsoft Windows NT 10.0.22631.0","appVersion":"1.0.0.0"},"activityId":"242f03f8138c48fc8e4b6af1c9801737"}',
                     'json+aesgcm',
                     '95A6AB02',
                     NULL,
                     NULL
                 );

INSERT INTO Logs (
                     Id,
                     CreatedUtc,
                     Level,
                     Source,
                     EventCode,
                     SessionId,
                     MachineId,
                     AppVersion,
                     IsCrash,
                     Payload,
                     PayloadFmt,
                     StackHash,
                     Reserved1,
                     Reserved2
                 )
                 VALUES (
                     2,
                     '2025-08-09T19:26:50.1677273Z',
                     'Info',
                     'App.xaml.OnStartup():70',
                     'INFO',
                     '1cd8ee1a52b9494ba7bba1fcd4923bcf',
                     'F519B045DE8FF1F4',
                     '1.0.0.0',
                     0,
                     '{"message":"Logging online (v1.0.0.0)","stage":"startup","severity":"Info","caller":{"member":"OnStartup","file":"C:\\Users\\pgmrd\\My Drive\\MWPV\\MWPV\\App.xaml.cs","line":70,"location":"App.xaml.OnStartup():70"},"environment":{"utc":"2025-08-09T19:26:49.5425898Z","machine":"LAPTOP-EKE0KKRA","processId":17492,"os":"Microsoft Windows NT 10.0.22631.0","appVersion":"1.0.0.0"}}',
                     'json+aesgcm',
                     '',
                     NULL,
                     NULL
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
