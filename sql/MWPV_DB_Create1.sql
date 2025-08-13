--
-- File generated with SQLiteStudio v3.4.4 on Tue Aug 12 13:27:22 2025
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
    Payload       TEXT    NOT NULL,
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

INSERT INTO Logs (
                     Id,
                     CreatedUtc,
                     Level,
                     Source,
                     EventCode,
                     CorrelationId,
                     SessionId,
                     MachineId,
                     AppVersion,
                     IsCrash,
                     Payload,
                     PayloadFmt,
                     PayloadVer,
                     KeySetVersion,
                     StackHash,
                     Reserved1,
                     Reserved2
                 )
                 VALUES (
                     1,
                     1755014744,
                     'INFO',
                     'post-login',
                     'EARLY_LOGIN_FAILURES_PENDING',
                     NULL,
                     '4e9db57bd37940a6a0a4997026205d98',
                     'f519b045de8ff1f411f7522cc3617ef2aae017c28dc9f3fee6dd3a2c180194c2',
                     '1.0.0.0',
                     0,
                     '{"pending":3,"dir":"C:\\Users\\pgmrd\\AppData\\Local\\MWPV\\early"}',
                     'json+aesgcm',
                     1,
                     1,
                     '',
                     NULL,
                     NULL
                 );

INSERT INTO Logs (
                     Id,
                     CreatedUtc,
                     Level,
                     Source,
                     EventCode,
                     CorrelationId,
                     SessionId,
                     MachineId,
                     AppVersion,
                     IsCrash,
                     Payload,
                     PayloadFmt,
                     PayloadVer,
                     KeySetVersion,
                     StackHash,
                     Reserved1,
                     Reserved2
                 )
                 VALUES (
                     2,
                     1755014744,
                     'INFO',
                     'SetupPasswordAndKeyFile',
                     'EARLY_LOGIN_FAILURE',
                     NULL,
                     '4e9db57bd37940a6a0a4997026205d98',
                     'f519b045de8ff1f411f7522cc3617ef2aae017c28dc9f3fee6dd3a2c180194c2',
                     '1.0.0.0',
                     0,
                     '{"earlyFail":"KeyfileMissingOrCorrupt","detail":"InvalidPasswordOrKeyFile: Invalid key-file password or wrong key file. Path=\u0027C:\\Users\\pgmrd\\Desktop\\Key.7z\u0027","occurredUtc":"2025-08-12T16:03:46.5071358Z"}',
                     'json+aesgcm',
                     1,
                     1,
                     '',
                     NULL,
                     NULL
                 );

INSERT INTO Logs (
                     Id,
                     CreatedUtc,
                     Level,
                     Source,
                     EventCode,
                     CorrelationId,
                     SessionId,
                     MachineId,
                     AppVersion,
                     IsCrash,
                     Payload,
                     PayloadFmt,
                     PayloadVer,
                     KeySetVersion,
                     StackHash,
                     Reserved1,
                     Reserved2
                 )
                 VALUES (
                     3,
                     1755014744,
                     'INFO',
                     'EarlyLoginIngest',
                     'EARLY_ELOG_DELETE_ATTEMPT',
                     NULL,
                     '4e9db57bd37940a6a0a4997026205d98',
                     'f519b045de8ff1f411f7522cc3617ef2aae017c28dc9f3fee6dd3a2c180194c2',
                     '1.0.0.0',
                     0,
                     '{"path":"C:\\Users\\pgmrd\\AppData\\Local\\MWPV\\early\\20250812160346523-KeyfileMissingOrCorrupt.elog"}',
                     'json+aesgcm',
                     1,
                     1,
                     '',
                     NULL,
                     NULL
                 );

INSERT INTO Logs (
                     Id,
                     CreatedUtc,
                     Level,
                     Source,
                     EventCode,
                     CorrelationId,
                     SessionId,
                     MachineId,
                     AppVersion,
                     IsCrash,
                     Payload,
                     PayloadFmt,
                     PayloadVer,
                     KeySetVersion,
                     StackHash,
                     Reserved1,
                     Reserved2
                 )
                 VALUES (
                     4,
                     1755014744,
                     'INFO',
                     'EarlyLoginIngest',
                     'EARLY_ELOG_DELETED',
                     NULL,
                     '4e9db57bd37940a6a0a4997026205d98',
                     'f519b045de8ff1f411f7522cc3617ef2aae017c28dc9f3fee6dd3a2c180194c2',
                     '1.0.0.0',
                     0,
                     '{"path":"C:\\Users\\pgmrd\\AppData\\Local\\MWPV\\early\\20250812160346523-KeyfileMissingOrCorrupt.elog"}',
                     'json+aesgcm',
                     1,
                     1,
                     '',
                     NULL,
                     NULL
                 );

INSERT INTO Logs (
                     Id,
                     CreatedUtc,
                     Level,
                     Source,
                     EventCode,
                     CorrelationId,
                     SessionId,
                     MachineId,
                     AppVersion,
                     IsCrash,
                     Payload,
                     PayloadFmt,
                     PayloadVer,
                     KeySetVersion,
                     StackHash,
                     Reserved1,
                     Reserved2
                 )
                 VALUES (
                     5,
                     1755014744,
                     'INFO',
                     'SetupPasswordAndKeyFile',
                     'EARLY_LOGIN_FAILURE',
                     NULL,
                     '4e9db57bd37940a6a0a4997026205d98',
                     'f519b045de8ff1f411f7522cc3617ef2aae017c28dc9f3fee6dd3a2c180194c2',
                     '1.0.0.0',
                     0,
                     '{"earlyFail":"KeyfileMissingOrCorrupt","detail":"InvalidPasswordOrKeyFile: Invalid key-file password or wrong key file. Path=\u0027C:\\Users\\pgmrd\\Desktop\\Key.7z\u0027","occurredUtc":"2025-08-12T16:03:52.2639318Z"}',
                     'json+aesgcm',
                     1,
                     1,
                     '',
                     NULL,
                     NULL
                 );

INSERT INTO Logs (
                     Id,
                     CreatedUtc,
                     Level,
                     Source,
                     EventCode,
                     CorrelationId,
                     SessionId,
                     MachineId,
                     AppVersion,
                     IsCrash,
                     Payload,
                     PayloadFmt,
                     PayloadVer,
                     KeySetVersion,
                     StackHash,
                     Reserved1,
                     Reserved2
                 )
                 VALUES (
                     6,
                     1755014744,
                     'INFO',
                     'EarlyLoginIngest',
                     'EARLY_ELOG_DELETE_ATTEMPT',
                     NULL,
                     '4e9db57bd37940a6a0a4997026205d98',
                     'f519b045de8ff1f411f7522cc3617ef2aae017c28dc9f3fee6dd3a2c180194c2',
                     '1.0.0.0',
                     0,
                     '{"path":"C:\\Users\\pgmrd\\AppData\\Local\\MWPV\\early\\20250812160352264-KeyfileMissingOrCorrupt.elog"}',
                     'json+aesgcm',
                     1,
                     1,
                     '',
                     NULL,
                     NULL
                 );

INSERT INTO Logs (
                     Id,
                     CreatedUtc,
                     Level,
                     Source,
                     EventCode,
                     CorrelationId,
                     SessionId,
                     MachineId,
                     AppVersion,
                     IsCrash,
                     Payload,
                     PayloadFmt,
                     PayloadVer,
                     KeySetVersion,
                     StackHash,
                     Reserved1,
                     Reserved2
                 )
                 VALUES (
                     7,
                     1755014744,
                     'INFO',
                     'EarlyLoginIngest',
                     'EARLY_ELOG_DELETED',
                     NULL,
                     '4e9db57bd37940a6a0a4997026205d98',
                     'f519b045de8ff1f411f7522cc3617ef2aae017c28dc9f3fee6dd3a2c180194c2',
                     '1.0.0.0',
                     0,
                     '{"path":"C:\\Users\\pgmrd\\AppData\\Local\\MWPV\\early\\20250812160352264-KeyfileMissingOrCorrupt.elog"}',
                     'json+aesgcm',
                     1,
                     1,
                     '',
                     NULL,
                     NULL
                 );

INSERT INTO Logs (
                     Id,
                     CreatedUtc,
                     Level,
                     Source,
                     EventCode,
                     CorrelationId,
                     SessionId,
                     MachineId,
                     AppVersion,
                     IsCrash,
                     Payload,
                     PayloadFmt,
                     PayloadVer,
                     KeySetVersion,
                     StackHash,
                     Reserved1,
                     Reserved2
                 )
                 VALUES (
                     8,
                     1755014744,
                     'INFO',
                     'SetupPasswordAndKeyFile',
                     'EARLY_LOGIN_FAILURE',
                     NULL,
                     '4e9db57bd37940a6a0a4997026205d98',
                     'f519b045de8ff1f411f7522cc3617ef2aae017c28dc9f3fee6dd3a2c180194c2',
                     '1.0.0.0',
                     0,
                     '{"earlyFail":"KeyfileMissingOrCorrupt","detail":"InvalidPasswordOrKeyFile: Invalid key-file password or wrong key file. Path=\u0027C:\\Users\\pgmrd\\Desktop\\Key.7z\u0027","occurredUtc":"2025-08-12T16:03:54.0423398Z"}',
                     'json+aesgcm',
                     1,
                     1,
                     '',
                     NULL,
                     NULL
                 );

INSERT INTO Logs (
                     Id,
                     CreatedUtc,
                     Level,
                     Source,
                     EventCode,
                     CorrelationId,
                     SessionId,
                     MachineId,
                     AppVersion,
                     IsCrash,
                     Payload,
                     PayloadFmt,
                     PayloadVer,
                     KeySetVersion,
                     StackHash,
                     Reserved1,
                     Reserved2
                 )
                 VALUES (
                     9,
                     1755014744,
                     'INFO',
                     'EarlyLoginIngest',
                     'EARLY_ELOG_DELETE_ATTEMPT',
                     NULL,
                     '4e9db57bd37940a6a0a4997026205d98',
                     'f519b045de8ff1f411f7522cc3617ef2aae017c28dc9f3fee6dd3a2c180194c2',
                     '1.0.0.0',
                     0,
                     '{"path":"C:\\Users\\pgmrd\\AppData\\Local\\MWPV\\early\\20250812160354042-KeyfileMissingOrCorrupt.elog"}',
                     'json+aesgcm',
                     1,
                     1,
                     '',
                     NULL,
                     NULL
                 );

INSERT INTO Logs (
                     Id,
                     CreatedUtc,
                     Level,
                     Source,
                     EventCode,
                     CorrelationId,
                     SessionId,
                     MachineId,
                     AppVersion,
                     IsCrash,
                     Payload,
                     PayloadFmt,
                     PayloadVer,
                     KeySetVersion,
                     StackHash,
                     Reserved1,
                     Reserved2
                 )
                 VALUES (
                     10,
                     1755014745,
                     'INFO',
                     'EarlyLoginIngest',
                     'EARLY_ELOG_DELETED',
                     NULL,
                     '4e9db57bd37940a6a0a4997026205d98',
                     'f519b045de8ff1f411f7522cc3617ef2aae017c28dc9f3fee6dd3a2c180194c2',
                     '1.0.0.0',
                     0,
                     '{"path":"C:\\Users\\pgmrd\\AppData\\Local\\MWPV\\early\\20250812160354042-KeyfileMissingOrCorrupt.elog"}',
                     'json+aesgcm',
                     1,
                     1,
                     '',
                     NULL,
                     NULL
                 );

INSERT INTO Logs (
                     Id,
                     CreatedUtc,
                     Level,
                     Source,
                     EventCode,
                     CorrelationId,
                     SessionId,
                     MachineId,
                     AppVersion,
                     IsCrash,
                     Payload,
                     PayloadFmt,
                     PayloadVer,
                     KeySetVersion,
                     StackHash,
                     Reserved1,
                     Reserved2
                 )
                 VALUES (
                     11,
                     1755017624,
                     'INFO',
                     'post-login',
                     'EARLY_LOGIN_FAILURES_PENDING',
                     NULL,
                     'bebbfd30c1234f17b3c3bdc010179d4e',
                     'f519b045de8ff1f411f7522cc3617ef2aae017c28dc9f3fee6dd3a2c180194c2',
                     '1.0.0.0',
                     0,
                     '{"decryptError":true}',
                     'json+aesgcm',
                     2,
                     1,
                     '',
                     NULL,
                     NULL
                 );

INSERT INTO Logs (
                     Id,
                     CreatedUtc,
                     Level,
                     Source,
                     EventCode,
                     CorrelationId,
                     SessionId,
                     MachineId,
                     AppVersion,
                     IsCrash,
                     Payload,
                     PayloadFmt,
                     PayloadVer,
                     KeySetVersion,
                     StackHash,
                     Reserved1,
                     Reserved2
                 )
                 VALUES (
                     12,
                     1755017624,
                     'INFO',
                     'SetupPasswordAndKeyFile',
                     'EARLY_LOGIN_FAILURE',
                     NULL,
                     'bebbfd30c1234f17b3c3bdc010179d4e',
                     'f519b045de8ff1f411f7522cc3617ef2aae017c28dc9f3fee6dd3a2c180194c2',
                     '1.0.0.0',
                     0,
                     '{"decryptError":true}',
                     'json+aesgcm',
                     2,
                     1,
                     '',
                     NULL,
                     NULL
                 );

INSERT INTO Logs (
                     Id,
                     CreatedUtc,
                     Level,
                     Source,
                     EventCode,
                     CorrelationId,
                     SessionId,
                     MachineId,
                     AppVersion,
                     IsCrash,
                     Payload,
                     PayloadFmt,
                     PayloadVer,
                     KeySetVersion,
                     StackHash,
                     Reserved1,
                     Reserved2
                 )
                 VALUES (
                     13,
                     1755017624,
                     'INFO',
                     'EarlyLoginIngest',
                     'EARLY_ELOG_DELETE_ATTEMPT',
                     NULL,
                     'bebbfd30c1234f17b3c3bdc010179d4e',
                     'f519b045de8ff1f411f7522cc3617ef2aae017c28dc9f3fee6dd3a2c180194c2',
                     '1.0.0.0',
                     0,
                     '{"decryptError":true}',
                     'json+aesgcm',
                     2,
                     1,
                     '',
                     NULL,
                     NULL
                 );

INSERT INTO Logs (
                     Id,
                     CreatedUtc,
                     Level,
                     Source,
                     EventCode,
                     CorrelationId,
                     SessionId,
                     MachineId,
                     AppVersion,
                     IsCrash,
                     Payload,
                     PayloadFmt,
                     PayloadVer,
                     KeySetVersion,
                     StackHash,
                     Reserved1,
                     Reserved2
                 )
                 VALUES (
                     14,
                     1755017624,
                     'INFO',
                     'EarlyLoginIngest',
                     'EARLY_ELOG_DELETED',
                     NULL,
                     'bebbfd30c1234f17b3c3bdc010179d4e',
                     'f519b045de8ff1f411f7522cc3617ef2aae017c28dc9f3fee6dd3a2c180194c2',
                     '1.0.0.0',
                     0,
                     '{"decryptError":true}',
                     'json+aesgcm',
                     2,
                     1,
                     '',
                     NULL,
                     NULL
                 );

INSERT INTO Logs (
                     Id,
                     CreatedUtc,
                     Level,
                     Source,
                     EventCode,
                     CorrelationId,
                     SessionId,
                     MachineId,
                     AppVersion,
                     IsCrash,
                     Payload,
                     PayloadFmt,
                     PayloadVer,
                     KeySetVersion,
                     StackHash,
                     Reserved1,
                     Reserved2
                 )
                 VALUES (
                     15,
                     1755018126,
                     'INFO',
                     'post-login',
                     'EARLY_LOGIN_FAILURES_PENDING',
                     NULL,
                     'ffbfb37e3220444b960b8e3e08e72568',
                     'f519b045de8ff1f411f7522cc3617ef2aae017c28dc9f3fee6dd3a2c180194c2',
                     '1.0.0.0',
                     0,
                     '{"decryptError":true}',
                     'json+aesgcm',
                     2,
                     1,
                     '',
                     NULL,
                     NULL
                 );

INSERT INTO Logs (
                     Id,
                     CreatedUtc,
                     Level,
                     Source,
                     EventCode,
                     CorrelationId,
                     SessionId,
                     MachineId,
                     AppVersion,
                     IsCrash,
                     Payload,
                     PayloadFmt,
                     PayloadVer,
                     KeySetVersion,
                     StackHash,
                     Reserved1,
                     Reserved2
                 )
                 VALUES (
                     16,
                     1755018126,
                     'INFO',
                     'SetupPasswordAndKeyFile',
                     'EARLY_LOGIN_FAILURE',
                     NULL,
                     'ffbfb37e3220444b960b8e3e08e72568',
                     'f519b045de8ff1f411f7522cc3617ef2aae017c28dc9f3fee6dd3a2c180194c2',
                     '1.0.0.0',
                     0,
                     '{"decryptError":true}',
                     'json+aesgcm',
                     2,
                     1,
                     '',
                     NULL,
                     NULL
                 );

INSERT INTO Logs (
                     Id,
                     CreatedUtc,
                     Level,
                     Source,
                     EventCode,
                     CorrelationId,
                     SessionId,
                     MachineId,
                     AppVersion,
                     IsCrash,
                     Payload,
                     PayloadFmt,
                     PayloadVer,
                     KeySetVersion,
                     StackHash,
                     Reserved1,
                     Reserved2
                 )
                 VALUES (
                     17,
                     1755018126,
                     'INFO',
                     'EarlyLoginIngest',
                     'EARLY_ELOG_DELETE_ATTEMPT',
                     NULL,
                     'ffbfb37e3220444b960b8e3e08e72568',
                     'f519b045de8ff1f411f7522cc3617ef2aae017c28dc9f3fee6dd3a2c180194c2',
                     '1.0.0.0',
                     0,
                     '{"decryptError":true}',
                     'json+aesgcm',
                     2,
                     1,
                     '',
                     NULL,
                     NULL
                 );

INSERT INTO Logs (
                     Id,
                     CreatedUtc,
                     Level,
                     Source,
                     EventCode,
                     CorrelationId,
                     SessionId,
                     MachineId,
                     AppVersion,
                     IsCrash,
                     Payload,
                     PayloadFmt,
                     PayloadVer,
                     KeySetVersion,
                     StackHash,
                     Reserved1,
                     Reserved2
                 )
                 VALUES (
                     18,
                     1755018126,
                     'INFO',
                     'EarlyLoginIngest',
                     'EARLY_ELOG_DELETED',
                     NULL,
                     'ffbfb37e3220444b960b8e3e08e72568',
                     'f519b045de8ff1f411f7522cc3617ef2aae017c28dc9f3fee6dd3a2c180194c2',
                     '1.0.0.0',
                     0,
                     '{"decryptError":true}',
                     'json+aesgcm',
                     2,
                     1,
                     '',
                     NULL,
                     NULL
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
