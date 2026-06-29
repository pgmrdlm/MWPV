
/* ============================================================================
   MWPV - 01.10 FRESH CREATE SCRIPT DRAFT

   Purpose:
   - Create a fresh database at schema version 01.10
   - Seed reference data required by the current schema
   - Stamp the database version immediately on fresh install

   01.10 changes reflected here:
   - AppSettings DisplayCategoriesWithItems option added

   01.09 changes reflected here:
   - Security Questions soft-deactivation column added
   - SecurityQuestionsTab logging templates and matching log_filters seeds added

   01.08 changes reflected here:
   - BankCardsTab changed-field logging templates added
   - Log Display filter data remains keyed by ComboType.Code = 'log_filters'

   01.07 changes reflected here:
   - Enhanced BankCardsTab logging templates added

   01.06 changes reflected here:
   - BankCardsTab logging templates and matching log_filters seeds added

   01.05 changes reflected here:
   - AppSettings added for user-defined password length settings

   01.04 changes reflected here:
   - BasicTab CategoryItem deactivation logging template added

   01.03 changes reflected here:
   - KeyArchiveIntegrity removed
   - CategoryItemAccounts uses the lean 01.01 structure with account type persistence
   - Restored account_types combo seeds with business-value sequences
   - Adds Accounts logging templates and matching log_filters seeds
============================================================================ */

PRAGMA encoding = "UTF-8";
PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

-- Drops for objects created by this script
DROP TABLE IF EXISTS ComboDetail;
DROP TABLE IF EXISTS ComboType;
DROP TABLE IF EXISTS CategoryItemPasswordHistory;
DROP TABLE IF EXISTS CategoryItemSecurityQuestions;
DROP TABLE IF EXISTS CategoryItemAccounts;
DROP TABLE IF EXISTS BankCards;
DROP TABLE IF EXISTS CategoryItem;
DROP TABLE IF EXISTS Category;
DROP TABLE IF EXISTS DbVersion;
DROP TABLE IF EXISTS AppSettings;
DROP TABLE IF EXISTS Logs;
DROP TABLE IF EXISTS LogMessageTemplate;

COMMIT;

PRAGMA foreign_keys = ON;

BEGIN TRANSACTION;

-- Lookup tables
CREATE TABLE ComboType (
    ComboTypeId   INTEGER PRIMARY KEY AUTOINCREMENT,
    Code          TEXT    NOT NULL UNIQUE,
    Description   TEXT    NOT NULL,
    Active        INTEGER NOT NULL DEFAULT 1 CHECK (Active IN (0,1)),
    CreatedUtc    TEXT    NOT NULL DEFAULT (datetime('now')),
    UpdatedUtc    TEXT    NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE ComboDetail (
    ComboDetailId INTEGER PRIMARY KEY AUTOINCREMENT,
    ComboTypeId   INTEGER NOT NULL REFERENCES ComboType (ComboTypeId) ON DELETE CASCADE,
    Seq           INTEGER NOT NULL,
    Code          TEXT    NOT NULL,
    Description   TEXT    NOT NULL,
    Active        INTEGER NOT NULL DEFAULT 1 CHECK (Active IN (0,1)),
    CreatedUtc    TEXT    NOT NULL DEFAULT (datetime('now')),
    UpdatedUtc    TEXT    NOT NULL DEFAULT (datetime('now'))
);

-- Categories
CREATE TABLE Category (
    Category_Key         INTEGER PRIMARY KEY AUTOINCREMENT,
    Category_Name        TEXT    NOT NULL UNIQUE,
    Category_Description TEXT,
    Category_Type        INTEGER NOT NULL DEFAULT 0,
    CreatedUtc           TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    IsActive             INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0,1))
);

-- Category items
CREATE TABLE CategoryItem (
    ItemId                INTEGER PRIMARY KEY AUTOINCREMENT,
    Category_Key          INTEGER NOT NULL REFERENCES Category (Category_Key) ON DELETE CASCADE,
    CI_Name               TEXT    NOT NULL,
    CI_Description        TEXT,
    CI_Username           TEXT,
    CI_SignInUrl          TEXT,
    CI_BookMarkOnly       INTEGER NOT NULL DEFAULT 0 CHECK (CI_BookMarkOnly IN (0,1)),
    CI_AccountEmail       BLOB,
    CI_AccountPhoneNumber BLOB,
    CI_Pin                BLOB,
    CI_CreateUTC          INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    CI_UpdateUTC          INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    IsActive              INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0,1)),
    CHECK (length(trim(CI_Name)) > 0),
    UNIQUE (Category_Key, CI_Name COLLATE NOCASE)
);

-- Password history
CREATE TABLE CategoryItemPasswordHistory (
    CIPaH_PwHistId  INTEGER PRIMARY KEY AUTOINCREMENT,
    CIPaH_ItemId    INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    CIPaH_CreatedAt REAL    NOT NULL DEFAULT (julianday('now')),
    CIPaH_Version   INTEGER NOT NULL DEFAULT 1,
    CIPaH_Password  BLOB    NOT NULL,
    CIPaH_PadLen    INTEGER,
    CIPaH_PwFp      BLOB    NOT NULL,
    CIPaH_FpVersion INTEGER NOT NULL DEFAULT 1
);

CREATE INDEX IF NOT EXISTS IX_CategoryItemPasswordHistory_Item_CreatedAt
ON CategoryItemPasswordHistory (CIPaH_ItemId, CIPaH_CreatedAt, CIPaH_PwHistId);

CREATE INDEX IF NOT EXISTS IX_CategoryItemPasswordHistory_PwFp
ON CategoryItemPasswordHistory (CIPaH_PwFp);

-- Security questions
CREATE TABLE CategoryItemSecurityQuestions (
    CISQ_Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    CISQ_ItemId    INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    CISQ_Seq       INTEGER NOT NULL,
    CISQ_Question  BLOB    NOT NULL,
    CISQ_Answer    BLOB    NOT NULL,
    CISQ_IsActive  INTEGER NOT NULL DEFAULT 1 CHECK (CISQ_IsActive IN (0,1)),
    CISQ_CreatedAt INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    CISQ_UpdatedAt INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    UNIQUE (CISQ_ItemId, CISQ_Seq)
);

CREATE INDEX IF NOT EXISTS IX_CategoryItemSecurityQuestions_Item_Active_Seq
ON CategoryItemSecurityQuestions (CISQ_ItemId, CISQ_IsActive, CISQ_Seq);

-- Bank cards
CREATE TABLE BankCards (
    BC_Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    BC_ItemId     INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    BC_CardType   INTEGER NOT NULL REFERENCES ComboDetail (ComboDetailId),
    BC_Cardholder TEXT,
    BC_Number     BLOB    NOT NULL,
    BC_ExpMonth   INTEGER NOT NULL CHECK (BC_ExpMonth BETWEEN 1 AND 12),
    BC_ExpYear    INTEGER NOT NULL,
    BC_CVV        BLOB,
    BC_Pin        BLOB,
    BC_BillingZip BLOB,
    BC_IsPrimary  INTEGER NOT NULL DEFAULT 0 CHECK (BC_IsPrimary IN (0,1)),
    BC_IsActive   INTEGER NOT NULL DEFAULT 1 CHECK (BC_IsActive IN (0,1)),
    BC_CreatedAt  INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    BC_UpdatedAt  INTEGER NOT NULL DEFAULT (strftime('%s','now'))
);

-- Category item accounts (lean 01.01 structure)
CREATE TABLE CategoryItemAccounts (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId              INTEGER NOT NULL REFERENCES CategoryItem (ItemId) ON DELETE CASCADE,
    Label               TEXT,
    Number              BLOB    NOT NULL,
    AccountTypeId       INTEGER REFERENCES ComboDetail (ComboDetailId),
    AccountTypeFreeform TEXT,
    CreatedAt           INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    UpdatedAt           INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    IsActive            INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0,1))
);

-- Database version history
CREATE TABLE DbVersion (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Version     TEXT    NOT NULL,
    AppliedOn   TEXT    NOT NULL,
    Description TEXT,
    IsCurrent   INTEGER NOT NULL CHECK (IsCurrent IN (0, 1))
);

-- Application settings
CREATE TABLE AppSettings (
    AS_PW_Minimum          INTEGER NOT NULL,
    AS_PW_Incriments       INTEGER NOT NULL,
    AS_PW_Inctriment_Steps INTEGER NOT NULL,
    AS_DisplayCategoriesWithItems INTEGER NOT NULL DEFAULT 1 CHECK (AS_DisplayCategoriesWithItems IN (0,1))
);

-- Log message templates
CREATE TABLE LogMessageTemplate (
    LMT_Id      INTEGER PRIMARY KEY AUTOINCREMENT,
    UpdateForm  TEXT    NOT NULL,
    Seq         INTEGER NOT NULL,
    LogMessage  TEXT    NOT NULL,
    Active      INTEGER NOT NULL DEFAULT 1 CHECK (Active IN (0,1)),
    CreatedUtc  TEXT    NOT NULL DEFAULT (datetime('now')),
    UpdatedUtc  TEXT    NOT NULL DEFAULT (datetime('now')),
    UNIQUE (UpdateForm, Seq)
);

CREATE INDEX IF NOT EXISTS IX_LogMessageTemplate_Form_Seq
ON LogMessageTemplate (UpdateForm, Seq);

-- Application logs
CREATE TABLE Logs (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    WhenUtc       TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now')),
    CreatedUtc    TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ','now')),
    Level         TEXT    NOT NULL CHECK (UPPER(Level) IN ('TRACE','DEBUG','INFO','WARN','WARNING','ERROR','FATAL')),
    Source        TEXT,
    EventCode     TEXT,
    SessionId     TEXT    NOT NULL DEFAULT '',
    LoginId       TEXT,
    ItemId        INTEGER,
    SubjectText   TEXT,
    MessageText   TEXT,
    MachineId     TEXT,
    DeviceMake    TEXT,
    DeviceModel   TEXT,
    OSVersion     TEXT,
    DeviceIdHash  TEXT,
    InstallType   TEXT,
    AppVersion    TEXT    NOT NULL DEFAULT '',
    IsCrash       INTEGER NOT NULL DEFAULT 0 CHECK (IsCrash IN (0,1)),
    KeySetVersion INTEGER NOT NULL DEFAULT 1,
    StackHash     TEXT
);

COMMIT;

BEGIN TRANSACTION;

-- Log message template seeds
INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT v.UpdateForm, v.Seq, v.LogMessage, 1
FROM (
    SELECT 'BasicTab' AS UpdateForm, 1 AS Seq,
           'Category Item #CategoryItemName# has been created for Category #CategoryName#' AS LogMessage
    UNION ALL SELECT 'BasicTab', 2,  'The following updates have been saved for #CategoryItemName#'
    UNION ALL SELECT 'BasicTab', 3,  '- Password updated'
    UNION ALL SELECT 'BasicTab', 4,  '- Bookmark flag toggled'
    UNION ALL SELECT 'BasicTab', 5,  '- PIN updated'
    UNION ALL SELECT 'BasicTab', 6,  '- User name updated'
    UNION ALL SELECT 'BasicTab', 7,  '- URL/Location updated'
    UNION ALL SELECT 'BasicTab', 8,  '- Phone number updated'
    UNION ALL SELECT 'BasicTab', 9,  '- Email updated'
    UNION ALL SELECT 'BasicTab', 10, '- Notes updated'
    UNION ALL SELECT 'BasicTab', 11, 'Edits were discarded for #CategoryItemName# (no changes saved)'
    UNION ALL SELECT 'BasicTab', 12, '- Category Item #CategoryItemName# was deactivated'
) AS v
WHERE NOT EXISTS (
    SELECT 1
    FROM LogMessageTemplate t
    WHERE t.UpdateForm = v.UpdateForm
      AND t.Seq        = v.Seq
);

INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT v.UpdateForm, v.Seq, v.LogMessage, 1
FROM (
    SELECT 'CategoryUpdates' AS UpdateForm,
           1 AS Seq,
           'Category #CategoryName# has been created' AS LogMessage
) AS v
WHERE NOT EXISTS (
    SELECT 1
    FROM LogMessageTemplate t
    WHERE t.UpdateForm = v.UpdateForm
      AND t.Seq        = v.Seq
);

INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT v.UpdateForm, v.Seq, v.LogMessage, 1
FROM (
    SELECT 'AccountsTab' AS UpdateForm,
           1 AS Seq,
           'Account #AccountTypeDisplay# (#AccountNumberMasked#) has been created for #CategoryItemName#' AS LogMessage
    UNION ALL
    SELECT 'AccountsTab',
           2,
           'Account #AccountTypeDisplay# (#AccountNumberMasked#) has been deactivated for #CategoryItemName#'
) AS v
WHERE NOT EXISTS (
    SELECT 1
    FROM LogMessageTemplate t
    WHERE t.UpdateForm = v.UpdateForm
      AND t.Seq        = v.Seq
);

INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT v.UpdateForm, v.Seq, v.LogMessage, 1
FROM (
    SELECT 'BankCardsTab' AS UpdateForm,
           1 AS Seq,
           'Bank card has been created for #CategoryItemName#' AS LogMessage
    UNION ALL
    SELECT 'BankCardsTab',
           2,
           'Bank card has been updated for #CategoryItemName#'
    UNION ALL
    SELECT 'BankCardsTab',
           3,
           'Bank card has been deactivated for #CategoryItemName#'
    UNION ALL
    SELECT 'BankCardsTab',
           4,
           'Bank card #BankCardDisplayName# has been created for #CategoryItemName#'
    UNION ALL
    SELECT 'BankCardsTab',
           5,
           'Bank card #BankCardDisplayName# has been updated for #CategoryItemName#'
    UNION ALL
    SELECT 'BankCardsTab',
           6,
           'Bank card #BankCardDisplayName# has been deactivated for #CategoryItemName#'
    UNION ALL
    SELECT 'BankCardsTab',
           7,
           '- Card type changed'
    UNION ALL
    SELECT 'BankCardsTab',
           8,
           '- Cardholder changed'
    UNION ALL
    SELECT 'BankCardsTab',
           9,
           '- Expiration changed'
    UNION ALL
    SELECT 'BankCardsTab',
           10,
           '- Active state changed'
    UNION ALL
    SELECT 'BankCardsTab',
           11,
           '- Card number changed'
    UNION ALL
    SELECT 'BankCardsTab',
           12,
           '- CVV changed'
    UNION ALL
    SELECT 'BankCardsTab',
           13,
           '- PIN changed'
    UNION ALL
    SELECT 'BankCardsTab',
           14,
           '- Billing ZIP changed'
) AS v
WHERE NOT EXISTS (
    SELECT 1
    FROM LogMessageTemplate t
    WHERE t.UpdateForm = v.UpdateForm
      AND t.Seq        = v.Seq
);

INSERT INTO LogMessageTemplate (UpdateForm, Seq, LogMessage, Active)
SELECT v.UpdateForm, v.Seq, v.LogMessage, 1
FROM (
    SELECT 'SecurityQuestionsTab' AS UpdateForm,
           1 AS Seq,
           'Security question has been created for #CategoryItemName#' AS LogMessage
    UNION ALL
    SELECT 'SecurityQuestionsTab',
           2,
           'Security question has been updated for #CategoryItemName#'
    UNION ALL
    SELECT 'SecurityQuestionsTab',
           3,
           'Security question has been deactivated for #CategoryItemName#'
) AS v
WHERE NOT EXISTS (
    SELECT 1
    FROM LogMessageTemplate t
    WHERE t.UpdateForm = v.UpdateForm
      AND t.Seq        = v.Seq
);

-- Reference ComboType seeds
INSERT INTO ComboType (Code, Description, Active)
SELECT 'account_types', 'Common financial account types', 1
WHERE NOT EXISTS (
    SELECT 1
    FROM ComboType
    WHERE Code = 'account_types'
);

INSERT INTO ComboType (Code, Description, Active)
SELECT 'credit_cards', 'Credit/Bank card options (mixed)', 1
WHERE NOT EXISTS (
    SELECT 1
    FROM ComboType
    WHERE Code = 'credit_cards'
);

INSERT INTO ComboType (Code, Description, Active)
SELECT 'log_filters', 'Log filter values for the Logs UI', 1
WHERE NOT EXISTS (
    SELECT 1
    FROM ComboType
    WHERE Code = 'log_filters'
);

INSERT INTO ComboType (Code, Description, Active)
SELECT 'basic_change_fields', 'Basic tab: changed field descriptors', 1
WHERE NOT EXISTS (
    SELECT 1
    FROM ComboType
    WHERE Code = 'basic_change_fields'
);

-- Account type reference values
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT
    ct.ComboTypeId,
    v.Seq,
    v.Code,
    v.Description,
    1
FROM ComboType ct
JOIN (
    SELECT 0  AS Seq, 'PRIMARY'            AS Code, 'Primary'            AS Description
    UNION ALL SELECT 1,  'CHECKING',           'Checking'
    UNION ALL SELECT 2,  'SAVINGS',            'Savings'
    UNION ALL SELECT 3,  'CHRISTMAS_SAVINGS',  'Christmas Savings'
    UNION ALL SELECT 4,  'IRA',                'IRA'
    UNION ALL SELECT 5,  'LOAN',               'Loan'
    UNION ALL SELECT 6,  'MORTGAGE',           'Mortgage'
    UNION ALL SELECT 99, 'FREEFORM',           'Freeform'
) AS v
WHERE ct.Code = 'account_types'
  AND NOT EXISTS (
      SELECT 1
      FROM ComboDetail cd
      WHERE cd.ComboTypeId = ct.ComboTypeId
        AND cd.Code        = v.Code
  );

-- Credit card reference values
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT
    ct.ComboTypeId,
    v.Seq,
    v.Code,
    v.Description,
    1
FROM ComboType ct
JOIN (
    SELECT 0 AS Seq, 'DEBIT_CARD' AS Code, 'Debit card' AS Description
    UNION ALL SELECT 1, 'MASTERCARD', 'Mastercard'
    UNION ALL SELECT 2, 'VISA', 'Visa'
    UNION ALL SELECT 3, 'AMERICAN_EXPRESS', 'American Express'
    UNION ALL SELECT 4, 'DISCOVER', 'Discover'
    UNION ALL SELECT 5, 'STORE_CARD', 'Store card (private label)'
    UNION ALL SELECT 6, 'VIRTUAL_CARD', 'Virtual card'
    UNION ALL SELECT 99, 'FREEFORM', 'Freeform'
) AS v
WHERE ct.Code = 'credit_cards'
  AND NOT EXISTS (
      SELECT 1
      FROM ComboDetail cd
      WHERE cd.ComboTypeId = ct.ComboTypeId
        AND cd.Code        = v.Code
  );

-- Log filter values
WITH v(Seq, Code, Description) AS (
    VALUES
      (0,  'CATEGORY_DUPLICATE',   'Duplicate category detected'),
      (1,  'CATEGORY_CREATED',     'Category successfully inserted'),
      (2,  'LOGIN',                'Login events'),
      (3,  'EARLY_FAIL',           'Early-fail events'),
      (4,  'SESSION_START',        'Session started (post-login)'),
      (5,  'SESSION_END',          'Session ended'),
      (10, 'CATEGORYITEM_CREATED', 'Category item created'),
      (11, 'CATEGORYITEM_CHANGED', 'Category item updated (one or more fields)'),
      (12, 'ACCOUNTS_CREATED',     'Accounts Created'),
      (13, 'ACCOUNTS_CHANGED',     'Accounts Changed'),
      (14, 'BANKCARD_CREATED',     'Bank card created'),
      (15, 'BANKCARD_CHANGED',     'Bank card changed'),
      (16, 'BANKCARD_DEACTIVATED', 'Bank card deactivated'),
      (17, 'SECURITYQUESTION_CREATED', 'Security question created'),
      (18, 'SECURITYQUESTION_CHANGED', 'Security question changed'),
      (19, 'SECURITYQUESTION_DEACTIVATED', 'Security question deactivated')

)
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT
    ct.ComboTypeId,
    v.Seq,
    v.Code,
    v.Description,
    1
FROM ComboType ct
CROSS JOIN v
WHERE ct.Code = 'log_filters'
  AND NOT EXISTS (
      SELECT 1
      FROM ComboDetail d
      WHERE d.ComboTypeId = ct.ComboTypeId
        AND d.Code        = v.Code
  );

-- Starter categories
INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Utilities', 'Bills and essential services', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Utilities');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Government', 'Government portals & services', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Government');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Banks', 'Banks & credit unions', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Banks');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Shopping', 'Retail & e-commerce', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Shopping');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Entertainment', 'Streaming & media', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Entertainment');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Healthcare', 'Health & medical portals', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Healthcare');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Insurance', 'Insurance accounts', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Insurance');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Social', 'Social networks & messaging', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Social');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Email', 'Email providers & identity', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Email');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Cloud', 'Cloud & hosting', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Cloud');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Development', 'Dev tools & repos', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Development');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Education', 'Schools, courses, LMS', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Education');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Travel', 'Airlines, hotels, transport', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Travel');

INSERT INTO Category (Category_Name, Category_Description, Category_Type, IsActive)
SELECT 'Misc', 'Everything else', 0, 1
WHERE NOT EXISTS (SELECT 1 FROM Category WHERE Category_Name = 'Misc');

-- Basic tab change descriptors
INSERT INTO ComboDetail (ComboTypeId, Seq, Code, Description, Active)
SELECT
    ct.ComboTypeId,
    v.Seq,
    v.Code,
    v.Description,
    1
FROM ComboType ct
JOIN (
    SELECT 10 AS Seq, 'CATEGORYITEM_CREATED' AS Code, 'Category item created' AS Description
    UNION ALL SELECT 11, 'CATEGORYITEM_CHANGED', 'Category item updated (one or more fields)'
    UNION ALL SELECT 12, 'EARLY_FAIL', 'Login failures'
    UNION ALL SELECT 13, 'CATEGORY_CREATED', 'Category successfully added'
) AS v
WHERE ct.Code = 'basic_change_fields'
  AND NOT EXISTS (
      SELECT 1
      FROM ComboDetail cd
      WHERE cd.ComboTypeId = ct.ComboTypeId
        AND cd.Code        = v.Code
  );

-- Default application settings
INSERT INTO AppSettings (
    AS_PW_Minimum,
    AS_PW_Incriments,
    AS_PW_Inctriment_Steps,
    AS_DisplayCategoriesWithItems
)
SELECT
    12,
    10,
    10,
    1
WHERE NOT EXISTS (SELECT 1 FROM AppSettings);

-- Fresh install database version stamp
INSERT INTO DbVersion (Version, AppliedOn, Description, IsCurrent)
SELECT
    '01.10',
    strftime('%Y-%m-%dT%H:%M:%SZ','now'),
    'Fresh database created at version 01.10',
    1
WHERE NOT EXISTS (SELECT 1 FROM DbVersion);

COMMIT;
